using System;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using MySqlConnector;

namespace PetSitters.Data
{
    /// <summary>
    /// Owns the connection settings for ONE database (local SQLite file or the
    /// shared MySQL server) and creates its schema. Repositories only see
    /// provider-neutral <see cref="DbConnection"/>s plus <see cref="Dialect"/>,
    /// so the same queries run on both engines. Which engine the app uses is
    /// decided once at launch by <c>Services.DatabaseSelector</c>: MySQL first,
    /// SQLite if MySQL can't be reached.
    /// The SQLite path is injectable so automated tests can point at a throwaway
    /// temp file instead of the real AppData store.
    /// </summary>
    public class Database
    {
        private readonly string _connectionString;

        // Initialize() runs its DDL once per instance: the startup check calls it
        // to prove the server is reachable, and AppServices calls it again, which
        // would otherwise cost the cloud database an extra round trip.
        private bool _initialized;

        /// <summary>The engine this instance talks to.</summary>
        public DatabaseProvider Provider { get; }

        /// <summary>
        /// Where the data lives, for display and diagnostics: the .db file path
        /// for SQLite, or "server:port/database" for MySQL. Never contains credentials.
        /// </summary>
        public string DataSource { get; }

        /// <summary>The SQL fragments that differ between the two engines.</summary>
        internal SqlDialect Dialect { get; }

        /// <summary>A SQLite database stored in the file at <paramref name="dataSource"/> (or ":memory:").</summary>
        public Database(string dataSource)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
                throw new ArgumentException("Data source is required.", nameof(dataSource));

            Provider = DatabaseProvider.Sqlite;
            Dialect = SqlDialect.Sqlite;
            DataSource = dataSource;
            // ForeignKeys=True enforces our FK relationships at the engine level.
            _connectionString = "Data Source=" + dataSource + ";Version=3;ForeignKeys=True;";
        }

        private Database(string mySqlConnectionString, string displayName)
        {
            Provider = DatabaseProvider.MySql;
            Dialect = SqlDialect.MySql;
            DataSource = displayName;
            _connectionString = mySqlConnectionString;
        }

        /// <summary>
        /// A MySQL database reached through <paramref name="connectionString"/>
        /// (build one from a mysql:// URL with <see cref="MySqlUrl.ToConnectionString"/>).
        /// Nothing connects until the first query or <see cref="Initialize"/>.
        /// </summary>
        public static Database ForMySql(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("A MySQL connection string is required.", nameof(connectionString));

            var settings = new MySqlConnectionStringBuilder(connectionString);
            return new Database(connectionString, settings.Server + ":" + settings.Port + "/" + settings.Database);
        }

        /// <summary>
        /// The local SQLite store at %AppData%\PetSitters\petsitters.db, creating
        /// the folder if needed. Used when no cloud database is configured or it
        /// can't be reached at launch.
        /// </summary>
        public static Database CreateLocalSqlite()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "PetSitters");
            Directory.CreateDirectory(folder);
            string dbPath = Path.Combine(folder, "petsitters.db");
            return new Database(dbPath);
        }

        /// <summary>
        /// Opens a fresh, already-open connection. Caller disposes it. For MySQL
        /// this normally reuses a pooled connection (see <see cref="MySqlUrl"/>),
        /// so it doesn't repeat the TLS handshake every time.
        /// </summary>
        public DbConnection OpenConnection()
        {
            DbConnection connection = Provider == DatabaseProvider.Sqlite
                ? (DbConnection)new SQLiteConnection(_connectionString)
                : new MySqlConnection(_connectionString);
            try
            {
                connection.Open();
            }
            catch
            {
                connection.Dispose();
                throw;
            }
            return connection;
        }

        /// <summary>
        /// Creates all tables if they don't exist yet, and brings older local
        /// databases up to date. Safe to call on every startup, and only does the
        /// work once per instance. For MySQL this is also the launch-time proof
        /// that the server is reachable: it throws if it isn't.
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
                return;

            if (Provider == DatabaseProvider.Sqlite)
                InitializeSqlite();
            else
                InitializeMySql();

            _initialized = true;
        }

        private void InitializeSqlite()
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
        /// The MySQL schema: the same tables, columns and keys as SQLite, in
        /// MySQL's types. Decisions:
        /// - Email is VARCHAR(255): MySQL can't put a UNIQUE index on TEXT.
        ///   (ValidationHelper.IsValidEmail caps emails at 254 characters, the RFC
        ///   maximum, so STRICT mode never rejects a valid one.)
        ///   Other free text is TEXT (64 KB), and the multi-line fields (notes, bios,
        ///   messages, availability, ...) are MEDIUMTEXT (16 MB). The server runs in
        ///   STRICT mode, which REJECTS over-long values rather than truncating them,
        ///   and the UI sets no length limits, so a pasted wall of text must still fit.
        /// - Dates stay ISO-8601 round-trip strings (VARCHAR(40)), exactly as in
        ///   SQLite, so both engines share the repositories' read/write code, and
        ///   ORDER BY on them still sorts chronologically.
        /// - Money is DECIMAL(10,2), not floating point.
        /// - Collation utf8mb4_0900_as_ci: case-insensitive (so the email + role
        ///   uniqueness matches SQLite's COLLATE NOCASE) but accent-sensitive, so
        ///   "jose@" and "josé@" stay different, like SQLite.
        /// - Every table has a PRIMARY KEY (the server enforces sql_require_primary_key).
        /// All CREATE statements go in ONE command, one round trip instead of five,
        /// and run only when the existence check finds any of the five tables
        /// missing (normally just the first launch).
        /// </summary>
        private void InitializeMySql()
        {
            const string table = " ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_as_ci;";
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                // Create only when something is missing. On a normal launch this is
                // the single round trip. It also lets the app run under a
                // least-privilege database user (SELECT/INSERT/UPDATE/DELETE only):
                // MySQL checks the CREATE privilege even for CREATE TABLE IF NOT
                // EXISTS on a table that exists. Table names are case-sensitive on
                // this server (lower_case_table_names=0), as written here.
                command.CommandText =
                    "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() " +
                    "AND TABLE_NAME IN ('Users', 'SitterProfiles', 'Pets', 'Bookings', 'ChatMessages');";
                if (Convert.ToInt32(command.ExecuteScalar()) == 5)
                    return;

                command.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
    Id               INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Email            VARCHAR(255) NOT NULL,
    PasswordHash     VARCHAR(255) NOT NULL,
    PasswordSalt     VARCHAR(255) NOT NULL,
    Role             INT          NOT NULL,
    FullName         TEXT         NOT NULL,
    Phone            TEXT         NULL,
    Location         TEXT         NULL,
    ProfileImagePath TEXT         NULL,
    CreatedUtc       VARCHAR(40)  NOT NULL,
    UNIQUE KEY UX_Users_Email_Role (Email, Role)
)" + table + @"

CREATE TABLE IF NOT EXISTS SitterProfiles (
    Id               INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    UserId           INT           NOT NULL,
    Availability     MEDIUMTEXT    NULL,
    ExperienceYears  INT           NOT NULL DEFAULT 0,
    Preferences      MEDIUMTEXT    NULL,
    Qualifications   MEDIUMTEXT    NULL,
    DailyRate        DECIMAL(10,2) NOT NULL DEFAULT 0,
    Bio              MEDIUMTEXT    NULL,
    UNIQUE KEY UX_SitterProfiles_UserId (UserId),
    CONSTRAINT FK_SitterProfiles_User FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
)" + table + @"

CREATE TABLE IF NOT EXISTS Pets (
    Id           INT  NOT NULL AUTO_INCREMENT PRIMARY KEY,
    OwnerUserId  INT  NOT NULL,
    Name         TEXT NOT NULL,
    Species      TEXT NULL,
    Breed        TEXT NULL,
    Age          INT  NOT NULL DEFAULT 0,
    AgeMonths    INT  NOT NULL DEFAULT 0,
    ImagePath    TEXT NULL,
    Notes        MEDIUMTEXT NULL,
    CONSTRAINT FK_Pets_Owner FOREIGN KEY (OwnerUserId) REFERENCES Users(Id) ON DELETE CASCADE
)" + table + @"

CREATE TABLE IF NOT EXISTS Bookings (
    Id                  INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    OwnerUserId         INT           NOT NULL,
    SitterUserId        INT           NOT NULL,
    PetId               INT           NULL,
    StartDate           VARCHAR(40)   NOT NULL,
    EndDate             VARCHAR(40)   NOT NULL,
    Message             MEDIUMTEXT    NULL,
    Status              INT           NOT NULL DEFAULT 0,
    DailyRateAtBooking  DECIMAL(10,2) NOT NULL DEFAULT 0,
    CreatedUtc          VARCHAR(40)   NOT NULL,
    CONSTRAINT FK_Bookings_Owner  FOREIGN KEY (OwnerUserId)  REFERENCES Users(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Bookings_Sitter FOREIGN KEY (SitterUserId) REFERENCES Users(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Bookings_Pet    FOREIGN KEY (PetId)        REFERENCES Pets(Id)  ON DELETE SET NULL
)" + table + @"

CREATE TABLE IF NOT EXISTS ChatMessages (
    Id            INT         NOT NULL AUTO_INCREMENT PRIMARY KEY,
    BookingId     INT         NOT NULL,
    SenderUserId  INT         NOT NULL,
    MessageText   MEDIUMTEXT  NOT NULL,
    CreatedUtc    VARCHAR(40) NOT NULL,
    CONSTRAINT FK_ChatMessages_Booking FOREIGN KEY (BookingId)    REFERENCES Bookings(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ChatMessages_Sender  FOREIGN KEY (SenderUserId) REFERENCES Users(Id)    ON DELETE CASCADE
)" + table;
                command.ExecuteNonQuery();
            }
            // No MySQL migrations yet: the MySQL schema started at the current
            // version. When it changes, add idempotent steps here (check
            // information_schema.COLUMNS / STATISTICS first, like ApplyMigrations does for SQLite).
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
        /// Brings an existing SQLITE database up to date (SQLite only: it uses
        /// PRAGMAs, and the MySQL schema started at the current version).
        /// "CREATE TABLE IF NOT EXISTS" only helps on a fresh file, so columns
        /// added after a release must be patched in here. Every step is
        /// idempotent and safe to re-run.
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
                    using (DbTransaction transaction = connection.BeginTransaction())
                    {
                        Execute(connection, "DROP TABLE IF EXISTS Users_new;", transaction);
                        Execute(connection, UsersTableSql("Users_new"), transaction);
                        Execute(connection, "INSERT INTO Users_new (" + columns + ") SELECT " + columns + " FROM Users;", transaction);
                        Execute(connection, "DROP TABLE Users;", transaction);
                        Execute(connection, "ALTER TABLE Users_new RENAME TO Users;", transaction);
                        transaction.Commit();
                    }
                }
                finally
                {
                    Execute(connection, "PRAGMA foreign_keys = ON;");
                }
            }
        }

        /// <summary>
        /// Runs one hard-coded statement on an open connection, inside
        /// <paramref name="transaction"/> when given. The transaction is set
        /// explicitly: SQLite would pick it up implicitly, but MySqlConnector
        /// rejects a command that doesn't name the connection's open transaction.
        /// </summary>
        private static void Execute(DbConnection connection, string sql, DbTransaction transaction = null)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
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
