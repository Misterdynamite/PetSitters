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
        // ---- Email: equivalence partitions (valid vs several invalid classes) ----
        [DataTestMethod]
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

        // ---- Password: boundary-value analysis around MinPasswordLength (6) ----
        [DataTestMethod]
        [DataRow("", false)]         // empty
        [DataRow("12345", false)]    // 5 chars  -> just below the boundary
        [DataRow("123456", true)]    // 6 chars  -> on the boundary (minimum allowed)
        [DataRow("1234567", true)]   // 7 chars  -> just above the boundary
        public void IsValidPassword_EnforcesMinimumLengthBoundary(string password, bool expected)
        {
            Assert.AreEqual(expected, ValidationHelper.IsValidPassword(password));
        }

        [DataTestMethod]
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

        // ---- Pet age (months): optional, whole number, boundary 0-11 ----
        [DataTestMethod]
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
        public void TryParseAgeMonths_TreatsNullAsNotSupplied()
        {
            bool ok = ValidationHelper.TryParseAgeMonths(null, out int months);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, months);
        }

        // ---- Age / years of experience: whole number, zero-or-greater ----
        [DataTestMethod]
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
