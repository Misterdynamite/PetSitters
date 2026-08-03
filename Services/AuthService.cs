using System;
using PetSitters.Data;
using PetSitters.Models;

namespace PetSitters.Services
{
    /// <summary>
    /// Account creation (FR-1) and login (FR-2). Validates input, enforces
    /// unique emails, and hashes passwords. Returns an <see cref="AuthResult"/>
    /// rather than throwing, so the UI can show friendly messages and tests can
    /// assert on the outcome.
    /// </summary>
    public class AuthService
    {
        private readonly UserRepository _users;

        public AuthService(UserRepository users)
        {
            _users = users ?? throw new ArgumentNullException(nameof(users));
        }

        public AuthResult Register(string email, string password, UserRole role,
            string fullName, string phone, string location)
        {
            email = (email ?? string.Empty).Trim();
            fullName = (fullName ?? string.Empty).Trim();

            if (!ValidationHelper.IsValidEmail(email))
                return AuthResult.Fail("Please enter a valid email address.");

            if (!ValidationHelper.IsValidPassword(password))
                return AuthResult.Fail($"Password must be at least {ValidationHelper.MinPasswordLength} characters.");

            if (!ValidationHelper.IsNonEmpty(fullName))
                return AuthResult.Fail("Please enter your full name.");

            if (_users.EmailExists(email))
                return AuthResult.Fail("An account with that email already exists.");

            PasswordHasher.CreateHash(password, out string hash, out string salt);

            var user = new User
            {
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = role,
                FullName = fullName,
                Phone = (phone ?? string.Empty).Trim(),
                Location = (location ?? string.Empty).Trim(),
                CreatedUtc = DateTime.UtcNow
            };

            _users.Insert(user);
            return AuthResult.Ok(user);
        }

        public AuthResult Login(string email, string password)
        {
            email = (email ?? string.Empty).Trim();

            if (!ValidationHelper.IsNonEmpty(email) || string.IsNullOrEmpty(password))
                return AuthResult.Fail("Please enter your email and password.");

            User user = _users.FindByEmail(email);

            // Same message whether the email is unknown or the password is wrong,
            // so we don't reveal which emails are registered.
            if (user == null || !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
                return AuthResult.Fail("Incorrect email or password.");

            return AuthResult.Ok(user);
        }
    }
}
