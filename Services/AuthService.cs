using System;
using PetSitters.Data;
using PetSitters.Models;

namespace PetSitters.Services
{
    /// <summary>
    /// Account creation (FR-1) and login (FR-2). Validates input, enforces
    /// one account per email per role (REQ-GR-06), and hashes passwords. Returns an <see cref="AuthResult"/>
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
            // Every field on the registration form is required.
            email = (email ?? string.Empty).Trim();
            fullName = (fullName ?? string.Empty).Trim();
            phone = (phone ?? string.Empty).Trim();
            location = (location ?? string.Empty).Trim();

            if (!ValidationHelper.IsValidEmail(email))
                return AuthResult.Fail("Please enter a valid email address.");

            if (!ValidationHelper.IsValidPassword(password))
                return AuthResult.Fail($"Password must be at least {ValidationHelper.MinPasswordLength} characters.");

            if (!ValidationHelper.IsNonEmpty(fullName))
                return AuthResult.Fail("Please enter your full name.");

            if (!ValidationHelper.IsNonEmpty(phone))
                return AuthResult.Fail("Please enter your phone number.");

            if (!ValidationHelper.IsNonEmpty(location))
                return AuthResult.Fail("Please enter your location (suburb / city).");

            // REQ-GR-06: one account per email PER ROLE. An owner may also
            // register as a sitter with the same email (and vice versa), but not
            // twice as the same role. Case-insensitive, matching the DB index.
            if (_users.EmailExists(email, role))
                return AuthResult.Fail(DuplicateAccountMessage(role));

            PasswordHasher.CreateHash(password, out string hash, out string salt);

            var user = new User
            {
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = role,
                FullName = fullName,
                Phone = phone,
                Location = location,
                CreatedUtc = DateTime.UtcNow
            };

            try
            {
                _users.Insert(user);
            }
            catch (System.Data.Common.DbException ex) when (Database.IsUniqueViolation(ex))
            {
                // Two people registered the same email + role at the same moment
                // (possible with the shared database): both passed EmailExists, and
                // the database's UNIQUE (Email, Role) key refused the second. Same
                // friendly message as the check above, instead of an error dialog.
                return AuthResult.Fail(DuplicateAccountMessage(role));
            }
            return AuthResult.Ok(user);
        }

        private static string DuplicateAccountMessage(UserRole role)
        {
            return $"{(role == UserRole.Owner ? "An owner" : "A sitter")} account with that email already exists. " +
                   "Log in instead, or use a different email.";
        }

        /// <summary>
        /// Logs in by email and password. Since REQ-GR-06 an email can have an
        /// Owner and a Sitter account: only accounts whose password matches are
        /// considered, so differing passwords pick the account by themselves. If
        /// both match, the result asks for a role (<see cref="AuthResult.RequiresRoleChoice"/>)
        /// and the caller retries with <paramref name="role"/> set.
        /// </summary>
        public AuthResult Login(string email, string password, UserRole? role = null)
        {
            email = (email ?? string.Empty).Trim();

            if (!ValidationHelper.IsNonEmpty(email) || string.IsNullOrEmpty(password))
                return AuthResult.Fail("Please enter your email and password.");

            var matches = new System.Collections.Generic.List<User>();
            foreach (User candidate in _users.FindAllByEmail(email))
            {
                if (role.HasValue && candidate.Role != role.Value)
                    continue;
                if (PasswordHasher.Verify(password, candidate.PasswordHash, candidate.PasswordSalt))
                    matches.Add(candidate);
            }

            // Same message whether the email is unknown, the password is wrong or
            // there is no account of the chosen role, so we don't reveal which
            // emails (or email + role pairs) are registered.
            if (matches.Count == 0)
                return AuthResult.Fail("Incorrect email or password.");

            // Only reachable with the correct password, so this reveals nothing
            // to someone who doesn't already own both accounts.
            if (matches.Count > 1)
                return AuthResult.ChooseRole(
                    "This email has both an owner and a sitter account. Choose which one to log in to.");

            return AuthResult.Ok(matches[0]);
        }
    }
}
