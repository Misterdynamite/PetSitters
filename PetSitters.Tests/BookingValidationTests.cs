using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Tests for REQ-GR-04 booking-form validation
    /// (<see cref="BookingService.ValidateRequest"/> and its use by
    /// <see cref="BookingService.RequestBooking"/>): the start can't be in the
    /// past, the end must be after the start, the duration must be 1 hour to
    /// 14 days, and a pet must be selected. Each rule has its own message.
    ///
    /// Boundary-value analysis on both duration limits and on "today". The
    /// service gets a FIXED clock (noon, 10 Mar 2030) so results never depend
    /// on the day the suite runs.
    /// </summary>
    [TestClass]
    public class BookingValidationTests : DatabaseTestBase
    {
        private static readonly DateTime Now = new DateTime(2030, 3, 10, 12, 0, 0);

        private BookingService _service;
        private int _ownerId;
        private int _sitterId;
        private int _petId;

        [TestInitialize]
        public void GivenOwnerWithAPetAndASitter()
        {
            _service = new BookingService(Services.Bookings, Services.Pets, () => Now);
            _ownerId = Services.Auth.Register("olivia@test.com", "secret1", UserRole.Owner,
                "Olivia", "021", "Wellington").User.Id;
            _sitterId = Services.Auth.Register("sam@test.com", "secret1", UserRole.Sitter,
                "Sam", "022", "Wellington").User.Id;
            _petId = Services.Pets.Insert(new Pet { OwnerUserId = _ownerId, Name = "Rex", Age = 3 }).Id;
        }

        // ---- start date ----

        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        // REQ-GR-04: days relative to "today" (10 Mar 2030).
        [DataRow(-1, false, DisplayName = "Yesterday is rejected")]
        [DataRow(0, true, DisplayName = "Today is allowed (dates only, judged by day)")]
        [DataRow(1, true, DisplayName = "Tomorrow is allowed")]
        public void Validate_StartDate_RelativeToToday(int startOffsetDays, bool valid)
        {
            DateTime start = Now.Date.AddDays(startOffsetDays);

            string error = _service.ValidateRequest(Request(start, start.AddDays(2)));

            AssertValidity(valid, error, "Start date cannot be in the past.");
        }

        // ---- end after start ----

        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow(0, DisplayName = "End equals start")]
        [DataRow(-1, DisplayName = "End before start")]
        public void Validate_EndNotAfterStart_IsRejected(int endOffsetDays)
        {
            DateTime start = Now.Date.AddDays(1);

            string error = _service.ValidateRequest(Request(start, start.AddDays(endOffsetDays)));

            Assert.AreEqual("End date must be after the start date.", error);
        }

        // ---- duration: 1 hour .. 14 days ----

        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow(59, false, DisplayName = "59 minutes is rejected")]
        [DataRow(60, true, DisplayName = "Exactly 1 hour is allowed")]
        [DataRow(61, true, DisplayName = "61 minutes is allowed")]
        public void Validate_MinimumDuration_Boundary(int minutes, bool valid)
        {
            DateTime start = Now.Date.AddDays(1).AddHours(9);

            string error = _service.ValidateRequest(Request(start, start.AddMinutes(minutes)));

            AssertValidity(valid, error, "A booking must be at least 1 hour long.");
        }

        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow(13, true, DisplayName = "13 days is allowed")]
        [DataRow(14, true, DisplayName = "Exactly 14 days is allowed")]
        [DataRow(15, false, DisplayName = "15 days is rejected")]
        public void Validate_MaximumDuration_Boundary(int days, bool valid)
        {
            DateTime start = Now.Date.AddDays(1);

            string error = _service.ValidateRequest(Request(start, start.AddDays(days)));

            AssertValidity(valid, error, "A booking can be at most 14 days long. Choose an earlier end date.");
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        public void Validate_FourteenDaysAndOneMinute_IsRejected()
        {
            DateTime start = Now.Date.AddDays(1);

            string error = _service.ValidateRequest(Request(start, start.AddDays(14).AddMinutes(1)));

            StringAssert.StartsWith(error, "A booking can be at most 14 days long.");
        }

        // ---- pet selected ----

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("InvalidInput")]
        [TestCategory("Negative")]
        // "No pet selected": "All my pets" is meaningless when the owner has none.
        public void Validate_AllMyPets_WhenOwnerHasNoPets_IsRejected()
        {
            Services.Pets.Delete(_petId);

            string error = _service.ValidateRequest(Request(Now.Date.AddDays(1), Now.Date.AddDays(2), petId: null));

            StringAssert.StartsWith(error, "Please select a pet for this booking.");
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        public void Validate_AllMyPets_WhenOwnerHasPets_IsAllowed()
        {
            Assert.IsNull(_service.ValidateRequest(Request(Now.Date.AddDays(1), Now.Date.AddDays(2), petId: null)));
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("InvalidInput")]
        // Authorisation/data integrity: a pet id that isn't the owner's (another
        // owner's, or deleted) is not a valid selection.
        public void Validate_PetBelongingToAnotherOwner_IsRejected()
        {
            int otherOwner = Services.Auth.Register("olive@test.com", "secret1", UserRole.Owner,
                "Olive", "021", "Wellington").User.Id;
            int theirPet = Services.Pets.Insert(new Pet { OwnerUserId = otherOwner, Name = "Milo", Age = 1 }).Id;

            string error = _service.ValidateRequest(Request(Now.Date.AddDays(1), Now.Date.AddDays(2), petId: theirPet));

            Assert.AreEqual("Please select one of your pets for this booking.", error);
        }

        // ---- wired into RequestBooking ----

        [TestMethod]
        [TestCategory("Smoke")]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        // REQ-GR-04 "A valid submission is accepted."
        public void RequestBooking_ValidSubmission_IsStoredAsPending()
        {
            BookingResult result = _service.RequestBooking(Request(Now.Date, Now.Date.AddDays(3)));

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(result.Booking.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("InvalidInput")]
        [TestCategory("ErrorHandling")]
        public void RequestBooking_InvalidSubmission_IsRejectedAndNotStored()
        {
            BookingResult result = _service.RequestBooking(Request(Now.Date.AddDays(-2), Now.Date.AddDays(1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Start date cannot be in the past.", result.ErrorMessage);
            Assert.AreEqual(0, Services.Bookings.GetForOwner(_ownerId).Count, "A rejected request must not be saved.");
        }

        // ---- helpers ----

        private Booking Request(DateTime start, DateTime end, int? petId = -1)
        {
            return new Booking
            {
                OwnerUserId = _ownerId,
                SitterUserId = _sitterId,
                PetId = petId == -1 ? _petId : petId,   // -1 = "the owner's pet" (DataRow-friendly default)
                StartDate = start,
                EndDate = end,
                DailyRateAtBooking = 40m,
                CreatedUtc = DateTime.UtcNow
            };
        }

        private static void AssertValidity(bool expectedValid, string error, string expectedMessage)
        {
            if (expectedValid)
                Assert.IsNull(error, "Expected the request to be valid, but got: " + error);
            else
                Assert.AreEqual(expectedMessage, error);
        }
    }
}
