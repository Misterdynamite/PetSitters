using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Data;
using PetSitters.Models;

namespace PetSitters.Tests
{
    /// <summary>
    /// Integration tests for chat persistence (FR-O5 / FR-S5: chat once a booking
    /// is accepted). These exercise a REAL file-backed database, following Lab 5:
    ///   * a message survives being read back by a fresh repository instance
    ///     (proves it is on disk, not just in memory);
    ///   * messages are scoped to their booking - a message for one booking must
    ///     not appear for another (the "messages not visible to unrelated users"
    ///     quality/security concern in the proposal);
    ///   * messages come back in chronological order.
    /// </summary>
    [TestClass]
    public class ChatPersistenceTests : DatabaseTestBase
    {
        private int _ownerId;
        private int _sitterId;

        [TestInitialize]
        public void SeedParticipants()
        {
            _ownerId = Services.Auth.Register("owner@test.com", "secret1", UserRole.Owner,
                "Olivia", "021", "Wellington").User.Id;
            _sitterId = Services.Auth.Register("sitter@test.com", "secret1", UserRole.Sitter,
                "Sam", "022", "Wellington").User.Id;
        }

        private int NewAcceptedBooking()
        {
            return Services.Bookings.Insert(new Booking
            {
                OwnerUserId = _ownerId,
                SitterUserId = _sitterId,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2),
                Status = BookingStatus.Accepted,
                DailyRateAtBooking = 45m,
                CreatedUtc = DateTime.UtcNow
            }).Id;
        }

        [TestMethod]
        public void Message_IsPersisted_AndReadBackByAFreshRepository()
        {
            int bookingId = NewAcceptedBooking();
            Services.Chats.Insert(new ChatMessage
            {
                BookingId = bookingId,
                SenderUserId = _ownerId,
                MessageText = "Hi, Rex needs medication at 6pm.",
                CreatedUtc = DateTime.UtcNow
            });

            // A brand-new repository over the same database file must see the row.
            var freshRepo = new ChatRepository(Db);
            List<ChatMessage> messages = freshRepo.GetForBooking(bookingId);

            Assert.AreEqual(1, messages.Count);
            Assert.AreEqual("Hi, Rex needs medication at 6pm.", messages[0].MessageText);
            Assert.AreEqual(_ownerId, messages[0].SenderUserId);
        }

        [TestMethod]
        public void GetForBooking_ReturnsOnlyThatBookingsMessages()
        {
            int bookingA = NewAcceptedBooking();
            int bookingB = NewAcceptedBooking();

            Services.Chats.Insert(new ChatMessage { BookingId = bookingA, SenderUserId = _ownerId, MessageText = "For A", CreatedUtc = DateTime.UtcNow });
            Services.Chats.Insert(new ChatMessage { BookingId = bookingB, SenderUserId = _sitterId, MessageText = "For B", CreatedUtc = DateTime.UtcNow });

            List<ChatMessage> forA = Services.Chats.GetForBooking(bookingA);

            Assert.AreEqual(1, forA.Count);
            Assert.AreEqual("For A", forA[0].MessageText, "Booking A must not see booking B's messages.");
        }

        [TestMethod]
        public void GetForBooking_ReturnsMessagesInChronologicalOrder()
        {
            int bookingId = NewAcceptedBooking();
            DateTime baseTime = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc);

            Services.Chats.Insert(new ChatMessage { BookingId = bookingId, SenderUserId = _ownerId, MessageText = "first", CreatedUtc = baseTime });
            Services.Chats.Insert(new ChatMessage { BookingId = bookingId, SenderUserId = _sitterId, MessageText = "second", CreatedUtc = baseTime.AddMinutes(5) });
            Services.Chats.Insert(new ChatMessage { BookingId = bookingId, SenderUserId = _ownerId, MessageText = "third", CreatedUtc = baseTime.AddMinutes(10) });

            List<ChatMessage> messages = Services.Chats.GetForBooking(bookingId);

            Assert.AreEqual(3, messages.Count);
            Assert.AreEqual("first", messages[0].MessageText);
            Assert.AreEqual("second", messages[1].MessageText);
            Assert.AreEqual("third", messages[2].MessageText);
        }
    }
}
