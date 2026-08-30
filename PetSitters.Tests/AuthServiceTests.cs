using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Component tests for <see cref="AuthService"/> against a real (isolated)
    /// SQLite database. Covers FR-A1 (account creation) and FR-A2 (login), plus
    /// the security quality attribute (hashed passwords, no user enumeration).
    /// </summary>
    [TestClass]
    public class AuthServiceTests : DatabaseTestBase
    {
        private const int LoginBudgetMilliseconds = 1000;
        private const int LoginSamples = 5;
        public TestContext TestContext { get; set; }

        private AuthResult RegisterOwner(string email = "olivia@test.com", string password = "secret1")
        {
            return Services.Auth.Register(email,
                password,
                UserRole.Owner,
                "Olivia Owner",
                "0210000000",
                "Wellington");
        }

        // ---- FR-A1: account creation ----
        // FR-03
        [TestMethod]
        public void Register_WithValidDetails_Succeeds()
        {
            AuthResult result = RegisterOwner();

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNotNull(result.User);
            Assert.IsTrue(result.User.Id > 0, "A database id should be assigned.");
            Assert.AreEqual(UserRole.Owner, result.User.Role);
        }

        [DataTestMethod]
        [DataRow("notanemail")]
        [DataRow("missing@domain")]
        [DataRow("")]
        public void Register_WithInvalidEmail_Fails(string email)
        {
            AuthResult result = Services.Auth.Register(email, "secret1", UserRole.Owner,
                "Olivia Owner", "021", "Wellington");

            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void Register_WithWeakPassword_Fails()
        {
            // 5 characters -> below the 6-character minimum (boundary).
            AuthResult result = Services.Auth.Register("olivia@test.com", "12345", UserRole.Owner,
                "Olivia Owner", "021", "Wellington");

            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void Register_WithEmptyName_Fails()
        {
            AuthResult result = Services.Auth.Register("olivia@test.com", "secret1", UserRole.Owner,
                "   ", "021", "Wellington");

            Assert.IsFalse(result.Success);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        public void Register_WithEmptyPhone_Fails(string phone)
        {
            // All registration fields are required.
            AuthResult result = Services.Auth.Register("olivia@test.com", "secret1", UserRole.Owner,
                "Olivia Owner", phone, "Wellington");

            Assert.IsFalse(result.Success);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        public void Register_WithEmptyLocation_Fails(string location)
        {
            AuthResult result = Services.Auth.Register("olivia@test.com", "secret1", UserRole.Owner,
                "Olivia Owner", "021", location);

            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void Register_WithAllFieldsSupplied_PersistsPhoneAndLocation()
        {
            AuthResult result = RegisterOwner();

            User stored = Services.Users.FindByEmail("olivia@test.com");
            Assert.IsTrue(result.Success);
            Assert.AreEqual("0210000000", stored.Phone);
            Assert.AreEqual("Wellington", stored.Location);
        }

        [TestMethod]
        public void Register_DuplicateEmail_Fails_CaseInsensitive()
        {
            RegisterOwner("olivia@test.com");

            // Same address, different casing -> must still be rejected as a duplicate.
            AuthResult second = Services.Auth.Register("OLIVIA@test.com", "secret1", UserRole.Owner,
                "Someone Else", "021", "Auckland");

            Assert.IsFalse(second.Success);
        }

        [TestMethod]
        public void Register_StoresHashedPassword_NotPlainText()
        {
            // Security: the persisted row must not contain the raw password.
            RegisterOwner("olivia@test.com", "secret1");

            User stored = Services.Users.FindByEmail("olivia@test.com");
            Assert.IsNotNull(stored);
            Assert.AreNotEqual("secret1", stored.PasswordHash);
            Assert.IsFalse(string.IsNullOrEmpty(stored.PasswordSalt));
        }

        // ---- FR-A2: login ----
        // FR-08

        [TestMethod]
        public void Login_WithCorrectCredentials_SucceedsWithinPerformanceBudget()
        {
            RegisterOwner("olivia@test.com", "secret1");
            Services.Auth.Login("olivia@test.com", "secret1");

            AuthResult result = null;
            var timings = new List<double>();

            for (int sample = 0; sample < LoginSamples; sample++)
            {
                var stopwatch = Stopwatch.StartNew();
                result = Services.Auth.Login("olivia@test.com", "secret1");
                stopwatch.Stop();

                timings.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual("olivia@test.com", result.User.Email);

            double slowest = timings.Max();
            double average = timings.Average();

            TestContext.WriteLine(
                "Sign-in over {0} samples - fastest {1:F1} ms, average {2:F1} ms, slowest {3:F1} ms (budget {4} ms).",
                LoginSamples, timings.Min(), average, slowest, LoginBudgetMilliseconds);

            Assert.IsTrue(slowest <= LoginBudgetMilliseconds,
                string.Format(
                    "Sign-in took {0:F1} ms, exceeding the {1} ms budget for REQ-NFR-02 (average {2:F1} ms over {3} samples).",
                    slowest, LoginBudgetMilliseconds, average, LoginSamples));
        }

        [TestMethod]
        public void Login_WithWrongPassword_Fails()
        {
            RegisterOwner("olivia@test.com", "secret1");

            AuthResult result = Services.Auth.Login("olivia@test.com", "wrongpass");

            Assert.IsFalse(result.Success);
        }

        [DataTestMethod]
        [DataRow("", "")]
        [DataRow("olivia@test.com", "")]
        [DataRow("", "secret1")]
        public void Login_WithMissingInput_Fails(string email, string password)
        {
            AuthResult result = Services.Auth.Login(email, password);

            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void Login_DoesNotRevealWhetherEmailIsRegistered()
        {
            // Security (no user enumeration): a wrong password on a known account
            // and a login for an unknown account must return the SAME message.
            RegisterOwner("olivia@test.com", "secret1");

            AuthResult wrongPassword = Services.Auth.Login("olivia@test.com", "wrongpass");
            AuthResult unknownEmail = Services.Auth.Login("nobody@test.com", "secret1");

            Assert.IsFalse(wrongPassword.Success);
            Assert.IsFalse(unknownEmail.Success);
            Assert.AreEqual(wrongPassword.ErrorMessage, unknownEmail.ErrorMessage);
        }
    }
}
