using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;

namespace PetSitters.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="Booking"/> cost calculations (pure logic).
    ///
    /// Boundary-value analysis (Lab 5) around the "minimum one night" rule and the
    /// nights x rate total. Supports FR-O4 (owner requests a booking) where the
    /// owner is shown an estimated cost.
    /// </summary>
    [TestClass]
    public class BookingCalculationTests
    {
        private static Booking BookingSpanning(int daysBetween, decimal dailyRate)
        {
            DateTime start = new DateTime(2026, 1, 10);
            return new Booking
            {
                StartDate = start,
                EndDate = start.AddDays(daysBetween),
                DailyRateAtBooking = dailyRate
            };
        }

        [DataTestMethod]
        [DataRow(0, 1)]    // same start/end date -> clamped up to a minimum of 1 night
        [DataRow(1, 1)]    // one day apart -> 1 night
        [DataRow(3, 3)]    // three days apart -> 3 nights
        [DataRow(7, 7)]
        public void Nights_IsDateSpan_WithMinimumOfOne(int daysBetween, int expectedNights)
        {
            Booking booking = BookingSpanning(daysBetween, 40m);

            Assert.AreEqual(expectedNights, booking.Nights);
        }

        [DataTestMethod]
        [DataRow(1, 45.0, 45.0)]     // 1 night  x $45  = $45
        [DataRow(3, 40.0, 120.0)]    // 3 nights x $40  = $120
        [DataRow(2, 55.5, 111.0)]    // 2 nights x $55.50 = $111
        [DataRow(0, 40.0, 40.0)]     // clamped to 1 night x $40 = $40
        public void EstimatedTotal_IsNightsTimesDailyRate(int daysBetween, double rate, double expectedTotal)
        {
            Booking booking = BookingSpanning(daysBetween, (decimal)rate);

            Assert.AreEqual((decimal)expectedTotal, booking.EstimatedTotal);
        }
    }
}
