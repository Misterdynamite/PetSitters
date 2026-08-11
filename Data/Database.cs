using System;
using System.Data.SQLite;
using System.IO;

namespace PetSitters.Data
{
    /// <summary>
    /// Owns the SQLite connection string and creates the schema on first run.
    /// The database file path is injectable so automated tests can point at a
    /// throwaway temp file (or ":memory:") instead of the real AppData store.
    /// </summary>
    public class Database
    {
        private readonly string _connectionString;

        /// <summary>Full path to the .db file (or ":memory:").</summary>
        public string DataSource { get; }

        public Database(string dataSource)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
                throw new ArgumentException("Data source is required.", nameof(dataSource));

            DataSource = dataSource;
            // ForeignKeys=True enforces our FK relationships at the engine level.
            _connectionString = "Data Source=" + dataSource + ";Version=3;ForeignKeys=True;";
        }

        /// <summary>
        /// Builds a Database pointing at %AppData%\PetSitters\petsitters.db,
        /// creating the folder if needed. This is what the running app uses.
        /// </summary>
        public static Database CreateDefault()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "PetSitters");
            Directory.CreateDirectory(folder);
            string dbPath = Path.Combine(folder, "petsitters.db");
            return new Database(dbPath);
        }

        /// <summary>Opens a fresh, already-open connection. Caller disposes it.</summary>
        public SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Creates all tables if they do not yet exist. Safe to call on every startup.
        /// </summary>
        public void Initialize()
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Email         TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    PasswordHash  TEXT    NOT NULL,
    PasswordSalt  TEXT    NOT NULL,
    Role          INTEGER NOT NULL,
    FullName      TEXT    NOT NULL,
    Phone         TEXT,
    Location      TEXT,
    CreatedUtc    TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS SitterProfiles (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId           INTEGER NOT NULL UNIQUE,
    Availability     TEXT,
    ExperienceYears  INTEGER NOT NULL DEFAULT 0,
    Preferences      TEXT,
    Qualifications   TEXT,
    DailyRate        REAL    NOT NULL DEFAULT 0,
    Bio              TEXT,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Pets (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId  INTEGER NOT NULL,
    Name         TEXT    NOT NULL,
    Species      TEXT,
    Breed        TEXT,
    Age          INTEGER NOT NULL DEFAULT 0,
    Notes        TEXT,
    FOREIGN KEY (OwnerUserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Bookings (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId         INTEGER NOT NULL,
    SitterUserId        INTEGER NOT NULL,
    PetId               INTEGER,
    StartDate           TEXT    NOT NULL,
    EndDate             TEXT    NOT NULL,
    Message             TEXT,
    Status              INTEGER NOT NULL DEFAULT 0,
    DailyRateAtBooking  REAL    NOT NULL DEFAULT 0,
    CreatedUtc          TEXT    NOT NULL,
    FOREIGN KEY (OwnerUserId)  REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY (SitterUserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY (PetId)        REFERENCES Pets(Id)  ON DELETE SET NULL
);";
                command.ExecuteNonQuery();
                // Chat messages tied to a specific booking. Only the booking's owner
                // and sitter should be able to read/write rows for that booking.
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS ChatMessages (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    BookingId     INTEGER NOT NULL,
    SenderUserId  INTEGER NOT NULL,
    MessageText   TEXT    NOT NULL,
    CreatedUtc    TEXT    NOT NULL,
    FOREIGN KEY (BookingId)    REFERENCES Bookings(Id) ON DELETE CASCADE,
    FOREIGN KEY (SenderUserId) REFERENCES Users(Id)    ON DELETE CASCADE
);
";
                command.ExecuteNonQuery();
            }
        }
    }
}
