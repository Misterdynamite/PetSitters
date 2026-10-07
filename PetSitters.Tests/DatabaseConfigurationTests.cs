using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySqlConnector;
using PetSitters.Data;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Unit tests for REQ-GR-09 (proposed: shared cloud database with local
    /// fallback at launch), covering how the database configuration is read: the .env file format
    /// (<see cref="EnvFile"/>), which source wins (<see cref="AppConfig"/>), and
    /// turning DATABASE_URL into a MySQL connection string (<see cref="MySqlUrl"/>).
    ///
    /// SECURITY is a first-class concern here: DATABASE_URL carries a password,
    /// so several tests assert that no error message ever contains it.
    /// The URLs below use made-up credentials, never the real ones.
    /// </summary>
    [TestClass]
    public class DatabaseConfigurationTests
    {
        private const string Secret = "S3cr3t-Pa55";
        private const string GoodUrl = "mysql://appuser:" + Secret + "@db.example.com:13306/sitters?ssl-mode=REQUIRED";

        // ---- .env parsing ----

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Smoke")]
        public void EnvFile_ParsesKeyValuePairs_IgnoringCommentsAndBlanks()
        {
            var values = EnvFile.Parse(new[]
            {
                "# a comment",
                "",
                "DATABASE_URL=" + GoodUrl,
                "   PETSITTERS_DB = sqlite   ",
            });

            Assert.AreEqual(GoodUrl, values["DATABASE_URL"]);
            Assert.AreEqual("sqlite", values["PETSITTERS_DB"]);
            Assert.AreEqual(2, values.Count);
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        [DataRow("KEY=\"quoted value\"", "quoted value", DisplayName = "Double quotes are stripped")]
        [DataRow("KEY='single'", "single", DisplayName = "Single quotes are stripped")]
        [DataRow("KEY=a=b=c", "a=b=c", DisplayName = "Only the first = splits (values may contain =)")]
        [DataRow("export KEY=value", "value", DisplayName = "Leading 'export' is allowed")]
        [DataRow("KEY=", "", DisplayName = "Empty value")]
        [DataRow("KEY=\"", "\"", DisplayName = "A lone quote is kept, not stripped")]
        public void EnvFile_ValueFormats(string line, string expected)
        {
            Assert.AreEqual(expected, EnvFile.Parse(new[] { line })["KEY"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("InvalidInput")]
        [TestCategory("ErrorHandling")]
        // A broken line must not stop the app from starting; it is just ignored.
        public void EnvFile_MalformedLines_AreIgnored()
        {
            var values = EnvFile.Parse(new[] { "no equals sign", "=no key", "GOOD=1" });

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual("1", values["GOOD"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("ErrorHandling")]
        public void EnvFile_MissingFile_MeansNoSettings()
        {
            var values = EnvFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".env"));

            Assert.AreEqual(0, values.Count);
        }

        // ---- AppConfig: precedence and modes ----

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        // Environment variables override the .env, so CI and the UI tests can
        // switch databases without editing a file.
        public void AppConfig_EnvironmentVariable_OverridesEnvFile()
        {
            string path = WriteTempEnv("DATABASE_URL=" + GoodUrl, "PETSITTERS_DB=auto");
            try
            {
                var environment = new Hashtable { ["PETSITTERS_DB"] = "sqlite" };

                AppConfig config = AppConfig.Load(path, environment);

                Assert.AreEqual(GoodUrl, config.DatabaseUrl, "Keys only in the file are still read.");
                Assert.IsTrue(config.ForceLocalDatabase, "The environment variable must win over the file.");
            }
            finally { File.Delete(path); }
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("InvalidInput")]
        [DataRow("sqlite", true)]
        [DataRow("SQLite", true, DisplayName = "Case-insensitive")]
        [DataRow("local", true, DisplayName = "Alias")]
        [DataRow("auto", false)]
        [DataRow("", false, DisplayName = "Unset means auto")]
        [DataRow("mysqll", false, DisplayName = "A typo falls back to normal behaviour")]
        public void AppConfig_DatabaseMode(string mode, bool forcesLocal)
        {
            AppConfig config = AppConfig.FromValues(new Dictionary<string, string> { ["PETSITTERS_DB"] = mode });

            Assert.AreEqual(forcesLocal, config.ForceLocalDatabase);
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow("0", MySqlUrl.DefaultConnectTimeoutSeconds, DisplayName = "0 is out of range -> default")]
        [DataRow("1", 1, DisplayName = "Lower bound")]
        [DataRow("60", 60, DisplayName = "Upper bound")]
        [DataRow("61", MySqlUrl.DefaultConnectTimeoutSeconds, DisplayName = "Above range -> default")]
        [DataRow("abc", MySqlUrl.DefaultConnectTimeoutSeconds, DisplayName = "Not a number -> default")]
        public void AppConfig_ConnectTimeout_IsBounded(string text, int expected)
        {
            AppConfig config = AppConfig.FromValues(new Dictionary<string, string> { ["DB_CONNECT_TIMEOUT_SECONDS"] = text });

            Assert.AreEqual(expected, config.ConnectTimeoutSeconds);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        public void AppConfig_NoUrl_MeansNoCloudDatabase()
        {
            AppConfig config = AppConfig.FromValues(new Dictionary<string, string> { ["DATABASE_URL"] = "   " });

            Assert.IsNull(config.DatabaseUrl);
        }

        // ---- MySqlUrl ----

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Smoke")]
        public void MySqlUrl_ParsesEveryPart()
        {
            var cs = new MySqlConnectionStringBuilder(MySqlUrl.ToConnectionString(GoodUrl, 5));

            Assert.AreEqual("db.example.com", cs.Server);
            Assert.AreEqual(13306u, cs.Port);
            Assert.AreEqual("appuser", cs.UserID);
            Assert.AreEqual(Secret, cs.Password);
            Assert.AreEqual("sitters", cs.Database);
            Assert.AreEqual(MySqlSslMode.Required, cs.SslMode);
            Assert.AreEqual(5u, cs.ConnectionTimeout);
            Assert.AreEqual((uint)MySqlUrl.MaximumPoolSize, cs.MaximumPoolSize, "Pool must stay small: the server's connection limit is shared.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Boundary")]
        public void MySqlUrl_NoPort_UsesMySqlDefault_AndNoSslMode_RequiresTls()
        {
            var cs = new MySqlConnectionStringBuilder(MySqlUrl.ToConnectionString("mysql://u:p@host/db"));

            Assert.AreEqual((uint)MySqlUrl.DefaultPort, cs.Port);
            Assert.AreEqual(MySqlSslMode.Required, cs.SslMode, "Without ssl-mode we still insist on encryption.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        // Passwords often contain characters that must be %-encoded in a URL.
        public void MySqlUrl_PercentEncodedCredentials_AreDecoded()
        {
            var cs = new MySqlConnectionStringBuilder(MySqlUrl.ToConnectionString("mysql://us%40er:p%3Ass%2Fw%23rd@host/db"));

            Assert.AreEqual("us@er", cs.UserID);
            Assert.AreEqual("p:ss/w#rd", cs.Password, "An encoded ':' must stay inside the password.");
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [DataRow("DISABLED", MySqlSslMode.None)]
        [DataRow("PREFERRED", MySqlSslMode.Preferred)]
        [DataRow("REQUIRED", MySqlSslMode.Required)]
        [DataRow("verify_ca", MySqlSslMode.VerifyCA, DisplayName = "Case-insensitive")]
        [DataRow("VERIFY-IDENTITY", MySqlSslMode.VerifyFull, DisplayName = "Dash or underscore")]
        public void MySqlUrl_SslModes(string mode, MySqlSslMode expected)
        {
            var cs = new MySqlConnectionStringBuilder(MySqlUrl.ToConnectionString("mysql://u:p@host/db?ssl-mode=" + mode));

            Assert.AreEqual(expected, cs.SslMode);
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Security")]
        [TestCategory("InvalidInput")]
        [TestCategory("ErrorHandling")]
        [DataRow("", DisplayName = "Empty")]
        [DataRow("postgres://appuser:" + Secret + "@host/db", DisplayName = "Wrong scheme")]
        [DataRow("mysql://appuser:" + Secret + "@host", DisplayName = "No database")]
        [DataRow("mysql://appuser:" + Secret + "@host/a/b", DisplayName = "Two path segments")]
        [DataRow("mysql://appuser@host/db", DisplayName = "No password")]
        [DataRow("mysql://appuser:" + Secret + "@host/db?ssl-mode=SOMETIMES", DisplayName = "Unknown ssl-mode")]
        [DataRow("not a url " + Secret, DisplayName = "Not a URL at all")]
        // A bad URL must fail with a FormatException whose message never contains the password.
        public void MySqlUrl_InvalidUrl_FailsWithoutLeakingThePassword(string url)
        {
            FormatException error = Assert.ThrowsException<FormatException>(() => MySqlUrl.ToConnectionString(url));

            StringAssert.DoesNotMatch(error.Message, new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(Secret)));
            StringAssert.Contains(error.Message, "DATABASE_URL");
        }

        // ---- helpers ----

        private static string WriteTempEnv(params string[] lines)
        {
            string path = Path.Combine(Path.GetTempPath(), "petsitters_env_" + Guid.NewGuid().ToString("N") + ".env");
            File.WriteAllLines(path, lines);
            return path;
        }
    }
}
