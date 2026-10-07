using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using PetSitters.Models;

namespace PetSitters.Data
{
    /// <summary>Reads and writes <see cref="Booking"/> rows.</summary>
    public class BookingRepository
    {
        private readonly Database _db;
        /// <summary>
        /// Raised after a booking's status is updated. Parameters: bookingId, new status.
        /// </summary>
        public event System.Action<int, PetSitters.Models.BookingStatus> BookingStatusChanged;

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
" + _db.Dialect.SelectLastInsertId;
                command.AddParameter("@owner", booking.OwnerUserId);
                command.AddParameter("@sitter", booking.SitterUserId);
                command.AddParameter("@pet", (object)booking.PetId ?? DBNull.Value);
                command.AddParameter("@start", booking.StartDate.ToString("o", CultureInfo.InvariantCulture));
                command.AddParameter("@end", booking.EndDate.ToString("o", CultureInfo.InvariantCulture));
                command.AddParameter("@message", (object)booking.Message ?? DBNull.Value);
                command.AddParameter("@status", (int)booking.Status);
                command.AddParameter("@rate", booking.DailyRateAtBooking);
                command.AddParameter("@created", booking.CreatedUtc.ToString("o", CultureInfo.InvariantCulture));
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
                command.AddParameter("@status", (int)status);
                command.AddParameter("@id", bookingId);
                command.ExecuteNonQuery();
            }
            // notify subscribers after the database update
            try { BookingStatusChanged?.Invoke(bookingId, status); } catch { }
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
                command.AddParameter("@userId", userId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        bookings.Add(Map(reader));
                }
            }
            return bookings;
        }

        private static Booking Map(DbDataReader reader)
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
                command.AddParameter("@id", id);
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
