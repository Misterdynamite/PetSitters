using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;

namespace PetSitters.Tests
{
    /// <summary>
    /// Unit tests for the REQ-GR-08 overlap rule, <see cref="Booking.RangesOverlap"/>
    /// (pure logic, no database).
    ///
    /// Boundary-value analysis around the half-open [start, end) range: the
    /// interesting cases are the day before, the day of, and the day after each
    /// edge of an accepted booking running 10th -> 13th (3 nights).
    /// </summary>
    [TestClass]
    public class BookingOverlapTests
    {
        private static readonly DateTime AcceptedStart = new DateTime(2026, 10, 10);
        private static readonly DateTime AcceptedEnd = new DateTime(2026, 10, 13);

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Negative")]
        [TestCategory("Boundary")]
        // REQ-GR-08 / FR-07
        [DataRow(10, 13, true,  DisplayName = "Identical range")]
        [DataRow(11, 12, true,  DisplayName = "Fully inside")]
        [DataRow(8, 15,  true,  DisplayName = "Fully surrounds")]
        [DataRow(8, 11,  true,  DisplayName = "Overlaps the start")]
        [DataRow(12, 15, true,  DisplayName = "Overlaps the end")]
        [DataRow(9, 11,  true,  DisplayName = "Ends one night inside")]
        [DataRow(12, 14, true,  DisplayName = "Starts on the last night")]
        [DataRow(7, 10,  false, DisplayName = "Back-to-back before (ends on start day)")]
        [DataRow(13, 16, false, DisplayName = "Back-to-back after (starts on hand-back day)")]
        [DataRow(1, 5,   false, DisplayName = "Entirely before")]
        [DataRow(20, 25, false, DisplayName = "Entirely after")]
        public void RangesOverlap_AgainstAcceptedBooking(int startDay, int endDay, bool expected)
        {
            DateTime start = new DateTime(2026, 10, startDay);
            DateTime end = new DateTime(2026, 10, endDay);

            Assert.AreEqual(expected, Booking.RangesOverlap(AcceptedStart, AcceptedEnd, start, end));
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        public void RangesOverlap_IsSymmetric()
        {
            DateTime otherStart = new DateTime(2026, 10, 12);
            DateTime otherEnd = new DateTime(2026, 10, 15);

            Assert.AreEqual(
                Booking.RangesOverlap(AcceptedStart, AcceptedEnd, otherStart, otherEnd),
                Booking.RangesOverlap(otherStart, otherEnd, AcceptedStart, AcceptedEnd),
                "Whether two bookings overlap must not depend on which one is checked first.");
        }

        /// <summary>
        /// REQ-PO-08 pet matching. 0 stands for "All my pets" (a null PetId),
        /// since DataRow cannot carry a nullable int.
        /// </summary>
        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Negative")]
        [DataRow(1, 1, true,  DisplayName = "Same pet")]
        [DataRow(1, 2, false, DisplayName = "Different pets")]
        [DataRow(0, 1, true,  DisplayName = "All my pets vs a specific pet")]
        [DataRow(1, 0, true,  DisplayName = "A specific pet vs all my pets")]
        [DataRow(0, 0, true,  DisplayName = "All my pets vs all my pets")]
        public void SharesPetWith_TreatsAllMyPetsAsEveryPet(int petA, int petB, bool expected)
        {
            var a = new Booking { PetId = petA == 0 ? (int?)null : petA };
            var b = new Booking { PetId = petB == 0 ? (int?)null : petB };

            Assert.AreEqual(expected, a.SharesPetWith(b));
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Negative")]
        [DataRow(BookingStatus.Pending, true)]
        [DataRow(BookingStatus.Accepted, true)]
        [DataRow(BookingStatus.Declined, false)]
        [DataRow(BookingStatus.Cancelled, false)]
        public void IsActive_OnlyForPendingAndAccepted(BookingStatus status, bool expected)
        {
            Assert.AreEqual(expected, new Booking { Status = status }.IsActive);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Boundary")]
        public void RangesOverlap_IgnoresTimeOfDay()
        {
            // The form only captures dates; a stray time component (e.g. from
            // DateTime.Now) must not turn back-to-back bookings into a clash.
            DateTime afternoonStart = AcceptedEnd.AddHours(15);

            Assert.IsFalse(Booking.RangesOverlap(AcceptedStart, AcceptedEnd, afternoonStart, afternoonStart.AddDays(2)));
        }
    }
}
