namespace PetSitters.Models
{
    /// <summary>
    /// Distinguishes the two kinds of account the system supports.
    /// Persisted as an integer in the database.
    /// </summary>
    public enum UserRole
    {
        Owner = 0,
        Sitter = 1
    }

    /// <summary>
    /// Lifecycle of a booking request made by an owner to a sitter.
    /// </summary>
    public enum BookingStatus
    {
        Pending = 0,
        Accepted = 1,
        Declined = 2,
        Cancelled = 3
    }
}
