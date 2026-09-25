using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Integration tests for <see cref="BookingService.AcceptRequest"/> against a
    /// real isolated SQLite database. Covers REQ-GR-08 (a sitter cannot accept a
    /// booking overlapping one they have already accepted; the rejected request
    /// stays pending) and the REQ-PS-03 guards on who may accept what.
    /// </summary>
    [TestClass]
    public class BookingServiceTests : DatabaseTestBase
    {
        private int _ownerId;
        private int _otherOwnerId;
        private int _sitterId;

        [TestInitialize]
        public void GivenTwoOwnersAndASitter()
        {
            // Runs after DatabaseTestBase.InitDatabase (base-class initialisers run first).
            _ownerId = Services.Auth.Register("olivia@test.com", "secret1", UserRole.Owner,
                "Olivia", "021", "Wellington").User.Id;
            _otherOwnerId = Services.Auth.Register("olive@test.com", "secret1", UserRole.Owner,
                "Olive", "021", "Wellington").User.Id;
            _sitterId = Services.Auth.Register("sam@test.com", "secret1", UserRole.Sitter,
                "Sam", "022", "Wellington").User.Id;
        }

        [TestMethod]
        // REQ-PS-03
        public void AcceptRequest_WithNoClash_AcceptsAndPersists()
        {
            Booking booking = Request(_ownerId, startDay: 10, endDay: 13);

            BookingResult result = Services.BookingActions.AcceptRequest(booking.Id, _sitterId);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(BookingStatus.Accepted, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        // REQ-GR-08 / FR-07: Olivia's booking accepted, Olive requests the same dates.
        public void AcceptRequest_OverlappingAnAcceptedBooking_IsRejectedAndStaysPending()
        {
            Booking first = Request(_ownerId, startDay: 10, endDay: 13);
            Booking second = Request(_otherOwnerId, startDay: 12, endDay: 15);
            Assert.IsTrue(Services.BookingActions.AcceptRequest(first.Id, _sitterId).Success);

            BookingResult result = Services.BookingActions.AcceptRequest(second.Id, _sitterId);

            Assert.IsFalse(result.Success, "An overlapping booking must not be accepted.");
            StringAssert.Contains(result.ErrorMessage, "stay pending",
                "REQ-GR-08 requires telling the sitter the booking stays pending.");
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(second.Id).Status,
                "The rejected request must remain pending, not be declined.");
            Assert.AreEqual(BookingStatus.Accepted, Services.Bookings.GetById(first.Id).Status,
                "The already-accepted booking must be untouched.");
        }

        [TestMethod]
        // REQ-GR-08 boundary: hand-back day == next start day is not a clash.
        public void AcceptRequest_BackToBackWithAcceptedBooking_IsAccepted()
        {
            Booking first = Request(_ownerId, startDay: 10, endDay: 13);
            Booking second = Request(_otherOwnerId, startDay: 13, endDay: 15);
            Services.BookingActions.AcceptRequest(first.Id, _sitterId);

            BookingResult result = Services.BookingActions.AcceptRequest(second.Id, _sitterId);

            Assert.IsTrue(result.Success, result.ErrorMessage);
        }

        /// <summary>
        /// Only ACCEPTED bookings block. Overlapping pending, declined and
        /// cancelled bookings are equivalence partitions that must not.
        /// </summary>
        [DataTestMethod]
        [DataRow(BookingStatus.Pending)]
        [DataRow(BookingStatus.Declined)]
        [DataRow(BookingStatus.Cancelled)]
        public void AcceptRequest_OverlappingANonAcceptedBooking_IsAccepted(BookingStatus otherStatus)
        {
            Booking other = Request(_ownerId, startDay: 10, endDay: 13);
            Services.Bookings.UpdateStatus(other.Id, otherStatus);
            Booking booking = Request(_otherOwnerId, startDay: 11, endDay: 12);

            BookingResult result = Services.BookingActions.AcceptRequest(booking.Id, _sitterId);

            Assert.IsTrue(result.Success,
                "A " + otherStatus + " booking should not block acceptance: " + result.ErrorMessage);
        }

        [TestMethod]
        // REQ-GR-08 is per sitter: another sitter's accepted booking is irrelevant.
        public void AcceptRequest_OverlapWithAnotherSittersBooking_IsAccepted()
        {
            int otherSitterId = Services.Auth.Register("sue@test.com", "secret1", UserRole.Sitter,
                "Sue", "023", "Wellington").User.Id;
            Booking theirs = Request(_ownerId, startDay: 10, endDay: 13, sitterId: otherSitterId);
            Services.BookingActions.AcceptRequest(theirs.Id, otherSitterId);
            Booking mine = Request(_otherOwnerId, startDay: 10, endDay: 13);

            Assert.IsTrue(Services.BookingActions.AcceptRequest(mine.Id, _sitterId).Success);
        }

        [TestMethod]
        // REQ-PS-03: "The sitter can also only address bookings that are assigned to them".
        public void AcceptRequest_ForAnotherSittersBooking_IsRejected()
        {
            int otherSitterId = Services.Auth.Register("sue@test.com", "secret1", UserRole.Sitter,
                "Sue", "023", "Wellington").User.Id;
            Booking booking = Request(_ownerId, startDay: 10, endDay: 13);

            BookingResult result = Services.BookingActions.AcceptRequest(booking.Id, otherSitterId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        public void AcceptRequest_ForANonPendingBooking_IsRejected()
        {
            Booking booking = Request(_ownerId, startDay: 10, endDay: 13);
            Services.Bookings.UpdateStatus(booking.Id, BookingStatus.Cancelled);

            BookingResult result = Services.BookingActions.AcceptRequest(booking.Id, _sitterId);

            Assert.IsFalse(result.Success, "A cancelled booking must not be revived by accepting it.");
            Assert.AreEqual(BookingStatus.Cancelled, Services.Bookings.GetById(booking.Id).Status);
        }

        // ---- helpers ----

        /// <summary>A pending request for days of a fixed future month, so "today" never matters.</summary>
        private Booking Request(int ownerId, int startDay, int endDay, int? sitterId = null)
        {
            return Services.Bookings.Insert(new Booking
            {
                OwnerUserId = ownerId,
                SitterUserId = sitterId ?? _sitterId,
                StartDate = new DateTime(2030, 3, startDay),
                EndDate = new DateTime(2030, 3, endDay),
                Status = BookingStatus.Pending,
                DailyRateAtBooking = 40m,
                CreatedUtc = DateTime.UtcNow
            });
        }
    }
}
