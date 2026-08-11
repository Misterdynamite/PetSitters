using System;
using System.Collections.Generic;
using System.Data.SQLite;
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
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@booking", message.BookingId);
                command.Parameters.AddWithValue("@sender", message.SenderUserId);
                command.Parameters.AddWithValue("@text", message.MessageText ?? string.Empty);
                command.Parameters.AddWithValue("@created", message.CreatedUtc.ToString("o", CultureInfo.InvariantCulture));
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
                command.CommandText = "SELECT * FROM ChatMessages WHERE BookingId = @booking ORDER BY CreatedUtc ASC;";
                command.Parameters.AddWithValue("@booking", bookingId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        list.Add(Map(reader));
                }
            }
            return list;
        }

        private static ChatMessage Map(SQLiteDataReader reader)
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
