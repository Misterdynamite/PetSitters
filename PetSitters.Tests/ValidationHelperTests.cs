using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ValidationHelper"/>.
    ///
    /// These apply Lab 5 test-design techniques explicitly:
    ///   * Equivalence partitioning  - one representative value per class of input.
    ///   * Boundary-value analysis   - values either side of the min-length rule.
    ///   * Data-driven tests         - [DataRow] runs the same logic over many inputs.
    /// Supports data-quality QA opportunities named in the proposal: invalid emails,
    /// weak passwords, invalid ages/rates (FR-A1, FR-4, FR-S2).
    /// </summary>
    [TestClass]
    public class ValidationHelperTests
    {
        // TryParseRate reads numbers in the user's culture (the app's audience
        // writes "45.50"). Pin a '.'-decimal culture so these tests don't fail on
        // a PC set to, say, German, where "45.555" means forty-five thousand.
        private System.Globalization.CultureInfo _originalCulture;

        [TestInitialize]
        public void PinCulture()
        {
            _originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-NZ");
        }

        [TestCleanup]
        public void RestoreCulture()
        {
            System.Globalization.CultureInfo.CurrentCulture = _originalCulture;
        }

        // ---- Email: equivalence partitions (valid vs several invalid classes) ----
        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow("user@test.com", true)]      // typical valid
        [DataRow("a@b.co", true)]             // minimal valid
        [DataRow("first.last@sub.domain.nz", true)]
        [DataRow("", false)]                  // empty
        [DataRow("   ", false)]               // whitespace only
        [DataRow("notanemail", false)]        // no @, no domain
        [DataRow("no@domain", false)]         // missing top-level domain (no dot)
        [DataRow("@nolocal.com", false)]      // missing local part
        [DataRow("has space@test.com", false)]// contains a space
        public void IsValidEmail_ClassifiesInputCorrectly(string email, bool expected)
        {
            Assert.AreEqual(expected, ValidationHelper.IsValidEmail(email));
        }

        /// <summary>
        /// Boundary-value analysis on the 254-character email limit (RFC 5321).
        /// It matters for the cloud database: Email is VARCHAR(255) there, and the
        /// server's STRICT mode rejects longer values instead of truncating them.
        /// </summary>
        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow(253, true, DisplayName = "253 characters: just inside")]
        [DataRow(254, true, DisplayName = "254 characters: the maximum")]
        [DataRow(255, false, DisplayName = "255 characters: just over")]
        public void IsValidEmail_EnforcesMaximumLengthBoundary(int length, bool expected)
        {
            const string domain = "@example.com";
            string email = new string('a', length - domain.Length) + domain;

            Assert.AreEqual(length, email.Length);
            Assert.AreEqual(expected, ValidationHelper.IsValidEmail(email));
        }

        // ---- Password: boundary-value analysis around MinPasswordLength (6) ----
        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Security")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow("", false)]         // empty
        [DataRow("12345", false)]    // 5 chars  -> just below the boundary
        [DataRow("123456", true)]    // 6 chars  -> on the boundary (minimum allowed)
        [DataRow("1234567", true)]   // 7 chars  -> just above the boundary
        public void IsValidPassword_EnforcesMinimumLengthBoundary(string password, bool expected)
        {
            Assert.AreEqual(expected, ValidationHelper.IsValidPassword(password));
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("InvalidInput")]
        [TestCategory("ErrorHandling")]
        [DataRow("something", true)]
        [DataRow("  ", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void IsNonEmpty_DetectsBlankValues(string value, bool expected)
        {
            Assert.AreEqual(expected, ValidationHelper.IsNonEmpty(value));
        }

        // ---- Daily rate: numeric, zero-or-greater ----
        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow("0", true)]        // boundary: zero is allowed
        [DataRow("45", true)]
        [DataRow("45.50", true)]
        [DataRow("-0.01", false)]   // just below zero
        [DataRow("-5", false)]
        [DataRow("abc", false)]     // not a number
        [DataRow("", false)]
        public void TryParseRate_AcceptsOnlyNonNegativeNumbers(string text, bool expected)
        {
            bool ok = ValidationHelper.TryParseRate(text, out decimal rate);

            Assert.AreEqual(expected, ok);
            if (expected)
                Assert.IsTrue(rate >= 0m);
        }

        /// <summary>
        /// Boundary-value analysis on the money column's range and precision:
        /// DECIMAL(10,2) on the cloud database. Larger values would be rejected
        /// by the server, and a third decimal place would be rounded there but
        /// kept by SQLite, so the two engines would disagree.
        /// </summary>
        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [TestCategory("Positive")]
        [DataRow("99999999.99", true, DisplayName = "The largest value that fits")]
        [DataRow("100000000", false, DisplayName = "Just too large")]
        [DataRow("45.5", true, DisplayName = "One decimal place")]
        [DataRow("45.55", true, DisplayName = "Two decimal places (cents)")]
        [DataRow("45.555", false, DisplayName = "Three decimal places")]
        public void TryParseRate_FitsTheMoneyColumn(string text, bool expected)
        {
            Assert.AreEqual(expected, ValidationHelper.TryParseRate(text, out decimal _));
        }

        // ---- Pet age (months): optional, whole number, boundary 0-11 ----
        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow("", true, 0)]      // blank -> optional, treated as 0 months
        [DataRow("   ", true, 0)]   // whitespace -> also treated as not supplied
        [DataRow("0", true, 0)]     // lower boundary
        [DataRow("1", true, 1)]     // just inside the lower boundary
        [DataRow("11", true, 11)]   // upper boundary (12 would be another year)
        [DataRow("12", false, 0)]   // just outside the upper boundary
        [DataRow("-1", false, 0)]   // just below the lower boundary
        [DataRow("6.5", false, 0)]  // not a whole number
        [DataRow("six", false, 0)]  // not a number
        public void TryParseAgeMonths_AcceptsBlankOrZeroToEleven(string text, bool expectedOk, int expectedMonths)
        {
            bool ok = ValidationHelper.TryParseAgeMonths(text, out int months);

            Assert.AreEqual(expectedOk, ok);
            if (expectedOk)
                Assert.AreEqual(expectedMonths, months);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("ErrorHandling")]
        public void TryParseAgeMonths_TreatsNullAsNotSupplied()
        {
            bool ok = ValidationHelper.TryParseAgeMonths(null, out int months);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, months);
        }

        // ---- Age / years of experience: whole number, zero-or-greater ----
        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        [TestCategory("InvalidInput")]
        [DataRow("0", true)]        // boundary
        [DataRow("3", true)]
        [DataRow("-1", false)]      // negative
        [DataRow("2.5", false)]     // not a whole number
        [DataRow("ten", false)]     // not a number
        [DataRow("", false)]
        public void TryParseNonNegativeInt_AcceptsOnlyWholeNonNegativeNumbers(string text, bool expected)
        {
            bool ok = ValidationHelper.TryParseNonNegativeInt(text, out int value);

            Assert.AreEqual(expected, ok);
            if (expected)
                Assert.IsTrue(value >= 0);
        }
    }
}
