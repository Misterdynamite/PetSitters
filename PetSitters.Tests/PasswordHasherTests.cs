using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PasswordHasher"/> (pure logic, no database).
    ///
    /// Quality attribute under test: SECURITY. Passwords must never be stored or
    /// compared in plain text; the hash must be salted (so equal passwords hash
    /// differently) and tamper-evident. Relates to FR-A1/FR-A2 (account creation
    /// and login) and the "password security" quality issue in the proposal.
    /// </summary>
    [TestClass]
    public class PasswordHasherTests
    {
        [TestMethod]
        public void CreateHash_ThenVerifyWithCorrectPassword_ReturnsTrue()
        {
            // Arrange
            PasswordHasher.CreateHash("secret123", out string hash, out string salt);

            // Act
            bool ok = PasswordHasher.Verify("secret123", hash, salt);

            // Assert
            Assert.IsTrue(ok);
        }

        [TestMethod]
        public void Verify_WithWrongPassword_ReturnsFalse()
        {
            PasswordHasher.CreateHash("secret123", out string hash, out string salt);

            bool ok = PasswordHasher.Verify("wrongpass", hash, salt);

            Assert.IsFalse(ok);
        }

        [TestMethod]
        public void CreateHash_IsSalted_SamePasswordProducesDifferentHashes()
        {
            // Two accounts with the same password must not share a hash,
            // otherwise a leaked database would reveal reused passwords.
            PasswordHasher.CreateHash("samePassword", out string hash1, out string salt1);
            PasswordHasher.CreateHash("samePassword", out string hash2, out string salt2);

            Assert.AreNotEqual(salt1, salt2, "Each hash should use a fresh random salt.");
            Assert.AreNotEqual(hash1, hash2, "Same password must not produce the same hash.");
        }

        [TestMethod]
        public void CreateHash_DoesNotStorePasswordInPlainText()
        {
            PasswordHasher.CreateHash("secret123", out string hash, out string salt);

            StringAssert.DoesNotMatch(hash, new System.Text.RegularExpressions.Regex("secret123"));
            StringAssert.DoesNotMatch(salt, new System.Text.RegularExpressions.Regex("secret123"));
        }

        [TestMethod]
        public void Verify_WithTamperedHash_ReturnsFalse()
        {
            PasswordHasher.CreateHash("secret123", out string hash, out string salt);
            string tampered = "A" + hash.Substring(1); // flip the first character

            bool ok = PasswordHasher.Verify("secret123", tampered, salt);

            Assert.IsFalse(ok);
        }

        [DataTestMethod]
        [DataRow(null, null)]
        [DataRow("", "")]
        public void Verify_WithMissingStoredHashOrSalt_ReturnsFalse(string hash, string salt)
        {
            // Defensive: a user row with no stored credentials must never authenticate.
            bool ok = PasswordHasher.Verify("anything", hash, salt);

            Assert.IsFalse(ok);
        }
    }
}
