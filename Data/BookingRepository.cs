using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using PetSitters.Models;

namespace PetSitters.Data
{
    /// <summary>Reads and writes <see cref="Booking"/> rows.</summary>
    public class BookingRepository
    {
        private readonly Database _db;

        public BookingRepository(Database db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Booking Insert(Booking booking)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO Bookings (OwnerUserId, SitterUserId, PetId, StartDate, EndDate, Message, Status, DailyRateAtBooking, CreatedUtc)
VALUES (@owner, @sitter, @pet, @start, @end, @message, @status, @rate, @created);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@owner", booking.OwnerUserId);
                command.Parameters.AddWithValue("@sitter", booking.SitterUserId);
                command.Parameters.AddWithValue("@pet", (object)booking.PetId ?? DBNull.Value);
                command.Parameters.AddWithValue("@start", booking.StartDate.ToString("o", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@end", booking.EndDate.ToString("o", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@message", (object)booking.Message ?? DBNull.Value);
                command.Parameters.AddWithValue("@status", (int)booking.Status);
                command.Parameters.AddWithValue("@rate", booking.DailyRateAtBooking);
                command.Parameters.AddWithValue("@created", booking.CreatedUtc.ToString("o", CultureInfo.InvariantCulture));
                booking.Id = Convert.ToInt32(command.ExecuteScalar());
                return booking;
            }
        }

        public void UpdateStatus(int bookingId, BookingStatus status)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE Bookings SET Status = @status WHERE Id = @id;";
                command.Parameters.AddWithValue("@status", (int)status);
                command.Parameters.AddWithValue("@id", bookingId);
                command.ExecuteNonQuery();
            }
        }

        public List<Booking> GetForOwner(int ownerUserId)
        {
            return Query("OwnerUserId", ownerUserId);
        }

        public List<Booking> GetForSitter(int sitterUserId)
        {
            return Query("SitterUserId", sitterUserId);
        }

        private List<Booking> Query(string column, int userId)
        {
            var bookings = new List<Booking>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                // Column name is a hard-coded literal (never user input), so this is safe.
                command.CommandText = "SELECT * FROM Bookings WHERE " + column + " = @userId ORDER BY CreatedUtc DESC;";
                command.Parameters.AddWithValue("@userId", userId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        bookings.Add(Map(reader));
                }
            }
            return bookings;
        }

        private static Booking Map(SQLiteDataReader reader)
        {
            object petId = reader["PetId"];
            return new Booking
            {
                Id = Convert.ToInt32(reader["Id"]),
                OwnerUserId = Convert.ToInt32(reader["OwnerUserId"]),
                SitterUserId = Convert.ToInt32(reader["SitterUserId"]),
                PetId = petId == DBNull.Value ? (int?)null : Convert.ToInt32(petId),
                StartDate = DateTime.Parse((string)reader["StartDate"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                EndDate = DateTime.Parse((string)reader["EndDate"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Message = reader["Message"] as string,
                Status = (BookingStatus)Convert.ToInt32(reader["Status"]),
                DailyRateAtBooking = Convert.ToDecimal(reader["DailyRateAtBooking"]),
                CreatedUtc = DateTime.Parse((string)reader["CreatedUtc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            };
        }

        public Booking GetById(int id)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Bookings WHERE Id = @id LIMIT 1;";
                command.Parameters.AddWithValue("@id", id);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                        return Map(reader);
                }
            }
            return null;
        }
    }
}
