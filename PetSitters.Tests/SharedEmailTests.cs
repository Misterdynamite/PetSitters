using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Tests
{
    /// <summary>
    /// Component tests for REQ-GR-06 against a real isolated SQLite database:
    /// one email may hold one Owner AND one Sitter account, but never two
    /// accounts of the same role; login picks the right account (asking for a
    /// role only when the password matches both).
    /// </summary>
    [TestClass]
    public class SharedEmailTests : DatabaseTestBase
    {
        private const string Email = "alex@test.com";
        private const string Password = "secret1";

        private AuthResult Register(UserRole role, string email = Email, string password = Password)
        {
            return Services.Auth.Register(email, password, role, "Alex " + role, "021", "Wellington");
        }

        // ---- registration ----

        /// <summary>Equivalence partition over the two orders an email can gain its second role.</summary>
        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [DataRow(UserRole.Owner, UserRole.Sitter)]
        [DataRow(UserRole.Sitter, UserRole.Owner)]
        // REQ-GR-06
        public void Register_SameEmailForTheOtherRole_Succeeds(UserRole first, UserRole second)
        {
            Register(first);

            AuthResult result = Register(second);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(2, Services.Users.FindAllByEmail(Email).Count, "Both accounts should exist.");
        }

        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
        [DataRow(UserRole.Owner, "An owner account with that email already exists")]
        [DataRow(UserRole.Sitter, "A sitter account with that email already exists")]
        // REQ-GR-06: rejected with a warning, and no second account is created.
        public void Register_SameEmailSameRole_IsRejectedWithRoleSpecificWarning(UserRole role, string expectedMessage)
        {
            Register(role);

            AuthResult result = Register(role, email: "ALEX@test.com");   // casing must not matter

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.ErrorMessage, expectedMessage);
            Assert.AreEqual(1, Services.Users.FindAllByEmail(Email).Count, "No second account may be created.");
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
        // REQ-GR-06: with both roles taken, a third registration of either role fails.
        public void Register_WhenBothRolesExist_RejectsEitherRole()
        {
            Register(UserRole.Owner);
            Register(UserRole.Sitter);

            Assert.IsFalse(Register(UserRole.Owner).Success);
            Assert.IsFalse(Register(UserRole.Sitter).Success);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Negative")]
        [TestCategory("ErrorHandling")]
        // The (Email, Role) constraint is enforced by the database itself, not
        // only by AuthService, so a bypassing insert cannot create a duplicate.
        public void Insert_DuplicateEmailAndRole_IsRejectedByTheDatabase()
        {
            Register(UserRole.Owner);
            var duplicate = new User
            {
                Email = "Alex@Test.com", PasswordHash = "h", PasswordSalt = "s",
                Role = UserRole.Owner, FullName = "Dup", CreatedUtc = System.DateTime.UtcNow
            };

            Assert.ThrowsException<System.Data.SQLite.SQLiteException>(() => Services.Users.Insert(duplicate));
        }

        // ---- login ----

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        public void Login_SharedEmailSamePassword_AsksWhichRole()
        {
            Register(UserRole.Owner);
            Register(UserRole.Sitter);

            AuthResult result = Services.Auth.Login(Email, Password);

            Assert.IsFalse(result.Success, "Login must not guess which account to open.");
            Assert.IsTrue(result.RequiresRoleChoice);
        }

        [DataTestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        [DataRow(UserRole.Owner)]
        [DataRow(UserRole.Sitter)]
        public void Login_SharedEmailWithChosenRole_OpensThatAccount(UserRole role)
        {
            Register(UserRole.Owner);
            Register(UserRole.Sitter);

            AuthResult result = Services.Auth.Login(Email, Password, role);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(role, result.User.Role);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Positive")]
        // Different passwords identify the account on their own, so no prompt.
        public void Login_SharedEmailDifferentPasswords_OpensTheMatchingAccount()
        {
            Register(UserRole.Owner, password: "ownerpass");
            Register(UserRole.Sitter, password: "sitterpass");

            AuthResult result = Services.Auth.Login(Email, "sitterpass");

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsFalse(result.RequiresRoleChoice);
            Assert.AreEqual(UserRole.Sitter, result.User.Role);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Negative")]
        // Security: no user enumeration. Wrong password, or a role with no
        // account, gives the same generic message as an unknown email.
        public void Login_SharedEmailFailures_UseTheGenericMessage()
        {
            Register(UserRole.Owner);
            string unknownEmail = Services.Auth.Login("nobody@test.com", Password).ErrorMessage;

            AuthResult wrongPassword = Services.Auth.Login(Email, "wrongpass");
            AuthResult missingRole = Services.Auth.Login(Email, Password, UserRole.Sitter);

            Assert.IsFalse(wrongPassword.Success);
            Assert.IsFalse(missingRole.Success);
            Assert.AreEqual(unknownEmail, wrongPassword.ErrorMessage);
            Assert.AreEqual(unknownEmail, missingRole.ErrorMessage);
            Assert.IsFalse(wrongPassword.RequiresRoleChoice || missingRole.RequiresRoleChoice);
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Security")]
        [TestCategory("Positive")]
        // Each role's account keeps its own data: the owner's pets are not the sitter's.
        public void SharedEmail_AccountsAreSeparate()
        {
            int ownerId = Register(UserRole.Owner).User.Id;
            int sitterId = Register(UserRole.Sitter).User.Id;
            Services.Pets.Insert(new Pet { OwnerUserId = ownerId, Name = "Rex", Age = 2 });

            Assert.AreNotEqual(ownerId, sitterId);
            Assert.AreEqual(1, Services.Pets.GetByOwner(ownerId).Count);
            Assert.AreEqual(0, Services.Pets.GetByOwner(sitterId).Count);
            Assert.AreEqual(1, Services.Users.GetByRole(UserRole.Sitter).Count(u => u.Email == Email));
        }
    }
}
