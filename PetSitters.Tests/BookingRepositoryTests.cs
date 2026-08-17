using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;

namespace PetSitters.Tests
{
    /// <summary>
    /// Component tests for <see cref="PetSitters.Data.BookingRepository"/>.
    /// Supports FR-O4 (owner requests a booking) and FR-S4 (sitter accepts/declines).
    /// </summary>
    [TestClass]
    public class BookingRepositoryTests : DatabaseTestBase
    {
        private int _ownerId;
        private int _sitterId;
        private int _petId;

        [TestMethod]
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

        [TestMethod]
        public void UpdateStatus_Accept_IsPersisted()
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(BookingStatus.Pending);

            Services.Bookings.UpdateStatus(booking.Id, BookingStatus.Accepted);

            Assert.AreEqual(BookingStatus.Accepted, Services.Bookings.GetById(booking.Id).Status);
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
