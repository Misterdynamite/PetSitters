using System;
using System.Data.SQLite;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Data;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Integration test for the REQ-GR-06 schema migration: a database created
    /// before the change (UNIQUE on Email alone) is rebuilt to
    /// UNIQUE (Email, Role) on startup WITHOUT losing data. The risk being
    /// guarded is the rebuild's DROP TABLE Users firing ON DELETE CASCADE and
    /// silently wiping every pet and booking (S1 data loss).
    ///
    /// Not a <see cref="DatabaseTestBase"/>: that creates a current-schema
    /// database, whereas this test must start from the old one.
    /// </summary>
    [TestClass]
    public class DatabaseMigrationTests
    {
        private string _dbPath;

        [TestInitialize]
        public void CreateOldSchemaDatabase()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"petsitters_migration_{Guid.NewGuid():N}.db");

            // The Users/Pets/Bookings schema exactly as shipped before REQ-GR-06.
            using (var connection = new SQLiteConnection("Data Source=" + _dbPath + ";Version=3;ForeignKeys=True;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, Email TEXT NOT NULL UNIQUE COLLATE NOCASE,
    PasswordHash TEXT NOT NULL, PasswordSalt TEXT NOT NULL, Role INTEGER NOT NULL,
    FullName TEXT NOT NULL, Phone TEXT, Location TEXT, ProfileImagePath TEXT, CreatedUtc TEXT NOT NULL);
CREATE TABLE Pets (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerUserId INTEGER NOT NULL, Name TEXT NOT NULL,
    Species TEXT, Breed TEXT, Age INTEGER NOT NULL DEFAULT 0, AgeMonths INTEGER NOT NULL DEFAULT 0,
    ImagePath TEXT, Notes TEXT,
    FOREIGN KEY (OwnerUserId) REFERENCES Users(Id) ON DELETE CASCADE);
CREATE TABLE Bookings (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerUserId INTEGER NOT NULL, SitterUserId INTEGER NOT NULL,
    PetId INTEGER, StartDate TEXT NOT NULL, EndDate TEXT NOT NULL, Message TEXT,
    Status INTEGER NOT NULL DEFAULT 0, DailyRateAtBooking REAL NOT NULL DEFAULT 0, CreatedUtc TEXT NOT NULL,
    FOREIGN KEY (OwnerUserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY (SitterUserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY (PetId) REFERENCES Pets(Id) ON DELETE SET NULL);
INSERT INTO Users (Id, Email, PasswordHash, PasswordSalt, Role, FullName, CreatedUtc)
    VALUES (7, 'olivia@test.com', 'h', 's', 0, 'Olivia', '2026-01-01T00:00:00.0000000Z'),
           (9, 'sam@test.com',    'h', 's', 1, 'Sam',    '2026-01-01T00:00:00.0000000Z');
INSERT INTO Pets (Id, OwnerUserId, Name) VALUES (3, 7, 'Rex');
INSERT INTO Bookings (OwnerUserId, SitterUserId, PetId, StartDate, EndDate, CreatedUtc)
    VALUES (7, 9, 3, '2030-03-10T00:00:00.0000000', '2030-03-13T00:00:00.0000000', '2026-01-01T00:00:00.0000000Z');";
                    command.ExecuteNonQuery();
                }
            }
        }

        [TestCleanup]
        public void DeleteDatabase()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { File.Delete(_dbPath); } catch (IOException) { /* best effort, temp file */ }
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Regression")]
        [TestCategory("Positive")]
        // REQ-GR-06 + DEF-001-class risk (data loss): existing rows, ids and
        // relationships survive the rebuild, and the new rule then applies.
        public void Initialize_OnPreGr06Database_KeepsAllDataAndAllowsSecondRole()
        {
            var services = new AppServices(new Database(_dbPath));   // runs Initialize() -> migration

            User olivia = services.Users.FindById(7);
            Assert.IsNotNull(olivia, "Users must survive the rebuild with their ids intact.");
            Assert.AreEqual("olivia@test.com", olivia.Email);
            Assert.AreEqual(1, services.Pets.GetByOwner(7).Count, "Pets must not be cascade-deleted.");
            Assert.AreEqual(1, services.Bookings.GetForOwner(7).Count, "Bookings must not be cascade-deleted.");
            Assert.AreEqual(3, services.Bookings.GetForOwner(7)[0].PetId, "Booking -> pet link must survive.");

            AuthResult sitterWithSameEmail = services.Auth.Register("olivia@test.com", "secret1",
                UserRole.Sitter, "Olivia", "021", "Wellington");
            Assert.IsTrue(sitterWithSameEmail.Success, "After migration an owner's email can register as a sitter: "
                + sitterWithSameEmail.ErrorMessage);
            Assert.IsTrue(sitterWithSameEmail.User.Id > 9, "New ids must continue after the existing ones.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        [TestCategory("Integration")]
        [TestCategory("Regression")]
        [TestCategory("Positive")]
        // Initialize runs on every app start, so the migration must be a no-op the second time.
        public void Initialize_RunTwice_IsIdempotent()
        {
            new AppServices(new Database(_dbPath));
            var services = new AppServices(new Database(_dbPath));

            Assert.AreEqual(1, services.Pets.GetByOwner(7).Count);
            Assert.AreEqual(1, services.Bookings.GetForSitter(9).Count);
        }
    }
}
