namespace PetSitters.Models
{
    /// <summary>
    /// The sitter-specific details a sitter registers (FR-8): availability,
    /// experience, preferences, qualifications and daily rate. One row per
    /// sitter <see cref="User"/> (linked via <see cref="UserId"/>).
    /// </summary>
    public class SitterProfile
    {
        public int Id { get; set; }

        /// <summary>FK to the owning sitter <see cref="User.Id"/>.</summary>
        public int UserId { get; set; }

        /// <summary>Free-text availability, e.g. "Weekends and weekday evenings".</summary>
        public string Availability { get; set; }

        public int ExperienceYears { get; set; }

        /// <summary>e.g. "Works great with cats, comfortable with large dogs".</summary>
        public string Preferences { get; set; }

        /// <summary>e.g. "Certificate in Animal Care, Pet First Aid".</summary>
        public string Qualifications { get; set; }

        /// <summary>Rate charged per day, in dollars.</summary>
        public decimal DailyRate { get; set; }

        /// <summary>Optional short introduction shown to owners.</summary>
        public string Bio { get; set; }
    }
}
