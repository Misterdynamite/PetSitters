using System;
using System.Data.SQLite;
using PetSitters.Models;

namespace PetSitters.Data
{
    /// <summary>Reads and writes the 1:1 <see cref="SitterProfile"/> for a sitter user.</summary>
    public class SitterProfileRepository
    {
        private readonly Database _db;

        public SitterProfileRepository(Database db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public SitterProfile GetByUserId(int userId)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SitterProfiles WHERE UserId = @userId LIMIT 1;";
                command.Parameters.AddWithValue("@userId", userId);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? Map(reader) : null;
                }
            }
        }

        /// <summary>Inserts a new profile, or updates the existing one for this user.</summary>
        public void Upsert(SitterProfile profile)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO SitterProfiles (UserId, Availability, ExperienceYears, Preferences, Qualifications, DailyRate, Bio)
VALUES (@userId, @availability, @exp, @prefs, @quals, @rate, @bio)
ON CONFLICT(UserId) DO UPDATE SET
    Availability    = excluded.Availability,
    ExperienceYears = excluded.ExperienceYears,
    Preferences     = excluded.Preferences,
    Qualifications  = excluded.Qualifications,
    DailyRate       = excluded.DailyRate,
    Bio             = excluded.Bio;";
                command.Parameters.AddWithValue("@userId", profile.UserId);
                command.Parameters.AddWithValue("@availability", (object)profile.Availability ?? DBNull.Value);
                command.Parameters.AddWithValue("@exp", profile.ExperienceYears);
                command.Parameters.AddWithValue("@prefs", (object)profile.Preferences ?? DBNull.Value);
                command.Parameters.AddWithValue("@quals", (object)profile.Qualifications ?? DBNull.Value);
                command.Parameters.AddWithValue("@rate", profile.DailyRate);
                command.Parameters.AddWithValue("@bio", (object)profile.Bio ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }

        private static SitterProfile Map(SQLiteDataReader reader)
        {
            return new SitterProfile
            {
                Id = Convert.ToInt32(reader["Id"]),
                UserId = Convert.ToInt32(reader["UserId"]),
                Availability = reader["Availability"] as string,
                ExperienceYears = Convert.ToInt32(reader["ExperienceYears"]),
                Preferences = reader["Preferences"] as string,
                Qualifications = reader["Qualifications"] as string,
                DailyRate = Convert.ToDecimal(reader["DailyRate"]),
                Bio = reader["Bio"] as string
            };
        }
    }
}
