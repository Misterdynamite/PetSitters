using System;
using System.Data.Common;
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
                command.AddParameter("@userId", userId);
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
                // One profile per sitter (UNIQUE UserId): insert, or update it in
                // place. The upsert clause is engine-specific (see SqlDialect.Upsert).
                command.CommandText = @"
INSERT INTO SitterProfiles (UserId, Availability, ExperienceYears, Preferences, Qualifications, DailyRate, Bio)
VALUES (@userId, @availability, @exp, @prefs, @quals, @rate, @bio)"
                    + _db.Dialect.Upsert("UserId",
                        "Availability", "ExperienceYears", "Preferences", "Qualifications", "DailyRate", "Bio")
                    + ";";
                command.AddParameter("@userId", profile.UserId);
                command.AddParameter("@availability", (object)profile.Availability ?? DBNull.Value);
                command.AddParameter("@exp", profile.ExperienceYears);
                command.AddParameter("@prefs", (object)profile.Preferences ?? DBNull.Value);
                command.AddParameter("@quals", (object)profile.Qualifications ?? DBNull.Value);
                command.AddParameter("@rate", profile.DailyRate);
                command.AddParameter("@bio", (object)profile.Bio ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }

        private static SitterProfile Map(DbDataReader reader)
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
