using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Data;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Tests for REQ-GR-09 (proposed: shared cloud database with local fallback
    /// at launch), i.e. the launch-time database choice (<see cref="DatabaseSelector"/>):
    /// MySQL first, SQLite when MySQL is switched off, not configured or
    /// unreachable. The cloud constructor is injected, so no test here contacts
    /// the real cloud server: "reachable" is simulated with a temp SQLite file,
    /// and "unreachable" uses a real MySQL connection to a closed local port,
    /// which is refused (after about 2 s on Windows, which retries a refused
    /// connection).
    /// </summary>
    [TestClass]
    public class DatabaseSelectorTests
    {
        private const string Secret = "not-the-real-password-123";
        private const string Url = "mysql://appuser:" + Secret + "@127.0.0.1:1/sitters";

        private readonly List<string> _tempFiles = new List<string>();

        [TestCleanup]
        public void DeleteTempFiles()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            foreach (string path in _tempFiles)
            {
                try { File.Delete(path); } catch (IOException) { /* best effort */ }
            }
        }

        [TestMethod]
        [TestCategory("Integration")]   // Initialize() runs on a real (temp SQLite) database
        [TestCategory("Positive")]
        [TestCategory("Smoke")]
        public void Select_CloudReachable_UsesCloud()
        {
            Database local = TempSqlite(), cloud = TempSqlite();

            DatabaseSelection selection = DatabaseSelector.Select(Config(Url), () => local, url => cloud);

            Assert.AreSame(cloud, selection.Database);
            Assert.IsTrue(selection.UsingCloud);
            Assert.IsFalse(selection.FellBack);
            Assert.AreEqual("Cloud database", selection.Summary);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
        [TestCategory("ErrorHandling")]
        [TestCategory("Smoke")]
        // The core requirement: MySQL can't be reached at launch -> SQLite, and say so.
        public void Select_CloudUnreachable_FallsBackToLocal_AndExplains()
        {
            Database local = TempSqlite();

            DatabaseSelection selection = DatabaseSelector.Select(Config(Url, timeoutSeconds: 3), () => local,
                url => Database.ForMySql(MySqlUrl.ToConnectionString(url, 3)));   // real driver, refused port

            Assert.AreSame(local, selection.Database);
            Assert.IsFalse(selection.UsingCloud);
            Assert.IsTrue(selection.FellBack, "Users must be told they are on unshared local data.");
            Assert.AreEqual("Offline: local database", selection.Summary);
            StringAssert.Contains(selection.Detail, "won't be seen by other users");
            Assert.IsFalse(selection.Detail.Contains("127.0.0.1"), "The tooltip must not name the server.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("ErrorHandling")]
        [TestCategory("Negative")]
        // Connected, but the schema step failed (e.g. a SQL error): still fall
        // back so the app opens, but report an ERROR, not "offline", so a real
        // bug isn't hidden behind a network-looking message.
        public void Select_FailureAfterConnecting_IsReportedAsError_NotOffline()
        {
            DatabaseSelection selection = DatabaseSelector.Select(Config(Url), TempSqlite,
                url => throw new InvalidOperationException("simulated SQL error while creating tables"));

            Assert.IsTrue(selection.FellBack);
            Assert.AreEqual("Cloud database error: local database", selection.Summary);
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Negative")]
        [DataRow("format", true, DisplayName = "Bad URL counts as can't-connect")]
        [DataRow("socket", true, DisplayName = "Network error")]
        [DataRow("timeout", true, DisplayName = "Timeout")]
        [DataRow("wrapped-timeout", true, DisplayName = "Timeout wrapped by the driver")]
        [DataRow("other", false, DisplayName = "Anything else is a real error")]
        public void IsConnectivityFailure_Classification(string kind, bool expected)
        {
            Exception ex;
            switch (kind)
            {
                case "format": ex = new FormatException("x"); break;
                case "socket": ex = new System.Net.Sockets.SocketException(10061); break;
                case "timeout": ex = new TimeoutException(); break;
                case "wrapped-timeout": ex = new InvalidOperationException("outer", new TimeoutException()); break;
                default: ex = new InvalidOperationException("x"); break;
            }

            Assert.AreEqual(expected, DatabaseSelector.IsConnectivityFailure(ex));
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Security")]
        [TestCategory("ErrorHandling")]
        // Even if a driver error echoed the URL, the tooltip must not show the password.
        public void Select_CloudFailureMessage_NeverContainsThePassword()
        {
            DatabaseSelection selection = DatabaseSelector.Select(Config(Url), TempSqlite,
                url => throw new InvalidOperationException("could not connect using " + url + " (password " + Secret + ")"));

            Assert.IsTrue(selection.FellBack);
            Assert.IsFalse(selection.Detail.Contains(Secret), "Password leaked into: " + selection.Detail);
            Assert.IsFalse(selection.Detail.Contains(Url), "URL leaked into: " + selection.Detail);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("InvalidInput")]
        [TestCategory("ErrorHandling")]
        // A malformed DATABASE_URL must not stop the app starting.
        public void Select_MalformedUrl_FallsBackToLocal()
        {
            Database local = TempSqlite();

            DatabaseSelection selection = DatabaseSelector.Select(Config("postgres://wrong"), () => local,
                url => Database.ForMySql(MySqlUrl.ToConnectionString(url)));

            Assert.AreSame(local, selection.Database);
            Assert.IsTrue(selection.FellBack);
            StringAssert.Contains(selection.Detail, "mysql://");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        public void Select_ForcedLocal_NeverTriesTheCloud()
        {
            Database local = TempSqlite();
            var config = AppConfig.FromValues(new Dictionary<string, string> { ["DATABASE_URL"] = Url, ["PETSITTERS_DB"] = "sqlite" });

            DatabaseSelection selection = DatabaseSelector.Select(config, () => local,
                url => throw new AssertFailedException("The cloud must not be attempted when PETSITTERS_DB=sqlite."));

            Assert.AreSame(local, selection.Database);
            Assert.IsFalse(selection.FellBack, "Choosing local on purpose is not a failure.");
            Assert.AreEqual("Local database", selection.Summary);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        public void Select_NoUrl_UsesLocalWithoutWarning()
        {
            Database local = TempSqlite();

            DatabaseSelection selection = DatabaseSelector.Select(Config(null), () => local,
                url => throw new AssertFailedException("No URL: the cloud must not be attempted."));

            Assert.AreSame(local, selection.Database);
            Assert.IsFalse(selection.FellBack);
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Security")]
        [TestCategory("Positive")]
        [DataRow("mysql://u:" + Secret + "@h/db", DisplayName = "Plain password")]
        [DataRow("mysql://u:p%40ss%3Aword@h/db", DisplayName = "Percent-encoded password (raw and decoded forms)")]
        public void Redact_RemovesUrlAndPassword(string url)
        {
            string decodedPassword = Uri.UnescapeDataString(url.Substring(url.IndexOf(':', 6) + 1, url.IndexOf('@') - url.IndexOf(':', 6) - 1));
            string text = "failed for " + url + " with password " + decodedPassword;

            string redacted = DatabaseSelector.Redact(text, url);

            Assert.IsFalse(redacted.Contains(decodedPassword), redacted);
            Assert.IsFalse(redacted.Contains(url), redacted);
        }

        // ---- helpers ----

        private static AppConfig Config(string url, int timeoutSeconds = 8)
        {
            var values = new Dictionary<string, string> { ["DB_CONNECT_TIMEOUT_SECONDS"] = timeoutSeconds.ToString() };
            if (url != null) values["DATABASE_URL"] = url;
            return AppConfig.FromValues(values);
        }

        private Database TempSqlite()
        {
            string path = Path.Combine(Path.GetTempPath(), "petsitters_select_" + Guid.NewGuid().ToString("N") + ".db");
            _tempFiles.Add(path);
            return new Database(path);
        }
    }
}
