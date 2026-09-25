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
