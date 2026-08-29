using System.Text.RegularExpressions;

namespace PetSitters.Services
{
    /// <summary>
    /// Small, UI-independent input validators. Kept pure so they can be unit
    /// tested directly against the assignment's acceptance criteria.
    /// </summary>
    public static class ValidationHelper
    {
        // Pragmatic email pattern: something@something.tld
        private static readonly Regex EmailRegex =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        public const int MinPasswordLength = 6;

        public static bool IsValidEmail(string email)
        {
            return !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email.Trim());
        }

        /// <summary>Minimum password rule for the prototype: at least 6 characters.</summary>
        public static bool IsValidPassword(string password)
        {
            return !string.IsNullOrEmpty(password) && password.Length >= MinPasswordLength;
        }

        public static bool IsNonEmpty(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }

        /// <summary>Parses a daily rate; must be a number that is zero or greater.</summary>
        public static bool TryParseRate(string text, out decimal rate)
        {
            return decimal.TryParse(text, out rate) && rate >= 0;
        }

        /// <summary>Parses an age/years value; must be a whole number that is zero or greater.</summary>
        public static bool TryParseNonNegativeInt(string text, out int value)
        {
            return int.TryParse(text, out value) && value >= 0;
        }

        /// <summary>Upper bound for the optional "months" part of a pet's age.</summary>
        public const int MaxAgeMonths = 11;

        /// <summary>
        /// Parses the optional months part of a pet's age. Blank means "not
        /// supplied" and yields 0. Anything supplied must be a whole number in
        /// the range 0-11, because 12 months would be another whole year.
        /// </summary>
        public static bool TryParseAgeMonths(string text, out int months)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                months = 0;
                return true;
            }

            return int.TryParse(text.Trim(), out months) && months >= 0 && months <= MaxAgeMonths;
        }
    }
}
