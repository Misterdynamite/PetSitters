using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using PetSitters.Models;

namespace PetSitters.Data
{
    /// <summary>
    /// Stores and retrieves chat messages which are scoped to a single booking
    /// between the owner and sitter who participated in that booking.
    /// </summary>
    public class ChatRepository
    {
        private readonly Database _db;

        public ChatRepository(Database db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public ChatMessage Insert(ChatMessage message)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO ChatMessages (BookingId, SenderUserId, MessageText, CreatedUtc)
VALUES (@booking, @sender, @text, @created);
" + _db.Dialect.SelectLastInsertId;
                command.AddParameter("@booking", message.BookingId);
                command.AddParameter("@sender", message.SenderUserId);
                command.AddParameter("@text", message.MessageText ?? string.Empty);
                command.AddParameter("@created", message.CreatedUtc.ToString("o", CultureInfo.InvariantCulture));
                message.Id = Convert.ToInt32(command.ExecuteScalar());
                return message;
            }
        }

        /// <summary>
        /// Returns messages for a booking sorted by ascending CreatedUtc.
        /// </summary>
        public List<ChatMessage> GetForBooking(int bookingId)
        {
            var list = new List<ChatMessage>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM ChatMessages WHERE BookingId = @booking ORDER BY CreatedUtc ASC, Id ASC;";   // Id breaks timestamp ties the same way on both engines
                command.AddParameter("@booking", bookingId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        list.Add(Map(reader));
                }
            }
            return list;
        }

        /// <summary>
        /// Like <see cref="GetForBooking"/>, but each message also carries its
        /// sender's display name (<see cref="ChatMessage.SenderName"/>), in ONE
        /// query. The chat panel used to look up the sender once per message,
        /// which on the cloud database made a 20-message chat take seconds.
        /// Ordered by time, then id, so equal timestamps keep insertion order.
        /// </summary>
        public List<ChatMessage> GetForBookingWithSenderNames(int bookingId)
        {
            var list = new List<ChatMessage>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT m.*, u.FullName AS SenderName FROM ChatMessages m" +
                    " LEFT JOIN Users u ON u.Id = m.SenderUserId" +
                    " WHERE m.BookingId = @booking ORDER BY m.CreatedUtc ASC, m.Id ASC;";
                command.AddParameter("@booking", bookingId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ChatMessage message = Map(reader);
                        message.SenderName = reader["SenderName"] as string;
                        list.Add(message);
                    }
                }
            }
            return list;
        }

        private static ChatMessage Map(DbDataReader reader)
        {
            return new ChatMessage
            {
                Id = Convert.ToInt32(reader["Id"]),
                BookingId = Convert.ToInt32(reader["BookingId"]),
                SenderUserId = Convert.ToInt32(reader["SenderUserId"]),
                MessageText = reader["MessageText"] as string,
                CreatedUtc = DateTime.Parse((string)reader["CreatedUtc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            };
        }
    }
}
