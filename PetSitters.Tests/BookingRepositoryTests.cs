using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;

namespace PetSitters.Tests
{
    /// <summary>
    /// Component tests for <see cref="PetSitters.Data.BookingRepository"/>.
    /// Supports FR-O4 (owner requests a booking), FR-S4 (sitter accepts/declines)
    /// and REQ-PO-07 (owner cancels a booking from pending or accepted).
    /// </summary>
    [TestClass]
    public class BookingRepositoryTests : DatabaseTestBase
    {
        private int _ownerId;
        private int _sitterId;
        private int _petId;

        [TestMethod]
        // FR-05
        public void Insert_BookingIsVisibleToBothOwnerAndSitter()
        {
            GivenOwnerSitterAndPet();

            Booking booking = InsertBooking(BookingStatus.Pending);

            List<Booking> ownerView = Services.Bookings.GetForOwner(_ownerId);
            List<Booking> sitterView = Services.Bookings.GetForSitter(_sitterId);

            Assert.AreEqual(1, ownerView.Count);
            Assert.AreEqual(1, sitterView.Count);
            Assert.AreEqual(booking.Id, sitterView[0].Id);
            Assert.AreEqual(BookingStatus.Pending, sitterView[0].Status);
        }

        // FR-04 / REQ-PS-03: the sitter's two possible responses are tested
        // separately so a failure names which branch broke, and so one failing
        // branch cannot hide the other behind it.

        [TestMethod]
        // FR-04
        public void UpdateStatus_Accept_IsPersisted()
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(BookingStatus.Pending);

            Services.Bookings.UpdateStatus(booking.Id, BookingStatus.Accepted);

            Assert.AreEqual(BookingStatus.Accepted, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        // FR-04
        public void UpdateStatus_Decline_IsPersisted()
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(BookingStatus.Pending);

            Services.Bookings.UpdateStatus(booking.Id, BookingStatus.Declined);

            Assert.AreEqual(BookingStatus.Declined, Services.Bookings.GetById(booking.Id).Status);
        }

        // ---- REQ-PO-07: owner cancels a booking ----

        /// <summary>
        /// REQ-PO-07 states the owner may cancel "a booking status with either
        /// pending or accepted", so both stages are covered as an equivalence
        /// partition over the states a cancel is allowed from.
        /// </summary>
        [DataTestMethod]
        [DataRow(BookingStatus.Pending)]
        [DataRow(BookingStatus.Accepted)]
        public void UpdateStatus_Cancel_IsPersistedFromEitherStage(BookingStatus stageBeforeCancelling)
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(stageBeforeCancelling);

            Services.Bookings.UpdateStatus(booking.Id, BookingStatus.Cancelled);

            Booking cancelled = Services.Bookings.GetById(booking.Id);
            Assert.IsNotNull(cancelled, "Cancelling should change the booking's status, not delete the record.");
            Assert.AreEqual(BookingStatus.Cancelled, cancelled.Status,
                "A booking cancelled from " + stageBeforeCancelling + " should persist as Cancelled.");
        }

        /// <summary>
        /// The second half of REQ-PO-07: a cancelled booking "will no longer
        /// appear in the sitter's list of booking requests". That list is the
        /// sitter's pending queue, while the booking itself is kept so it stays on
        /// the owner's record rather than vanishing.
        /// </summary>
        [TestMethod]
        public void UpdateStatus_Cancel_RemovesBookingFromSittersPendingQueue()
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(BookingStatus.Pending);

            Services.Bookings.UpdateStatus(booking.Id, BookingStatus.Cancelled);

            List<Booking> sittersPendingQueue = Services.Bookings.GetForSitter(_sitterId)
                .Where(b => b.Status == BookingStatus.Pending)
                .ToList();

            Assert.AreEqual(0, sittersPendingQueue.Count,
                "A cancelled booking should drop out of the sitter's pending requests.");
            Assert.AreEqual(1, Services.Bookings.GetForOwner(_ownerId).Count,
                "The cancelled booking should still be on the owner's record.");
        }

        [TestMethod]
        public void GetForSitter_DoesNotReturnAnotherSittersBookings()
        {
            GivenOwnerSitterAndPet();
            InsertBooking(BookingStatus.Pending);

            // A second, unrelated sitter should see no booking requests.
            var otherSitter = Services.Auth.Register("sitter2@test.com", "secret1", UserRole.Sitter,
                "Second Sitter", "021", "Auckland");

            Assert.AreEqual(0, Services.Bookings.GetForSitter(otherSitter.User.Id).Count);
        }

        [TestMethod]
        public void Insert_PreservesDailyRateSnapshot()
        {
            GivenOwnerSitterAndPet();

            Booking booking = InsertBooking(BookingStatus.Pending, dailyRate: 55m);

            Assert.AreEqual(55m, Services.Bookings.GetById(booking.Id).DailyRateAtBooking);
        }

        // ---- helpers ----
        private void GivenOwnerSitterAndPet()
        {
            _ownerId = Services.Auth.Register("owner@test.com", "secret1", UserRole.Owner,
                "Olivia", "021", "Wellington").User.Id;
            _sitterId = Services.Auth.Register("sitter@test.com", "secret1", UserRole.Sitter,
                "Sam", "022", "Wellington").User.Id;
            _petId = Services.Pets.Insert(new Pet { OwnerUserId = _ownerId, Name = "Rex", Species = "Dog", Age = 4 }).Id;
        }

        private Booking InsertBooking(BookingStatus status, decimal dailyRate = 45m)
        {
            return Services.Bookings.Insert(new Booking
            {
                OwnerUserId = _ownerId,
                SitterUserId = _sitterId,
                PetId = _petId,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(3),
                Message = "Please look after Rex",
                Status = status,
                DailyRateAtBooking = dailyRate,
                CreatedUtc = DateTime.UtcNow
            });
        }
    }
}
