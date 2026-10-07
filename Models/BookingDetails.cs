namespace PetSitters.Models
{
    /// <summary>
    /// A booking together with the owner, sitter and pet it refers to, loaded
    /// in ONE query (a JOIN) instead of one lookup per row. On the cloud
    /// database every query costs a ~195 ms round trip, so the dashboards'
    /// old per-row lookups made screens take many seconds; see
    /// BookingRepository.GetDetailsForOwner / GetDetailsForSitter.
    ///
    /// The joined <see cref="User"/>s carry display details only: their
    /// PasswordHash and PasswordSalt are deliberately not loaded (null).
    /// </summary>
    public class BookingDetails
    {
        public Booking Booking { get; set; }

        public User Owner { get; set; }

        public User Sitter { get; set; }

        /// <summary>The specific pet, or null for an "All my pets" booking (or a deleted pet).</summary>
        public Pet Pet { get; set; }
    }

    /// <summary>
    /// A sitter and their sitting profile (null if they haven't filled one in
    /// yet), loaded together for the owner's Find Sitters list in one query.
    /// </summary>
    public class SitterListing
    {
        public User Sitter { get; set; }

        public SitterProfile Profile { get; set; }
    }
}
