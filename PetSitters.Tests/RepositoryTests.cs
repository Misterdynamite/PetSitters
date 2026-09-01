using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Models;

namespace PetSitters.Tests
{
    /// <summary>
    /// Component tests for <see cref="PetSitters.Data.UserRepository"/> against a
    /// real isolated database. Supports FR-O2 / FR-S1 (register personal details)
    /// and FR-O1 (browse sitters -> GetByRole).
    /// </summary>
    [TestClass]
    public class UserRepositoryTests : DatabaseTestBase
    {
        private User NewUser(string email, UserRole role, string name)
        {
            return Services.Users.Insert(new User
            {
                Email = email,
                PasswordHash = "hash",
                PasswordSalt = "salt",
                Role = role,
                FullName = name,
                Phone = "021",
                Location = "Wellington",
                CreatedUtc = System.DateTime.UtcNow
            });
        }

        [TestMethod]
        public void Insert_AssignsId_AndCanBeFoundByEmailAndId()
        {
            User inserted = NewUser("a@test.com", UserRole.Owner, "Alice");

            Assert.IsTrue(inserted.Id > 0);
            Assert.IsNotNull(Services.Users.FindByEmail("a@test.com"));
            Assert.AreEqual("Alice", Services.Users.FindById(inserted.Id).FullName);
        }

        [TestMethod]
        public void EmailExists_IsCaseInsensitive()
        {
            NewUser("a@test.com", UserRole.Owner, "Alice");

            Assert.IsTrue(Services.Users.EmailExists("A@TEST.COM"));
            Assert.IsFalse(Services.Users.EmailExists("other@test.com"));
        }

        [TestMethod]
        // FR-01
        public void GetByRole_ReturnsOnlyThatRole_OrderedByName()
        {
            NewUser("sitter-b@test.com", UserRole.Sitter, "Bob");
            NewUser("sitter-a@test.com", UserRole.Sitter, "Ann");
            NewUser("owner@test.com", UserRole.Owner, "Olivia");

            List<User> sitters = Services.Users.GetByRole(UserRole.Sitter);

            Assert.AreEqual(2, sitters.Count);
            Assert.AreEqual("Ann", sitters[0].FullName, "Results should be ordered by name.");
            Assert.AreEqual("Bob", sitters[1].FullName);
        }

        [TestMethod]
        // FR-02
        public void UpdateDetails_PersistsEditedFields()
        {
            User user = NewUser("a@test.com", UserRole.Owner, "Alice");

            user.FullName = "Alice Smith";
            user.Location = "Auckland";
            Services.Users.UpdateDetails(user);

            User reloaded = Services.Users.FindById(user.Id);
            Assert.AreEqual("Alice Smith", reloaded.FullName);
            Assert.AreEqual("Auckland", reloaded.Location);
        }
    }

    /// <summary>
    /// Component tests for <see cref="PetSitters.Data.PetRepository"/>.
    /// Supports FR-4 (owner registers pet details).
    /// </summary>
    [TestClass]
    public class PetRepositoryTests : DatabaseTestBase
    {
        private int _ownerId;

        private void GivenAnOwner()
        {
            var result = Services.Auth.Register("owner@test.com", "secret1", UserRole.Owner,
                "Olivia", "021", "Wellington");
            _ownerId = result.User.Id;
        }

        private Pet AddPet(string name)
        {
            return Services.Pets.Insert(new Pet
            {
                OwnerUserId = _ownerId,
                Name = name,
                Species = "Dog",
                Age = 3
            });
        }

        [TestMethod]
        public void Insert_ThenGetByOwner_ReturnsThePets()
        {
            GivenAnOwner();
            AddPet("Rex");
            AddPet("Bella");

            List<Pet> pets = Services.Pets.GetByOwner(_ownerId);

            Assert.AreEqual(2, pets.Count);
            // Ordered by name: Bella before Rex.
            Assert.AreEqual("Bella", pets[0].Name);
        }

        [TestMethod]
        public void Insert_PersistsYearsAndOptionalMonths()
        {
            GivenAnOwner();
            Services.Pets.Insert(new Pet
            {
                OwnerUserId = _ownerId,
                Name = "Milo",
                Species = "Cat",
                Age = 2,
                AgeMonths = 7
            });

            Pet stored = Services.Pets.GetByOwner(_ownerId)[0];
            Assert.AreEqual(2, stored.Age);
            Assert.AreEqual(7, stored.AgeMonths);
            Assert.AreEqual("2 years 7 months", stored.AgeDisplay);
        }

        [TestMethod]
        public void Insert_DefaultsMonthsToZero_WhenNotSupplied()
        {
            GivenAnOwner();
            AddPet("Rex"); // AddPet supplies years only

            Pet stored = Services.Pets.GetByOwner(_ownerId)[0];
            Assert.AreEqual(0, stored.AgeMonths);
        }

        [TestMethod]
        public void Delete_RemovesOnlyTheSelectedPet()
        {
            GivenAnOwner();
            Pet rex = AddPet("Rex");
            AddPet("Bella");

            Services.Pets.Delete(rex.Id);

            List<Pet> pets = Services.Pets.GetByOwner(_ownerId);
            Assert.AreEqual(1, pets.Count);
            Assert.AreEqual("Bella", pets[0].Name);
        }
    }

    /// <summary>
    /// Component tests for <see cref="PetSitters.Data.SitterProfileRepository"/>.
    /// Supports FR-S2 (sitter registers availability, experience, rate, etc.).
    /// </summary>
    [TestClass]
    public class SitterProfileRepositoryTests : DatabaseTestBase
    {
        private int _sitterId;

        private void GivenASitter()
        {
            var result = Services.Auth.Register("sitter@test.com", "secret1", UserRole.Sitter,
                "Sam", "021", "Wellington");
            _sitterId = result.User.Id;
        }

        [TestMethod]
        public void Upsert_InsertsProfile_WhenNoneExists()
        {
            GivenASitter();

            Services.SitterProfiles.Upsert(new SitterProfile
            {
                UserId = _sitterId,
                Availability = "Weekends",
                ExperienceYears = 3,
                Preferences = "Great with cats",
                Qualifications = "Pet First Aid",
                DailyRate = 45m,
                Bio = "Friendly sitter"
            });

            SitterProfile profile = Services.SitterProfiles.GetByUserId(_sitterId);
            Assert.IsNotNull(profile);
            Assert.AreEqual(45m, profile.DailyRate);
            Assert.AreEqual(3, profile.ExperienceYears);
        }

        [TestMethod]
        public void Upsert_UpdatesInPlace_WhenProfileAlreadyExists()
        {
            GivenASitter();
            Services.SitterProfiles.Upsert(new SitterProfile { UserId = _sitterId, DailyRate = 45m, ExperienceYears = 3 });

            // Second upsert for the same sitter should update, not create a duplicate.
            Services.SitterProfiles.Upsert(new SitterProfile { UserId = _sitterId, DailyRate = 60m, ExperienceYears = 5 });

            SitterProfile profile = Services.SitterProfiles.GetByUserId(_sitterId);
            Assert.AreEqual(60m, profile.DailyRate);
            Assert.AreEqual(5, profile.ExperienceYears);
        }

        [TestMethod]
        public void GetByUserId_ReturnsNull_WhenSitterHasNoProfileYet()
        {
            GivenASitter();

            Assert.IsNull(Services.SitterProfiles.GetByUserId(_sitterId));
        }
    }
}
