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
        [TestCategory("Smoke")]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        // FR-04
        public void UpdateStatus_Accept_IsPersisted()
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(BookingStatus.Pending);

            Services.Bookings.UpdateStatus(booking.Id, BookingStatus.Accepted);

            Assert.AreEqual(BookingStatus.Accepted, Services.Bookings.GetById(booking.Id).Status);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
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
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Negative")]
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
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        public void Insert_PreservesDailyRateSnapshot()
        {
            GivenOwnerSitterAndPet();

            Booking booking = InsertBooking(BookingStatus.Pending, dailyRate: 55m);

            Assert.AreEqual(55m, Services.Bookings.GetById(booking.Id).DailyRateAtBooking);
        }

        // ---- One-query loaders for the dashboards (cloud latency) ----

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        // FR-O4 / FR-S4: the owner's list shows sitter and pet without extra lookups.
        public void GetDetailsForOwner_IncludesSitterAndPet()
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(BookingStatus.Pending);

            BookingDetails details = Services.Bookings.GetDetailsForOwner(_ownerId).Single();

            Assert.AreEqual(booking.Id, details.Booking.Id);
            Assert.AreEqual("Sam", details.Sitter.FullName);
            Assert.AreEqual("Olivia", details.Owner.FullName);
            Assert.AreEqual("Rex", details.Pet.Name);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        // "All my pets" bookings have no PetId: the LEFT JOIN must still return the booking.
        public void GetDetailsForSitter_AllMyPetsBooking_HasNoPet_ButIsStillListed()
        {
            GivenOwnerSitterAndPet();
            Services.Bookings.Insert(new Booking
            {
                OwnerUserId = _ownerId, SitterUserId = _sitterId, PetId = null,
                StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1),
                Status = BookingStatus.Pending, DailyRateAtBooking = 45m, CreatedUtc = DateTime.UtcNow
            });

            BookingDetails details = Services.Bookings.GetDetailsForSitter(_sitterId).Single();

            Assert.IsNull(details.Pet);
            Assert.AreEqual("Olivia", details.Owner.FullName, "The sitter's view needs the owner's details.");
            Assert.AreEqual("Wellington", details.Owner.Location);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Negative")]
        // Joined users carry display details only: no other person's password hash leaves the database.
        public void GetDetails_JoinedUsers_DoNotCarryPasswordHashes()
        {
            GivenOwnerSitterAndPet();
            InsertBooking(BookingStatus.Pending);

            BookingDetails details = Services.Bookings.GetDetailsForSitter(_sitterId).Single();

            Assert.IsNull(details.Owner.PasswordHash);
            Assert.IsNull(details.Owner.PasswordSalt);
            Assert.IsNull(details.Sitter.PasswordHash);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Negative")]
        public void GetDetailsForSitter_ExcludesOtherSittersBookings()
        {
            GivenOwnerSitterAndPet();
            InsertBooking(BookingStatus.Pending);
            int otherSitter = Services.Auth.Register("sitter2@test.com", "secret1", UserRole.Sitter,
                "Second Sitter", "021", "Auckland").User.Id;

            Assert.AreEqual(0, Services.Bookings.GetDetailsForSitter(otherSitter).Count);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Performance")]
        [TestCategory("Positive")]
        // The cloud-latency fix: loading a dashboard's bookings is ONE round trip
        // however many bookings there are (the old screens made 1 + 2 per booking).
        public void GetDetails_IsOneRoundTrip_RegardlessOfRowCount()
        {
            GivenOwnerSitterAndPet();
            for (int i = 0; i < 6; i++)
                InsertBooking(BookingStatus.Pending);

            int before = Db.ConnectionsOpened;
            List<BookingDetails> owners = Services.Bookings.GetDetailsForOwner(_ownerId);
            List<BookingDetails> sitters = Services.Bookings.GetDetailsForSitter(_sitterId);

            Assert.AreEqual(6, owners.Count);
            Assert.AreEqual(6, sitters.Count);
            Assert.AreEqual(2, Db.ConnectionsOpened - before, "Each loader must be a single query.");
        }

        // ---- Shared-database safety ----

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        public void TryUpdateStatus_FromAnExpectedStatus_Changes_AndRaisesTheEvent()
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(BookingStatus.Pending);
            int raised = 0;
            Services.Bookings.BookingStatusChanged += (id, status) => raised++;

            bool changed = Services.Bookings.TryUpdateStatus(booking.Id, BookingStatus.Accepted, BookingStatus.Pending);

            Assert.IsTrue(changed);
            Assert.AreEqual(BookingStatus.Accepted, Services.Bookings.GetById(booking.Id).Status);
            Assert.AreEqual(1, raised, "Listeners must hear about the change.");
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
        [TestCategory("ErrorHandling")]
        // The race guard, at the SQL level: the other person already changed the
        // booking (here: cancelled it), so a stale "Pending -> Declined" from a
        // second computer must change NOTHING. One conditional UPDATE, no gap.
        public void TryUpdateStatus_WhenAlreadyChangedByTheOtherParty_ChangesNothing()
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(BookingStatus.Cancelled);
            int raised = 0;
            Services.Bookings.BookingStatusChanged += (id, status) => raised++;

            bool changed = Services.Bookings.TryUpdateStatus(booking.Id, BookingStatus.Declined, BookingStatus.Pending);

            Assert.IsFalse(changed);
            Assert.AreEqual(BookingStatus.Cancelled, Services.Bookings.GetById(booking.Id).Status, "The other party's change must stand.");
            Assert.AreEqual(0, raised, "Nothing changed, so no event.");
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        // Booking dates are calendar values: stored with NO time-zone offset, so a
        // PC in another time zone reading the shared database sees the same day.
        public void Insert_StoresBookingDatesWithoutATimeZoneOffset()
        {
            GivenOwnerSitterAndPet();
            DateTime localToday = DateTime.Today;   // Kind = Local, as the booking form produces
            Booking booking = InsertBooking(BookingStatus.Pending);

            string raw;
            using (var connection = Db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT StartDate FROM Bookings WHERE Id = " + booking.Id + ";";
                raw = (string)command.ExecuteScalar();
            }
            Booking readBack = Services.Bookings.GetById(booking.Id);

            Assert.IsFalse(raw.Contains("+") || raw.EndsWith("Z"), "Stored with an offset: " + raw);
            Assert.AreEqual(DateTimeKind.Unspecified, readBack.StartDate.Kind);
            Assert.AreEqual(localToday, readBack.StartDate, "The same calendar date and time must come back.");
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Boundary")]
        [TestCategory("Positive")]
        // Rows written before offset-free storage carry an offset. Read on a PC in
        // another time zone they used to move to a different day; now the clock
        // value is taken as written, wherever the app runs.
        public void GetById_LegacyRowWithAnOffset_ReadsTheDateAsWritten()
        {
            GivenOwnerSitterAndPet();
            Booking booking = InsertBooking(BookingStatus.Pending);
            using (var connection = Db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                // +14:00 is ahead of every real time zone, so converting it to local
                // time would land on the previous day anywhere this test runs.
                command.CommandText = "UPDATE Bookings SET StartDate = '2030-03-10T00:00:00.0000000+14:00' WHERE Id = " + booking.Id + ";";
                command.ExecuteNonQuery();
            }

            Booking readBack = Services.Bookings.GetById(booking.Id);

            Assert.AreEqual(new DateTime(2030, 3, 10, 0, 0, 0), readBack.StartDate);
            Assert.AreEqual(DateTimeKind.Unspecified, readBack.StartDate.Kind);
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
