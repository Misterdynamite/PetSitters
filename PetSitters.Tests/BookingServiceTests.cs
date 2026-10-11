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
    /// owner cannot book the same pet twice over overlapping dates),
    /// REQ-PO-07 / DEF-003 (the owner cancels from pending or accepted) and
    /// DEF-008 (deleting a pet cancels its live bookings).
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
        [TestCategory("Smoke")]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        // REQ-PS-03
        public void AcceptRequest_WithNoClash_AcceptsAndPersists()
        {
            Booking booking = Request(_ownerId, startDay: 10, endDay: 13);

            BookingResult result = Services.BookingActions.AcceptRequest(booking.Id, _sitterId);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(BookingStatus.Accepted, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
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
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Negative")]
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
        [TestCategory("Integration")]
        [TestCategory("Negative")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
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
        [TestCategory("Integration")]
        [TestCategory("Negative")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        public void RequestBooking_DifferentPetSameDates_IsAllowed()
        {
            int rex = AddPet(_ownerId, "Rex");
            int milo = AddPet(_ownerId, "Milo");
            Request(_ownerId, 10, 13, petId: rex);

            Assert.IsTrue(Services.BookingActions.RequestBooking(NewRequest(_ownerId, milo, 10, 13)).Success);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        public void RequestBooking_SamePetBackToBack_IsAllowed()
        {
            int rex = AddPet(_ownerId, "Rex");
            Request(_ownerId, 10, 13, petId: rex);

            Assert.IsTrue(Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 13, 15)).Success);
        }

        /// <summary>"All my pets" (null PetId) clashes with any pet, in both directions.</summary>
        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        // The rule is per owner: another owner's all-pets booking is irrelevant.
        public void RequestBooking_AnotherOwnersBooking_DoesNotBlock()
        {
            int rex = AddPet(_ownerId, "Rex");
            Request(_otherOwnerId, 10, 13, petId: null);

            Assert.IsTrue(Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13)).Success);
        }

        // ---- REQ-PO-07 / DEF-003: owner cancels a booking ----

        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
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
        [TestCategory("Integration")]
        [TestCategory("Negative")]
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
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Negative")]
        // Authorisation: an owner cannot cancel someone else's booking, nor can the sitter.
        public void CancelBooking_ByAnyoneButTheOwner_IsRejected()
        {
            Booking booking = Request(_ownerId, 10, 13);

            Assert.IsFalse(Services.BookingActions.CancelBooking(booking.Id, _otherOwnerId).Success);
            Assert.IsFalse(Services.BookingActions.CancelBooking(booking.Id, _sitterId).Success);
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        // REQ-PO-07 + REQ-PO-08 together: cancelling frees the pet to be rebooked.
        public void CancelBooking_ThenRebookSamePetAndDates_IsAllowed()
        {
            int rex = AddPet(_ownerId, "Rex");
            Booking first = Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13)).Booking;
            Assert.IsFalse(Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13)).Success);

            Services.BookingActions.CancelBooking(first.Id, _ownerId);

            Assert.IsTrue(Services.BookingActions.RequestBooking(NewRequest(_ownerId, rex, 10, 13)).Success);
        }

        // ---- Decline and sitter cancel go through the service (shared-database safety) ----

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        // REQ-PS-03
        public void DeclineRequest_Pending_IsDeclined()
        {
            Booking booking = Request(_ownerId, 10, 13);

            BookingResult result = Services.BookingActions.DeclineRequest(booking.Id, _sitterId);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(BookingStatus.Declined, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
        // The owner cancelled first (from another computer): the sitter's decline is refused, nothing changes.
        public void DeclineRequest_AfterTheOwnerCancelled_IsRefused_AndStaysCancelled()
        {
            Booking booking = Request(_ownerId, 10, 13);
            Services.BookingActions.CancelBooking(booking.Id, _ownerId);

            BookingResult result = Services.BookingActions.DeclineRequest(booking.Id, _sitterId);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ChangedElsewhere, "The sitter's list is stale and must be reloaded.");
            StringAssert.Contains(result.ErrorMessage, "already been cancelled");
            Assert.AreEqual(BookingStatus.Cancelled, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
        // Same for accepting: the owner cancelled while the sitter's list still showed it as pending.
        public void AcceptRequest_AfterTheOwnerCancelled_ReportsChangedElsewhere()
        {
            Booking booking = Request(_ownerId, 10, 13);
            Services.BookingActions.CancelBooking(booking.Id, _ownerId);

            BookingResult result = Services.BookingActions.AcceptRequest(booking.Id, _sitterId);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ChangedElsewhere);
            Assert.AreEqual(BookingStatus.Cancelled, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
        // And for the owner: the sitter declined while the owner's list still showed it as pending.
        public void CancelBooking_AfterTheSitterDeclined_ReportsChangedElsewhere()
        {
            Booking booking = Request(_ownerId, 10, 13);
            Services.BookingActions.DeclineRequest(booking.Id, _sitterId);

            BookingResult result = Services.BookingActions.CancelBooking(booking.Id, _ownerId);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ChangedElsewhere);
            StringAssert.Contains(result.ErrorMessage, "already been declined");
            Assert.AreEqual(BookingStatus.Declined, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
        [TestCategory("Security")]
        // Not-yours is NOT "changed elsewhere" (no reload hint for someone else's booking).
        public void DeclineRequest_NotYours_IsAPlainRefusal()
        {
            Booking booking = Request(_ownerId, 10, 13);

            BookingResult result = Services.BookingActions.DeclineRequest(booking.Id, RegisterOtherSitter());

            Assert.IsFalse(result.Success);
            Assert.IsFalse(result.ChangedElsewhere);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Negative")]
        public void DeclineRequest_ForAnotherSittersBooking_IsRefused()
        {
            Booking booking = Request(_ownerId, 10, 13);

            Assert.IsFalse(Services.BookingActions.DeclineRequest(booking.Id, RegisterOtherSitter()).Success);
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(booking.Id).Status);
        }

        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [TestCategory("Negative")]
        [DataRow(BookingStatus.Accepted, true, DisplayName = "Accepted: the sitter may cancel")]
        [DataRow(BookingStatus.Pending, false, DisplayName = "Pending: decline it instead")]
        [DataRow(BookingStatus.Cancelled, false, DisplayName = "Already cancelled")]
        public void CancelAsSitter_OnlyFromAccepted(BookingStatus stage, bool allowed)
        {
            Booking booking = Request(_ownerId, 10, 13);
            Services.Bookings.UpdateStatus(booking.Id, stage);

            BookingResult result = Services.BookingActions.CancelAsSitter(booking.Id, _sitterId);

            Assert.AreEqual(allowed, result.Success, result.ErrorMessage);
            Assert.AreEqual(allowed ? BookingStatus.Cancelled : stage, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Negative")]
        public void CancelAsSitter_ByAnotherSitter_IsRefused()
        {
            Booking booking = Request(_ownerId, 10, 13);
            Services.Bookings.UpdateStatus(booking.Id, BookingStatus.Accepted);

            Assert.IsFalse(Services.BookingActions.CancelAsSitter(booking.Id, RegisterOtherSitter()).Success);
            Assert.AreEqual(BookingStatus.Accepted, Services.Bookings.GetById(booking.Id).Status);
        }

        // ---- DEF-008: deleting a pet cancels its live bookings ----

        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [DataRow(BookingStatus.Pending)]
        [DataRow(BookingStatus.Accepted)]
        // DEF-008 regression (REQ-PO-08): a deleted pet's live booking used to stay
        // live with no pet, which the same-pet rule reads as "All my pets", so it
        // blocked every other pet of the owner over those dates.
        public void DeletePet_WithALiveBooking_CancelsIt_AndFreesTheDates(BookingStatus stage)
        {
            int rex = AddPet(_ownerId, "Rex");
            int buddy = AddPet(_ownerId, "Buddy");
            Booking rexBooking = Request(_ownerId, 10, 13, petId: rex);
            Services.Bookings.UpdateStatus(rexBooking.Id, stage);

            PetDeletionResult result = Services.BookingActions.DeletePet(rex, _ownerId);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(1, result.CancelledBookings);
            Assert.AreEqual(BookingStatus.Cancelled, Services.Bookings.GetById(rexBooking.Id).Status);
            Assert.IsFalse(Services.Pets.GetByOwner(_ownerId).Exists(p => p.Id == rex), "The pet itself must be deleted.");
            BookingResult buddyRequest = Services.BookingActions.RequestBooking(NewRequest(_ownerId, buddy, 11, 12));
            Assert.IsTrue(buddyRequest.Success, "Another pet must be bookable over those dates: " + buddyRequest.ErrorMessage);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        // Only the deleted pet's LIVE bookings are cancelled: its finished ones keep
        // their status, and other pets' and "All my pets" bookings are untouched.
        public void DeletePet_LeavesFinishedAndOtherBookingsAlone()
        {
            int rex = AddPet(_ownerId, "Rex");
            int buddy = AddPet(_ownerId, "Buddy");
            Booking rexDeclined = Request(_ownerId, 1, 3, petId: rex);
            Services.Bookings.UpdateStatus(rexDeclined.Id, BookingStatus.Declined);
            Booking buddyPending = Request(_ownerId, 10, 13, petId: buddy);
            Booking allPetsPending = Request(_ownerId, 20, 22, petId: null);

            PetDeletionResult result = Services.BookingActions.DeletePet(rex, _ownerId);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(0, result.CancelledBookings);
            Assert.AreEqual(BookingStatus.Declined, Services.Bookings.GetById(rexDeclined.Id).Status);
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(buddyPending.Id).Status);
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(allPetsPending.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Negative")]
        // Authorisation: another owner can't delete the pet, or cancel its bookings that way.
        public void DeletePet_NotYourPet_IsRefused_AndNothingChanges()
        {
            int rex = AddPet(_ownerId, "Rex");
            Booking booking = Request(_ownerId, 10, 13, petId: rex);

            PetDeletionResult result = Services.BookingActions.DeletePet(rex, _otherOwnerId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(BookingStatus.Pending, Services.Bookings.GetById(booking.Id).Status);
            Assert.IsTrue(Services.Pets.GetByOwner(_ownerId).Exists(p => p.Id == rex), "The pet must not be deleted.");
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
