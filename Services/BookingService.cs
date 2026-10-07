using System;
using System.Collections.Generic;
using System.Linq;
using PetSitters.Data;
using PetSitters.Models;

namespace PetSitters.Services
{
    /// <summary>
    /// Booking state transitions that carry business rules, kept out of the
    /// WPF views so they can be tested without a window. Like
    /// <see cref="AuthService"/>, it returns a result object rather than
    /// throwing, so the UI can show the message and tests can assert on it.
    /// </summary>
    public class BookingService
    {
        /// <summary>REQ-GR-04: shortest bookable duration.</summary>
        public static readonly TimeSpan MinimumDuration = TimeSpan.FromHours(1);

        /// <summary>REQ-GR-04: longest bookable duration (exactly 14 days is allowed).</summary>
        public static readonly TimeSpan MaximumDuration = TimeSpan.FromDays(14);

        private readonly BookingRepository _bookings;
        private readonly PetRepository _pets;
        private readonly Func<DateTime> _now;

        /// <param name="now">
        /// Clock used for the "start is in the past" rule. Defaults to the
        /// system clock; tests inject a fixed time so results don't depend on
        /// the day they run.
        /// </param>
        public BookingService(BookingRepository bookings, PetRepository pets, Func<DateTime> now = null)
        {
            _bookings = bookings ?? throw new ArgumentNullException(nameof(bookings));
            _pets = pets ?? throw new ArgumentNullException(nameof(pets));
            _now = now ?? (() => DateTime.Now);
        }

        /// <summary>
        /// A sitter accepts a pending request (REQ-PS-03), unless it overlaps a
        /// booking that sitter has already accepted (REQ-GR-08). On rejection
        /// nothing is written, so the request simply stays pending.
        /// </summary>
        public BookingResult AcceptRequest(int bookingId, int sitterUserId)
        {
            // One query serves both the guard and the overlap check: the request
            // must be among this sitter's own bookings (otherwise it's someone
            // else's, or doesn't exist). That saves a round trip to the cloud database.
            List<Booking> sittersBookings = _bookings.GetForSitter(sitterUserId);
            Booking booking = sittersBookings.FirstOrDefault(b => b.Id == bookingId);

            // Guards: a sitter may only act on their own pending requests.
            if (booking == null)
                return BookingResult.Fail("That booking request could not be found.");
            // The sitter's list showed it as pending; if it isn't now, the owner
            // changed it in the meantime (shared database): say so, and have the
            // view reload its stale list.
            if (booking.Status != BookingStatus.Pending)
                return ChangedElsewhere(booking.Status);

            // Only *accepted* bookings block: other pending requests for the same
            // dates are just competing offers, and the sitter is free to pick one.
            Booking clash = sittersBookings
                .Where(b => b.Id != booking.Id && b.Status == BookingStatus.Accepted)
                .OrderBy(b => b.StartDate)
                .FirstOrDefault(b => b.Overlaps(booking));

            if (clash != null)
                return BookingResult.Fail(
                    $"This request overlaps a booking you have already accepted " +
                    $"({clash.StartDate:d MMM yyyy} – {clash.EndDate:d MMM yyyy}). " +
                    "It has not been accepted and will stay pending.");

            // Pending -> Accepted only if it's STILL pending: the owner may have
            // cancelled it from another computer since this sitter's list loaded.
            if (!_bookings.TryUpdateStatus(booking.Id, BookingStatus.Accepted, BookingStatus.Pending))
                return ChangedElsewhere(booking.Id);

            booking.Status = BookingStatus.Accepted;
            return BookingResult.Ok(booking);
        }

        /// <summary>
        /// A sitter declines one of their own pending requests (REQ-PS-03).
        /// Conditional, like every transition here: refused if it's no longer
        /// pending (e.g. the owner cancelled it in the meantime).
        /// </summary>
        public BookingResult DeclineRequest(int bookingId, int sitterUserId)
        {
            Booking booking = _bookings.GetById(bookingId);
            if (booking == null || booking.SitterUserId != sitterUserId)
                return BookingResult.Fail("That booking request could not be found.");
            if (booking.Status != BookingStatus.Pending)
                return ChangedElsewhere(booking.Status);

            if (!_bookings.TryUpdateStatus(booking.Id, BookingStatus.Declined, BookingStatus.Pending))
                return ChangedElsewhere(booking.Id);

            booking.Status = BookingStatus.Declined;
            return BookingResult.Ok(booking);
        }

        /// <summary>
        /// A sitter cancels one of their own ACCEPTED bookings (from My Chats).
        /// Only accepted ones: a pending request is declined instead.
        /// </summary>
        public BookingResult CancelAsSitter(int bookingId, int sitterUserId)
        {
            Booking booking = _bookings.GetById(bookingId);
            if (booking == null || booking.SitterUserId != sitterUserId)
                return BookingResult.Fail("Booking not found, or you are not the sitter for this booking.");
            // Only accepted bookings are listed under My Chats, so anything else
            // means the owner changed it in the meantime.
            if (booking.Status != BookingStatus.Accepted)
                return ChangedElsewhere(booking.Status);

            if (!_bookings.TryUpdateStatus(booking.Id, BookingStatus.Cancelled, BookingStatus.Accepted))
                return ChangedElsewhere(booking.Id);

            booking.Status = BookingStatus.Cancelled;
            return BookingResult.Ok(booking);
        }

        /// <summary>
        /// The result when a conditional status change found the booking had
        /// already moved on, i.e. the other person acted first on another computer.
        /// </summary>
        private BookingResult ChangedElsewhere(int bookingId)
        {
            // Lost the race in the conditional UPDATE itself: re-read to report the new state.
            Booking now = _bookings.GetById(bookingId);
            return Stale(now == null ? "removed" : now.Status.ToString().ToLowerInvariant());
        }

        private static BookingResult ChangedElsewhere(BookingStatus current)
        {
            return Stale(current.ToString().ToLowerInvariant());
        }

        private static BookingResult Stale(string state)
        {
            return BookingResult.Stale(
                $"This booking was changed before your action was saved (it is now {state}). " +
                "The lists have been refreshed.");
        }

        /// <summary>
        /// An owner sends a booking request (REQ-PO-04). It must first pass the
        /// REQ-GR-04 form rules (<see cref="ValidateRequest"/>), then the same
        /// animal must not already have a live booking over overlapping dates,
        /// with this sitter or any other (REQ-PO-08). On success the booking is
        /// stored as Pending; on any rejection nothing is saved.
        /// </summary>
        public BookingResult RequestBooking(Booking booking)
        {
            if (booking == null) throw new ArgumentNullException(nameof(booking));

            string invalid = ValidateRequest(booking);
            if (invalid != null)
                return BookingResult.Fail(invalid);

            // Both pending and accepted bookings block. A pending request already
            // claims the pet for those dates; to switch sitters, the owner cancels
            // it first (REQ-PO-07). Declined/cancelled ones free the pet again.
            Booking clash = _bookings.GetForOwner(booking.OwnerUserId)
                .Where(b => b.IsActive && b.SharesPetWith(booking))
                .OrderBy(b => b.StartDate)
                .FirstOrDefault(b => b.Overlaps(booking));

            if (clash != null)
            {
                // "All my pets" on either side means we can't name a single pet.
                string who = booking.PetId.HasValue && clash.PetId.HasValue
                    ? "This pet already has"
                    : "One of these pets already has";
                return BookingResult.Fail(
                    $"{who} a {clash.Status.ToString().ToLowerInvariant()} booking " +
                    $"({clash.StartDate:d MMM yyyy} – {clash.EndDate:d MMM yyyy}) that overlaps these dates. " +
                    "Cancel it under My Bookings or choose different dates.");
            }

            booking.Status = BookingStatus.Pending;
            _bookings.Insert(booking);
            return BookingResult.Ok(booking);
        }

        /// <summary>
        /// REQ-GR-04 form rules, checked in the order a user would fix them.
        /// Returns a specific message for the first rule broken, or null if valid.
        /// Public so the rules can be tested without touching the database's
        /// booking rows.
        /// </summary>
        public string ValidateRequest(Booking booking)
        {
            if (booking == null) throw new ArgumentNullException(nameof(booking));

            // The form captures dates, not times, so "in the past" is judged by
            // day: a booking starting today is allowed. If time pickers are ever
            // added, compare against _now() itself instead of its date.
            if (booking.StartDate.Date < _now().Date)
                return "Start date cannot be in the past.";

            if (booking.EndDate <= booking.StartDate)
                return "End date must be after the start date.";

            // Unreachable from the date-only form (end > start means at least a
            // day), but enforced so the rule holds for any caller and for times.
            TimeSpan duration = booking.EndDate - booking.StartDate;
            if (duration < MinimumDuration)
                return "A booking must be at least 1 hour long.";

            if (duration > MaximumDuration)
                return "A booking can be at most 14 days long. Choose an earlier end date.";

            // "No pet selected": the request must name one of the owner's pets,
            // or "All my pets" (null), which only makes sense if they have any.
            var ownersPets = _pets.GetByOwner(booking.OwnerUserId);
            if (booking.PetId.HasValue)
            {
                if (!ownersPets.Any(p => p.Id == booking.PetId.Value))
                    return "Please select one of your pets for this booking.";
            }
            else if (ownersPets.Count == 0)
            {
                return "Please select a pet for this booking. Add your pet under My Pets first.";
            }

            return null;
        }

        /// <summary>
        /// An owner cancels one of their own bookings while it is pending or
        /// accepted (REQ-PO-07). The row is kept with status Cancelled, so it
        /// stays on the owner's record but leaves the sitter's queue and chats.
        /// </summary>
        public BookingResult CancelBooking(int bookingId, int ownerUserId)
        {
            Booking booking = _bookings.GetById(bookingId);

            // Guards: only the booking's owner may cancel, and only a live booking.
            if (booking == null || booking.OwnerUserId != ownerUserId)
                return BookingResult.Fail("That booking could not be found.");
            // The owner's list showed it as live; if it isn't now, the sitter
            // declined it in the meantime (or it was already cancelled elsewhere).
            if (!booking.IsActive)
                return ChangedElsewhere(booking.Status);

            // Only if still pending or accepted: the sitter may have declined it
            // (or accepted it) from another computer since this list loaded.
            if (!_bookings.TryUpdateStatus(booking.Id, BookingStatus.Cancelled, BookingStatus.Pending, BookingStatus.Accepted))
                return ChangedElsewhere(booking.Id);

            booking.Status = BookingStatus.Cancelled;
            return BookingResult.Ok(booking);
        }
    }

    /// <summary>Outcome of a booking action such as <see cref="BookingService.AcceptRequest"/>.</summary>
    public class BookingResult
    {
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }
        public Booking Booking { get; private set; }

        /// <summary>
        /// Failed because someone else changed the booking first (shared database).
        /// The screen's lists are out of date, so the view should reload them.
        /// </summary>
        public bool ChangedElsewhere { get; private set; }

        public static BookingResult Ok(Booking booking)
        {
            return new BookingResult { Success = true, Booking = booking };
        }

        public static BookingResult Fail(string message)
        {
            return new BookingResult { Success = false, ErrorMessage = message };
        }

        public static BookingResult Stale(string message)
        {
            return new BookingResult { Success = false, ErrorMessage = message, ChangedElsewhere = true };
        }
    }
}
