using System;

namespace PetSitters.Models
{
    /// <summary>
    /// A registered account. Holds the login credentials plus the shared
    /// personal details captured for both owners and sitters
    /// (FR-3 / FR-7: "register personal details including location").
    /// Role-specific data lives in <see cref="SitterProfile"/>, <see cref="Pet"/>, etc.
    /// </summary>
    public class User
    {
        public int Id { get; set; }

        public string Email { get; set; }

        /// <summary>Base64 PBKDF2 hash of the password. Never store the plain password.</summary>
        public string PasswordHash { get; set; }

        /// <summary>Base64 random salt used when hashing the password.</summary>
        public string PasswordSalt { get; set; }

        public UserRole Role { get; set; }

        public string FullName { get; set; }

        public string Phone { get; set; }

        /// <summary>Suburb / city / postcode. Used by owners to browse nearby sitters.</summary>
        public string Location { get; set; }

        public DateTime CreatedUtc { get; set; }

        /// <summary>Local path to the profile image copied into the app user images folder.</summary>
        public string ProfileImagePath { get; set; }

        /// <summary>
        /// A shallow copy. The views edit a copy, save it, and only then update
        /// the signed-in user, so a save that fails (e.g. the cloud connection
        /// drops) can't leave unsaved values in the session, where the next
        /// successful save would quietly write them.
        /// </summary>
        public User Clone()
        {
            return (User)MemberwiseClone();
        }

        /// <summary>Copies the editable personal details from <paramref name="source"/> onto this user.</summary>
        public void CopyDetailsFrom(User source)
        {
            FullName = source.FullName;
            Phone = source.Phone;
            Location = source.Location;
            ProfileImagePath = source.ProfileImagePath;
        }
    }
}
