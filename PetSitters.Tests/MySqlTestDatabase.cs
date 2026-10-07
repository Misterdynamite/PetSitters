using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySqlConnector;
using PetSitters.Data;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// The throwaway MySQL database used by the MySQL parity tests.
    ///
    /// STRICTLY OPT-IN, because the run takes minutes over the internet and
    /// creates (then drops) a database on a shared server. Visual Studio's "Run
    /// All Tests" ignores command-line filters, so the opt-in has to live here:
    /// 1. <c>PETSITTERS_TEST_MYSQL_URL</c> = a mysql:// URL for a server you can create
    ///    databases on (its database part is ignored); or
    /// 2. <c>PETSITTERS_TEST_MYSQL=1</c> = reuse the server from the app's DATABASE_URL
    ///    (environment variable, or the repo-root .env found by walking up from
    ///    the test output folder).
    /// Without either, every MySQL test is reported Inconclusive (skipped), never
    /// failed. That's what happens in CI and in a normal local run.
    ///
    /// SAFETY: tests never touch the app's database. Each run creates its own
    /// database named <c>sitters4us_test_&lt;UTC time&gt;_&lt;random&gt;</c>, empties it
    /// before every test, and drops it when the run ends (<see cref="MySqlParityTestRun"/>).
    /// A crashed run's leftover is cleaned up by the next run once it's older
    /// than <see cref="StaleAfter"/>. Unique names mean two people can run the
    /// suite at the same time without wiping each other's rows.
    /// </summary>
    internal static class MySqlTestDatabase
    {
        public const string TestUrlVariable = "PETSITTERS_TEST_MYSQL_URL";
        public const string UseAppServerVariable = "PETSITTERS_TEST_MYSQL";
        private const string NamePrefix = "sitters4us_test_";
        private const string TimeFormat = "yyyyMMddTHHmm";
        private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

        private static readonly object Gate = new object();
        private static bool _resolved;
        private static string _skipReason;
        private static string _serverConnectionString;   // no database selected
        private static string _testConnectionString;     // the run's own test database
        private static string _testDatabaseName;

        /// <summary>
        /// A <see cref="Database"/> on this run's test database, with every table
        /// emptied, i.e. as isolated as the per-test SQLite file. Marks the test
        /// Inconclusive when no MySQL server is configured.
        /// </summary>
        public static Database CreateForTest()
        {
            EnsureTestDatabase();
            if (_skipReason != null)
                Assert.Inconclusive(_skipReason);

            var database = Database.ForMySql(_testConnectionString);
            database.Initialize();   // CREATE TABLE IF NOT EXISTS (first test creates, later ones no-op)
            Execute(_testConnectionString,
                "DELETE FROM ChatMessages; DELETE FROM Bookings; DELETE FROM Pets; DELETE FROM SitterProfiles; DELETE FROM Users;");
            return database;
        }

        /// <summary>Drops this run's test database, if one was created. Called once after all tests.</summary>
        public static void DropTestDatabase()
        {
            lock (Gate)
            {
                if (_testDatabaseName == null)
                    return;
                Execute(_serverConnectionString, "DROP DATABASE IF EXISTS `" + _testDatabaseName + "`;");
                _testDatabaseName = null;
            }
        }

        private static void EnsureTestDatabase()
        {
            lock (Gate)
            {
                if (_resolved)
                    return;
                _resolved = true;

                string url = Environment.GetEnvironmentVariable(TestUrlVariable);
                string appUrl = Environment.GetEnvironmentVariable(AppConfig.DatabaseUrlKey) ?? FindRepoEnvValue(AppConfig.DatabaseUrlKey);
                if (string.IsNullOrWhiteSpace(url) && Environment.GetEnvironmentVariable(UseAppServerVariable) == "1")
                    url = appUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    _skipReason = "MySQL parity tests are opt-in and were skipped. To run them, set " + TestUrlVariable +
                                  " to a mysql:// URL, or " + UseAppServerVariable + "=1 to use the server in DATABASE_URL / .env.";
                    return;
                }

                var server = new MySqlConnectionStringBuilder(MySqlUrl.ToConnectionString(url, 15));
                string appDatabase = string.IsNullOrWhiteSpace(appUrl) ? null
                    : new MySqlConnectionStringBuilder(MySqlUrl.ToConnectionString(appUrl)).Database;

                string now = DateTime.UtcNow.ToString(TimeFormat, CultureInfo.InvariantCulture);
                _testDatabaseName = NamePrefix + now + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                // Belt and braces: the generated name can never equal the app's
                // database, but refuse loudly if it somehow did.
                if (string.Equals(_testDatabaseName, appDatabase, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing to use the app's own database for tests.");

                server.Database = string.Empty;
                _serverConnectionString = server.ConnectionString;
                server.Database = _testDatabaseName;
                _testConnectionString = server.ConnectionString;

                DropStaleTestDatabases();
                Execute(_serverConnectionString,
                    "CREATE DATABASE `" + _testDatabaseName + "` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_ci;");
            }
        }

        /// <summary>Removes test databases left by crashed runs (only our prefix, only old ones).</summary>
        private static void DropStaleTestDatabases()
        {
            var stale = new List<string>();
            using (var connection = new MySqlConnection(_serverConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME LIKE 'sitters4us\\_test\\_%';";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string name = reader.GetString(0);
                            string stamp = name.Substring(NamePrefix.Length, Math.Min(TimeFormat.Length, name.Length - NamePrefix.Length));
                            if (DateTime.TryParseExact(stamp, TimeFormat, CultureInfo.InvariantCulture,
                                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime created) &&
                                DateTime.UtcNow - created > StaleAfter)
                                stale.Add(name);
                        }
                    }
                }
            }
            foreach (string name in stale)
                Execute(_serverConnectionString, "DROP DATABASE IF EXISTS `" + name + "`;");
        }

        private static void Execute(string connectionString, string sql)
        {
            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Reads one key from the nearest .env above the test output folder (the repo root's).</summary>
        private static string FindRepoEnvValue(string key)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, AppConfig.EnvFileName);
                if (File.Exists(candidate))
                    return EnvFile.Load(candidate).TryGetValue(key, out string value) ? value : null;
            }
            return null;
        }
    }

    /// <summary>Run-level hook: drops the MySQL test database after all tests finish.</summary>
    [TestClass]
    public class MySqlParityTestRun
    {
        [AssemblyCleanup]
        public static void DropMySqlTestDatabase()
        {
            MySqlTestDatabase.DropTestDatabase();
        }
    }
}
