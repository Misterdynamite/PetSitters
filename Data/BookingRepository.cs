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
                // Booking dates are calendar dates/times, not instants: stored WITHOUT
                // a time-zone offset. A Local-kind date (DateTime.Today, the form's
                // default) would otherwise be written as "...+13:00" and, read on a
                // PC in another time zone from the shared database, land on a
                // different day, shifting the dates shown and the overlap checks.
                command.AddParameter("@start", AsCalendarDate(booking.StartDate));
                command.AddParameter("@end", AsCalendarDate(booking.EndDate));
                command.AddParameter("@message", (object)booking.Message ?? DBNull.Value);
                command.AddParameter("@status", (int)booking.Status);
                command.AddParameter("@rate", booking.DailyRateAtBooking);
                command.AddParameter("@created", booking.CreatedUtc.ToString("o", CultureInfo.InvariantCulture));
                booking.Id = Convert.ToInt32(command.ExecuteScalar());
                return booking;
            }
        }

        /// <summary>
        /// Changes the status ONLY if it is currently one of
        /// <paramref name="expectedStatuses"/>, in a single UPDATE. Returns false,
        /// changing nothing and raising no event, when the booking has moved on:
        /// with a shared database the other person may have acted first (the
        /// owner cancelled while the sitter was looking at the request). The
        /// check and the write being one statement means there is no gap for
        /// that to slip through. BookingService uses this for every transition.
        /// </summary>
        public bool TryUpdateStatus(int bookingId, BookingStatus newStatus, params BookingStatus[] expectedStatuses)
        {
            if (expectedStatuses == null || expectedStatuses.Length == 0)
                throw new ArgumentException("At least one expected status is required.", nameof(expectedStatuses));

            int changed;
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                var placeholders = new List<string>();
                for (int i = 0; i < expectedStatuses.Length; i++)
                {
                    placeholders.Add("@expected" + i);
                    command.AddParameter("@expected" + i, (int)expectedStatuses[i]);
                }
                command.CommandText = "UPDATE Bookings SET Status = @status WHERE Id = @id AND Status IN (" +
                                      string.Join(", ", placeholders) + ");";
                command.AddParameter("@status", (int)newStatus);
                command.AddParameter("@id", bookingId);
                // Rows matched by the WHERE clause (both engines count the row even
                // if the value is unchanged), so 0 means "not in an expected state".
                changed = command.ExecuteNonQuery();
            }

            if (changed > 0)
            {
                try { BookingStatusChanged?.Invoke(bookingId, newStatus); } catch { }
            }
            return changed > 0;
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

        /// <summary>
        /// The owner's bookings, newest first, each with its sitter and pet, in
        /// ONE query. Feeds both the owner's My Bookings and Chats tabs (chats
        /// are the Accepted ones), replacing the old 1 + 2-per-booking lookups.
        /// </summary>
        public List<BookingDetails> GetDetailsForOwner(int ownerUserId)
        {
            return QueryDetails("OwnerUserId", ownerUserId);
        }

        /// <summary>The sitter's bookings with owner and pet, in ONE query (Booking Requests + My Chats).</summary>
        public List<BookingDetails> GetDetailsForSitter(int sitterUserId)
        {
            return QueryDetails("SitterUserId", sitterUserId);
        }

        private List<BookingDetails> QueryDetails(string column, int userId)
        {
            var details = new List<BookingDetails>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                // Column name is a hard-coded literal (never user input), so this is safe.
                // The booking's own columns come through b.* unprefixed (read by Map);
                // the joined owner, sitter and pet are prefixed o_, s_ and p_.
                command.CommandText =
                    "SELECT b.*, " +
                    DbCommandExtensions.AliasedColumns("o", "o_", UserRepository.PublicColumns) + ", " +
                    DbCommandExtensions.AliasedColumns("s", "s_", UserRepository.PublicColumns) + ", " +
                    DbCommandExtensions.AliasedColumns("p", "p_", PetRepository.Columns) +
                    " FROM Bookings b" +
                    " LEFT JOIN Users o ON o.Id = b.OwnerUserId" +
                    " LEFT JOIN Users s ON s.Id = b.SitterUserId" +
                    " LEFT JOIN Pets p ON p.Id = b.PetId" +
                    " WHERE b." + column + " = @userId" +
                    " ORDER BY b.CreatedUtc DESC, b.Id DESC;";
                command.AddParameter("@userId", userId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        details.Add(new BookingDetails
                        {
                            Booking = Map(reader),
                            Owner = UserRepository.MapJoined(reader, "o_"),
                            Sitter = UserRepository.MapJoined(reader, "s_"),
                            Pet = PetRepository.MapJoined(reader, "p_"),
                        });
                }
            }
            return details;
        }

        private List<Booking> Query(string column, int userId)
        {
            var bookings = new List<Booking>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                // Column name is a hard-coded literal (never user input), so this is safe.
                command.CommandText = "SELECT * FROM Bookings WHERE " + column + " = @userId ORDER BY CreatedUtc DESC, Id DESC;";   // Id breaks timestamp ties the same way on both engines
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
                StartDate = ParseCalendarDate((string)reader["StartDate"]),
                EndDate = ParseCalendarDate((string)reader["EndDate"]),
                Message = reader["Message"] as string,
                Status = (BookingStatus)Convert.ToInt32(reader["Status"]),
                DailyRateAtBooking = Convert.ToDecimal(reader["DailyRateAtBooking"]),
                CreatedUtc = DateTime.Parse((string)reader["CreatedUtc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            };
        }

        /// <summary>
        /// Reads a booking date as the wall-clock value it was written with.
        /// Rows from before offset-free storage (older local databases) carry an
        /// offset such as "+13:00". Parsing those with RoundtripKind would convert
        /// them into THIS PC's time zone and, elsewhere, onto a different day, so
        /// the offset is ignored and the clock value kept.
        /// </summary>
        private static DateTime ParseCalendarDate(string text)
        {
            DateTime clock = DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal).DateTime;
            return DateTime.SpecifyKind(clock, DateTimeKind.Unspecified);
        }

        /// <summary>ISO-8601 round-trip text with no offset/"Z": the same wall-clock value everywhere.</summary>
        private static string AsCalendarDate(DateTime value)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Unspecified).ToString("o", CultureInfo.InvariantCulture);
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
