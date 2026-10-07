using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Data;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Base class for tests that need a real database.
    ///
    /// Lab 5 (test isolation): by default each test gets its OWN temporary
    /// SQLite file, created fresh in TestInitialize and deleted in TestCleanup.
    /// Tests never share state, so they can run in any order without interfering
    /// with one another, and without touching the real
    /// %AppData%\PetSitters\petsitters.db or the cloud database.
    ///
    /// MySQL parity: the classes in MySqlParityTests.cs inherit every test class
    /// built on this base and override <see cref="CreateDatabase"/>, so the SAME
    /// tests also run against MySQL (category "MySql", opt-in, see docs/CI.md).
    /// </summary>
    public abstract class DatabaseTestBase
    {
        private string _dbPath;

        /// <summary>The isolated database under test.</summary>
        protected Database Db { get; private set; }

        /// <summary>Repositories/services wired to <see cref="Db"/> (schema already created).</summary>
        protected AppServices Services { get; private set; }

        [TestInitialize]
        public void InitDatabase()
        {
            Db = CreateDatabase();
            // AppServices' constructor calls Database.Initialize(), creating the schema.
            Services = new AppServices(Db);
        }

        /// <summary>A fresh, empty database for one test. SQLite temp file unless overridden.</summary>
        protected virtual Database CreateDatabase()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"petsitters_test_{Guid.NewGuid():N}.db");
            return new Database(_dbPath);
        }

        [TestCleanup]
        public void CleanupDatabase()
        {
            if (_dbPath == null)
                return;   // not a SQLite temp file (e.g. the MySQL parity run): nothing to delete

            // System.Data.SQLite closes each connection per-operation (no pooling in
            // our connection string), so the file handle is free by now. Force a GC
            // first as a belt-and-braces measure, then best-effort delete.
            GC.Collect();
            GC.WaitForPendingFinalizers();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(_dbPath))
                        File.Delete(_dbPath);
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }
    }
}
