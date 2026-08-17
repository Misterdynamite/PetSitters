using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Data;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Base class for tests that need a real SQLite database.
    ///
    /// Lab 5 (test isolation): each test gets its OWN temporary database file,
    /// created fresh in TestInitialize and deleted in TestCleanup. Tests never
    /// share state, so they can run in any order (or in parallel) without
    /// interfering with one another and without touching the real
    /// %AppData%\PetSitters\petsitters.db used by the running app.
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
            _dbPath = Path.Combine(Path.GetTempPath(), $"petsitters_test_{Guid.NewGuid():N}.db");
            Db = new Database(_dbPath);
            // AppServices' constructor calls Database.Initialize(), creating the schema.
            Services = new AppServices(Db);
        }

        [TestCleanup]
        public void CleanupDatabase()
        {
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
