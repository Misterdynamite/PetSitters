using System;
using System.Security.Cryptography;

namespace PetSitters.Services
{
    /// <summary>
    /// Hashes passwords with PBKDF2 (Rfc2898DeriveBytes) and a per-user random salt.
    /// Passwords are never stored or compared in plain text (security quality attribute).
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;      // 128-bit salt
        private const int HashSize = 32;      // 256-bit hash
        private const int Iterations = 100_000;

        /// <summary>Creates a fresh random salt and the resulting hash for a password.</summary>
        public static void CreateHash(string password, out string hashBase64, out string saltBase64)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            byte[] salt = new byte[SaltSize];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(salt);

            byte[] hash = Derive(password, salt);
            hashBase64 = Convert.ToBase64String(hash);
            saltBase64 = Convert.ToBase64String(salt);
        }

        /// <summary>Returns true if the password reproduces the stored hash for the stored salt.</summary>
        public static bool Verify(string password, string hashBase64, string saltBase64)
        {
            if (string.IsNullOrEmpty(hashBase64) || string.IsNullOrEmpty(saltBase64))
                return false;

            byte[] salt = Convert.FromBase64String(saltBase64);
            byte[] expected = Convert.FromBase64String(hashBase64);
            byte[] actual = Derive(password ?? string.Empty, salt);

            return FixedTimeEquals(expected, actual);
        }

        private static byte[] Derive(string password, byte[] salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                return pbkdf2.GetBytes(HashSize);
        }

        /// <summary>Length-constant comparison to avoid timing side-channels.</summary>
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
