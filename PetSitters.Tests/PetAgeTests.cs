using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;

namespace PetSitters.Tests
{
    /// <summary>
    /// Unit tests for a pet's age presentation (FR-O3: register pet details).
    ///
    /// A pet's age is recorded as whole years plus an optional 0-11 months.
    /// These tests cover the combination rules and the singular/plural wording
    /// shown to owners (pet list) and sitters (job details popup).
    /// </summary>
    [TestClass]
    public class PetAgeTests
    {
        [DataTestMethod]
        [DataRow(2, 3, "2 years 3 months")]   // both parts supplied
        [DataRow(1, 1, "1 year 1 month")]     // singular wording for both
        [DataRow(2, 0, "2 years")]            // months omitted -> years only
        [DataRow(1, 0, "1 year")]             // singular year
        [DataRow(0, 5, "5 months")]           // under a year -> months only
        [DataRow(0, 1, "1 month")]            // singular month
        [DataRow(0, 11, "11 months")]         // upper month boundary
        [DataRow(0, 0, "Under 1 month")]      // newborn / unknown -> friendly text
        public void FormatAge_CombinesYearsAndMonths(int years, int months, string expected)
        {
            Assert.AreEqual(expected, Pet.FormatAge(years, months));
        }

        [TestMethod]
        public void AgeDisplay_UsesTheStoredYearsAndMonths()
        {
            var pet = new Pet { Name = "Rex", Age = 2, AgeMonths = 6 };

            Assert.AreEqual("2 years 6 months", pet.AgeDisplay);
        }

        [TestMethod]
        public void AgeMonths_DefaultsToZero_WhenNotSupplied()
        {
            // Months are optional: a pet created with only years reads as years.
            var pet = new Pet { Name = "Bella", Age = 4 };

            Assert.AreEqual(0, pet.AgeMonths);
            Assert.AreEqual("4 years", pet.AgeDisplay);
        }
    }
}
