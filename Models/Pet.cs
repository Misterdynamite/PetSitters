namespace PetSitters.Models
{
    /// <summary>
    /// A pet belonging to an owner (FR-4: "register their pet's details").
    /// An owner may register many pets.
    /// </summary>
    public class Pet
    {
        public int Id { get; set; }

        /// <summary>FK to the owning <see cref="User.Id"/>.</summary>
        public int OwnerUserId { get; set; }

        public string Name { get; set; }

        /// <summary>e.g. Dog, Cat, Rabbit.</summary>
        public string Species { get; set; }

        public string Breed { get; set; }

        /// <summary>Whole years of the pet's age. Required.</summary>
        public int Age { get; set; }

        /// <summary>
        /// Additional months on top of <see cref="Age"/>, 0-11. Optional -
        /// defaults to 0 when the owner only knows the pet's age in years.
        /// </summary>
        public int AgeMonths { get; set; }

        /// <summary>Care notes, e.g. "Needs medication twice a day".</summary>
        public string Notes { get; set; }

        /// <summary>
        /// Human-readable age combining years and months, e.g. "2 years 3 months",
        /// "1 year", "5 months". Used in the pet list and the sitter's job details.
        /// </summary>
        public string AgeDisplay
        {
            get { return FormatAge(Age, AgeMonths); }
        }

        /// <summary>
        /// Formats a years/months pair for display. Kept static and public so the
        /// rule can be unit tested directly and reused by the views.
        /// </summary>
        public static string FormatAge(int years, int months)
        {
            string yearPart = years + (years == 1 ? " year" : " years");
            string monthPart = months + (months == 1 ? " month" : " months");

            if (years > 0 && months > 0) return yearPart + " " + monthPart;
            if (years > 0) return yearPart;
            if (months > 0) return monthPart;
            return "Under 1 month";
        }
    }
}
