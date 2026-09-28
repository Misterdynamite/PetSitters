using System;
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
        private readonly BookingRepository _bookings;

        public BookingService(BookingRepository bookings)
        {
            _bookings = bookings ?? throw new ArgumentNullException(nameof(bookings));
        }

        /// <summary>
        /// A sitter accepts a pending request (REQ-PS-03), unless it overlaps a
        /// booking that sitter has already accepted (REQ-GR-08). On rejection
        /// nothing is written, so the request simply stays pending.
        /// </summary>
        public BookingResult AcceptRequest(int bookingId, int sitterUserId)
        {
            Booking booking = _bookings.GetById(bookingId);

            // Guards: a sitter may only act on their own pending requests.
            if (booking == null || booking.SitterUserId != sitterUserId)
                return BookingResult.Fail("That booking request could not be found.");
            if (booking.Status != BookingStatus.Pending)
                return BookingResult.Fail("Only pending requests can be accepted.");

            // Only *accepted* bookings block: other pending requests for the same
            // dates are just competing offers, and the sitter is free to pick one.
            Booking clash = _bookings.GetForSitter(sitterUserId)
                .Where(b => b.Id != booking.Id && b.Status == BookingStatus.Accepted)
                .OrderBy(b => b.StartDate)
                .FirstOrDefault(b => b.Overlaps(booking));

            if (clash != null)
                return BookingResult.Fail(
                    $"This request overlaps a booking you have already accepted " +
                    $"({clash.StartDate:d MMM yyyy} – {clash.EndDate:d MMM yyyy}). " +
                    "It has not been accepted and will stay pending.");

            _bookings.UpdateStatus(booking.Id, BookingStatus.Accepted);
            booking.Status = BookingStatus.Accepted;
            return BookingResult.Ok(booking);
        }

        /// <summary>
        /// An owner sends a booking request (REQ-PO-04), unless the same animal
        /// already has a live booking over overlapping dates, with this sitter
        /// or any other (REQ-PO-08). On success the booking is stored as Pending.
        /// Date-range validation (REQ-GR-04) is still done by the form, not here.
        /// </summary>
        public BookingResult RequestBooking(Booking booking)
        {
            if (booking == null) throw new ArgumentNullException(nameof(booking));

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
            if (!booking.IsActive)
                return BookingResult.Fail(
                    $"This booking is already {booking.Status.ToString().ToLowerInvariant()} and cannot be cancelled.");

            _bookings.UpdateStatus(booking.Id, BookingStatus.Cancelled);
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

        public static BookingResult Ok(Booking booking)
        {
            return new BookingResult { Success = true, Booking = booking };
        }

        public static BookingResult Fail(string message)
        {
            return new BookingResult { Success = false, ErrorMessage = message };
        }
    }
}
