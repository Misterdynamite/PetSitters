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

        public int Age { get; set; }

        /// <summary>Care notes, e.g. "Needs medication twice a day".</summary>
        public string Notes { get; set; }
    }
}
