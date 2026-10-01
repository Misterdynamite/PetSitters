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
                command.CommandText = UsersTableSql("IF NOT EXISTS Users") + @"

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
    AgeMonths    INTEGER NOT NULL DEFAULT 0,
    ImagePath    TEXT,
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

            ApplyMigrations();
        }

        /// <summary>
        /// The Users table definition, shared by first-run creation and the
        /// REQ-GR-06 rebuild so the two can never drift apart.
        /// <paramref name="nameClause"/> is a hard-coded literal ("IF NOT EXISTS
        /// Users" or "Users_new"), never user input.
        /// </summary>
        private static string UsersTableSql(string nameClause)
        {
            // REQ-GR-06: an email may hold one Owner AND one Sitter account, so
            // uniqueness is on (Email, Role), not Email alone. The index uses
            // Email's NOCASE collation, so "A@x.com" and "a@x.com" still collide.
            return @"
CREATE TABLE " + nameClause + @" (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Email         TEXT    NOT NULL COLLATE NOCASE,
    PasswordHash  TEXT    NOT NULL,
    PasswordSalt  TEXT    NOT NULL,
    Role          INTEGER NOT NULL,
    FullName      TEXT    NOT NULL,
    Phone         TEXT,
    Location      TEXT,
    ProfileImagePath TEXT,
    CreatedUtc    TEXT    NOT NULL,
    UNIQUE (Email, Role)
);";
        }

        /// <summary>
        /// Brings an existing database up to date. "CREATE TABLE IF NOT EXISTS"
        /// only helps on a fresh file, so columns added after a release must be
        /// patched in here. Every step is idempotent and safe to re-run.
        /// </summary>
        private void ApplyMigrations()
        {
            // Pets.AgeMonths: added when pet age became "years + optional months".
            // Existing rows default to 0 months, preserving their recorded years.
            if (!ColumnExists("Pets", "AgeMonths"))
            {
                using (var connection = OpenConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "ALTER TABLE Pets ADD COLUMN AgeMonths INTEGER NOT NULL DEFAULT 0;";
                    command.ExecuteNonQuery();
                }
            }

            // Add ProfileImagePath to Users if missing
            if (!ColumnExists("Users", "ProfileImagePath"))
            {
                using (var connection = OpenConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "ALTER TABLE Users ADD COLUMN ProfileImagePath TEXT;";
                    command.ExecuteNonQuery();
                }
            }

            // Add ImagePath to Pets if missing
            if (!ColumnExists("Pets", "ImagePath"))
            {
                using (var connection = OpenConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "ALTER TABLE Pets ADD COLUMN ImagePath TEXT;";
                    command.ExecuteNonQuery();
                }
            }

            // REQ-GR-06: databases created before this change have UNIQUE on
            // Email alone, which blocks an owner from also registering as a
            // sitter. SQLite cannot drop a constraint, so the table is rebuilt.
            if (HasUniqueIndexOnEmailOnly())
                RebuildUsersWithPerRoleUniqueEmail();
        }

        /// <summary>
        /// True if Users still carries the pre-REQ-GR-06 constraint: a unique
        /// index whose only column is Email.
        /// </summary>
        private bool HasUniqueIndexOnEmailOnly()
        {
            using (var connection = OpenConnection())
            {
                var uniqueIndexes = new System.Collections.Generic.List<string>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA index_list(Users);";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (Convert.ToInt32(reader["unique"]) == 1)
                                uniqueIndexes.Add((string)reader["name"]);
                        }
                    }
                }

                foreach (string index in uniqueIndexes)
                {
                    var columns = new System.Collections.Generic.List<string>();
                    using (var command = connection.CreateCommand())
                    {
                        // Index names come from SQLite's own catalogue, never user
                        // input; quoted as an identifier for safety.
                        command.CommandText = "PRAGMA index_info(\"" + index + "\");";
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                                columns.Add(reader["name"] as string);
                        }
                    }

                    if (columns.Count == 1 && string.Equals(columns[0], "Email", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Rebuilds Users with UNIQUE (Email, Role), keeping every row and Id.
        /// Follows SQLite's documented table-rebuild procedure. Foreign keys MUST
        /// be off: with them on, DROP TABLE Users would fire the ON DELETE
        /// CASCADE rules and wipe every pet, booking, profile and chat message.
        /// </summary>
        private void RebuildUsersWithPerRoleUniqueEmail()
        {
            const string columns =
                "Id, Email, PasswordHash, PasswordSalt, Role, FullName, Phone, Location, ProfileImagePath, CreatedUtc";

            using (var connection = OpenConnection())
            {
                Execute(connection, "PRAGMA foreign_keys = OFF;");   // no-op inside a transaction, so set first
                try
                {
                    using (var transaction = connection.BeginTransaction())
                    {
                        Execute(connection, "DROP TABLE IF EXISTS Users_new;");
                        Execute(connection, UsersTableSql("Users_new"));
                        Execute(connection, "INSERT INTO Users_new (" + columns + ") SELECT " + columns + " FROM Users;");
                        Execute(connection, "DROP TABLE Users;");
                        Execute(connection, "ALTER TABLE Users_new RENAME TO Users;");
                        transaction.Commit();
                    }
                }
                finally
                {
                    Execute(connection, "PRAGMA foreign_keys = ON;");
                }
            }
        }

        /// <summary>Runs one hard-coded statement on an open connection.</summary>
        private static void Execute(SQLiteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        /// <summary>True if the given table already has the given column.</summary>
        private bool ColumnExists(string table, string column)
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                // Table name is a hard-coded literal from our own schema, never user input.
                command.CommandText = "PRAGMA table_info(" + table + ");";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader["name"] as string, column, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
