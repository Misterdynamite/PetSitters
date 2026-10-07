using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Data;

namespace PetSitters.Tests.MySql
{
    // MySQL parity: each class below inherits EVERY test of a SQLite-backed test
    // class, swapping only the database (see DatabaseTestBase.CreateDatabase).
    // The same assertions therefore prove both engines behave identically:
    // schema, constraints, case-insensitive email uniqueness, upserts, ordering,
    // the REQ-PO-08/REQ-GR-08 overlap rules, chat persistence and the rest.
    //
    // Category "MySql" (on each class) keeps them OUT of the build gate and CI,
    // because they need a reachable MySQL server and take minutes over the
    // internet. Run them on purpose:
    //   dotnet test PetSitters.Tests -c Debug --filter TestCategory=MySql
    // With no server configured they are reported Inconclusive (skipped), not failed.

    [TestClass, TestCategory("MySql")]
    public class AuthServiceTests_MySql : AuthServiceTests
    {
        protected override Database CreateDatabase() => MySqlTestDatabase.CreateForTest();
    }

    [TestClass, TestCategory("MySql")]
    public class BookingRepositoryTests_MySql : BookingRepositoryTests
    {
        protected override Database CreateDatabase() => MySqlTestDatabase.CreateForTest();
    }

    [TestClass, TestCategory("MySql")]
    public class BookingServiceTests_MySql : BookingServiceTests
    {
        protected override Database CreateDatabase() => MySqlTestDatabase.CreateForTest();
    }

    [TestClass, TestCategory("MySql")]
    public class BookingValidationTests_MySql : BookingValidationTests
    {
        protected override Database CreateDatabase() => MySqlTestDatabase.CreateForTest();
    }

    [TestClass, TestCategory("MySql")]
    public class ChatPersistenceTests_MySql : ChatPersistenceTests
    {
        protected override Database CreateDatabase() => MySqlTestDatabase.CreateForTest();
    }

    [TestClass, TestCategory("MySql")]
    public class UserRepositoryTests_MySql : UserRepositoryTests
    {
        protected override Database CreateDatabase() => MySqlTestDatabase.CreateForTest();
    }

    [TestClass, TestCategory("MySql")]
    public class PetRepositoryTests_MySql : PetRepositoryTests
    {
        protected override Database CreateDatabase() => MySqlTestDatabase.CreateForTest();
    }

    [TestClass, TestCategory("MySql")]
    public class SitterProfileRepositoryTests_MySql : SitterProfileRepositoryTests
    {
        protected override Database CreateDatabase() => MySqlTestDatabase.CreateForTest();
    }

    [TestClass, TestCategory("MySql")]
    public class SharedEmailTests_MySql : SharedEmailTests
    {
        protected override Database CreateDatabase() => MySqlTestDatabase.CreateForTest();
    }
}
