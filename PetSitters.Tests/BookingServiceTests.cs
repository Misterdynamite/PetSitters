using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Integration tests for <see cref="BookingService"/> against a real isolated
    /// SQLite database. Covers REQ-GR-08 (a sitter cannot accept a booking
    /// overlapping one they have already accepted; the rejected request stays
    /// pending), the REQ-PS-03 guards on who may accept what, REQ-PO-08 (an
    /// owner cannot book the same pet twice over overlapping dates) and
    /// REQ-PO-07 / DEF-003 (the owner cancels from pending or accepted).
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

        // ---- REQ-PO-08: owner cannot double-book the same pet ----

        [TestMethod]
        // REQ-PO-04
        public void RequestBooking_WithNoClash_IsStoredAsPending()
        {
            int rex = AddPet(_ownerId, "Rex");

            BookingResult result = Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13));

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(result.Booking.Id).Status);
        }

        /// <summary>
        /// The requirement's scenario: the same animal with a DIFFERENT sitter
        /// over overlapping dates. Both live states block.
        /// </summary>
        [DataTestMethod]
        [DataRow(BookingStatus.Pending)]
        [DataRow(BookingStatus.Accepted)]
        public void RequestBooking_SamePetOverlappingLiveBooking_IsRejectedAndNotStored(BookingStatus existingStatus)
        {
            int rex = AddPet(_ownerId, "Rex");
            int otherSitterId = RegisterOtherSitter();
            Booking existing = Request(_ownerId, 10, 13, sitterId: otherSitterId, petId: rex);
            Services.Bookings.UpdateStatus(existing.Id, existingStatus);

            BookingResult result = Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 12, 15));

            Assert.IsFalse(result.Success, "A pet cannot be booked twice over overlapping dates.");
            StringAssert.Contains(result.ErrorMessage, "This pet already has");
            Assert.AreEqual(1, Services.Bookings.GetForOwner(_ownerId).Count, "A refused request must not be saved.");
        }

        [DataTestMethod]
        [DataRow(BookingStatus.Declined)]
        [DataRow(BookingStatus.Cancelled)]
        public void RequestBooking_SamePetOverlappingInactiveBooking_IsAllowed(BookingStatus existingStatus)
        {
            int rex = AddPet(_ownerId, "Rex");
            Booking existing = Request(_ownerId, 10, 13, petId: rex);
            Services.Bookings.UpdateStatus(existing.Id, existingStatus);

            BookingResult result = Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13));

            Assert.IsTrue(result.Success, "A " + existingStatus + " booking should free the pet: " + result.ErrorMessage);
        }

        [TestMethod]
        public void RequestBooking_DifferentPetSameDates_IsAllowed()
        {
            int rex = AddPet(_ownerId, "Rex");
            int milo = AddPet(_ownerId, "Milo");
            Request(_ownerId, 10, 13, petId: rex);

            Assert.IsTrue(Services.BookingActions.RequestBooking(NewRequest(_ownerId, milo, 10, 13)).Success);
        }

        [TestMethod]
        public void RequestBooking_SamePetBackToBack_IsAllowed()
        {
            int rex = AddPet(_ownerId, "Rex");
            Request(_ownerId, 10, 13, petId: rex);

            Assert.IsTrue(Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 13, 15)).Success);
        }

        /// <summary>"All my pets" (null PetId) clashes with any pet, in both directions.</summary>
        [DataTestMethod]
        [DataRow(true, false, DisplayName = "Existing all-pets booking blocks a specific pet")]
        [DataRow(false, true, DisplayName = "Existing specific-pet booking blocks an all-pets request")]
        public void RequestBooking_AllMyPets_ClashesWithAnyPet(bool existingIsAllPets, bool newIsAllPets)
        {
            int rex = AddPet(_ownerId, "Rex");
            Request(_ownerId, 10, 13, petId: existingIsAllPets ? (int?)null : rex);

            BookingResult result = Services.BookingActions.RequestBooking(
                NewRequest(_ownerId, newIsAllPets ? (int?)null : rex, 11, 12));

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.ErrorMessage, "One of these pets already has");
        }

        [TestMethod]
        // The rule is per owner: another owner's all-pets booking is irrelevant.
        public void RequestBooking_AnotherOwnersBooking_DoesNotBlock()
        {
            int rex = AddPet(_ownerId, "Rex");
            Request(_otherOwnerId, 10, 13, petId: null);

            Assert.IsTrue(Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13)).Success);
        }

        // ---- REQ-PO-07 / DEF-003: owner cancels a booking ----

        [DataTestMethod]
        [DataRow(BookingStatus.Pending)]
        [DataRow(BookingStatus.Accepted)]
        public void CancelBooking_FromPendingOrAccepted_IsCancelled(BookingStatus stage)
        {
            Booking booking = Request(_ownerId, 10, 13);
            Services.Bookings.UpdateStatus(booking.Id, stage);

            BookingResult result = Services.BookingActions.CancelBooking(booking.Id, _ownerId);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(BookingStatus.Cancelled, Services.Bookings.GetById(booking.Id).Status);
        }

        [DataTestMethod]
        [DataRow(BookingStatus.Declined)]
        [DataRow(BookingStatus.Cancelled)]
        public void CancelBooking_FromDeclinedOrCancelled_IsRejected(BookingStatus stage)
        {
            Booking booking = Request(_ownerId, 10, 13);
            Services.Bookings.UpdateStatus(booking.Id, stage);

            BookingResult result = Services.BookingActions.CancelBooking(booking.Id, _ownerId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(stage, Services.Bookings.GetById(booking.Id).Status, "The status must be left unchanged.");
        }

        [TestMethod]
        // Authorisation: an owner cannot cancel someone else's booking, nor can the sitter.
        public void CancelBooking_ByAnyoneButTheOwner_IsRejected()
        {
            Booking booking = Request(_ownerId, 10, 13);

            Assert.IsFalse(Services.BookingActions.CancelBooking(booking.Id, _otherOwnerId).Success);
            Assert.IsFalse(Services.BookingActions.CancelBooking(booking.Id, _sitterId).Success);
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        // REQ-PO-07 + REQ-PO-08 together: cancelling frees the pet to be rebooked.
        public void CancelBooking_ThenRebookSamePetAndDates_IsAllowed()
        {
            int rex = AddPet(_ownerId, "Rex");
            Booking first = Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13)).Booking;
            Assert.IsFalse(Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13)).Success);

            Services.BookingActions.CancelBooking(first.Id, _ownerId);

            Assert.IsTrue(Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13)).Success);
        }

        // ---- helpers ----

        /// <summary>A pending request stored directly (bypassing the service's checks) for arranging state.</summary>
        private Booking Request(int ownerId, int startDay, int endDay, int? sitterId = null, int? petId = null)
        {
            return Services.Bookings.Insert(NewRequest(ownerId, petId, startDay, endDay, sitterId));
        }

        /// <summary>An unsaved request for days of a fixed future month, so "today" never matters.</summary>
        private Booking NewRequest(int ownerId, int? petId, int startDay, int endDay, int? sitterId = null)
        {
            return new Booking
            {
                OwnerUserId = ownerId,
                SitterUserId = sitterId ?? _sitterId,
                PetId = petId,
                StartDate = new DateTime(2030, 3, startDay),
                EndDate = new DateTime(2030, 3, endDay),
                Status = BookingStatus.Pending,
                DailyRateAtBooking = 40m,
                CreatedUtc = DateTime.UtcNow
            };
        }

        private int AddPet(int ownerId, string name)
        {
            return Services.Pets.Insert(new Pet { OwnerUserId = ownerId, Name = name, Species = "Dog", Age = 3 }).Id;
        }

        private int RegisterOtherSitter()
        {
            return Services.Auth.Register("sue@test.com", "secret1", UserRole.Sitter,
                "Sue", "023", "Wellington").User.Id;
        }
    }
}
