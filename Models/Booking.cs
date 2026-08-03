using System;

namespace PetSitters.Models
{
    /// <summary>
    /// A booking request made by an owner to a sitter (FR-6). Carries the
    /// requested date range and a snapshot of the sitter's daily rate so the
    /// estimated cost stays stable even if the sitter later changes their rate.
    /// </summary>
    public class Booking
    {
        public int Id { get; set; }

        public int OwnerUserId { get; set; }

        public int SitterUserId { get; set; }

        /// <summary>Optional specific pet the request is for; null = "all my pets".</summary>
        public int? PetId { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        /// <summary>Message from the owner to the sitter.</summary>
        public string Message { get; set; }

        public BookingStatus Status { get; set; }

        /// <summary>Sitter's daily rate captured at the time of the request.</summary>
        public decimal DailyRateAtBooking { get; set; }

        public DateTime CreatedUtc { get; set; }

        /// <summary>Number of nights spanned by the request (minimum 1).</summary>
        public int Nights
        {
            get
            {
                int nights = (int)(EndDate.Date - StartDate.Date).TotalDays;
                return nights < 1 ? 1 : nights;
            }
        }

        /// <summary>Estimated total cost = nights x daily rate.</summary>
        public decimal EstimatedTotal
        {
            get { return Nights * DailyRateAtBooking; }
        }
    }
}
