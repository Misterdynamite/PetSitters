using PetSitters.Models;

namespace PetSitters.Services
{
    /// <summary>Outcome of a registration or login attempt.</summary>
    public class AuthResult
    {
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }
        public User User { get; private set; }

        public static AuthResult Ok(User user)
        {
            return new AuthResult { Success = true, User = user };
        }

        public static AuthResult Fail(string message)
        {
            return new AuthResult { Success = false, ErrorMessage = message };
        }
    }
}
