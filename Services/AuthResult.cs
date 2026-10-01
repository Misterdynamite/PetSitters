using PetSitters.Models;

namespace PetSitters.Services
{
    /// <summary>Outcome of a registration or login attempt.</summary>
    public class AuthResult
    {
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }
        public User User { get; private set; }

        /// <summary>
        /// Login only: the email and password matched BOTH an Owner and a Sitter
        /// account (REQ-GR-06), so the user must say which one to open. Not a
        /// success; retry the login with a role.
        /// </summary>
        public bool RequiresRoleChoice { get; private set; }

        public static AuthResult Ok(User user)
        {
            return new AuthResult { Success = true, User = user };
        }

        public static AuthResult Fail(string message)
        {
            return new AuthResult { Success = false, ErrorMessage = message };
        }

        public static AuthResult ChooseRole(string message)
        {
            return new AuthResult { Success = false, RequiresRoleChoice = true, ErrorMessage = message };
        }
    }
}
