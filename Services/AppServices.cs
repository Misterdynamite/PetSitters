using PetSitters.Data;
using PetSitters.Models;

namespace PetSitters.Services
{
    /// <summary>
    /// Simple composition root: builds the database and repositories once and
    /// exposes them (plus the current session user) to the views. Created at
    /// startup in App.xaml.cs.
    /// </summary>
    public class AppServices
    {
        public Database Database { get; }
        public UserRepository Users { get; }
        public SitterProfileRepository SitterProfiles { get; }
        public PetRepository Pets { get; }
        public BookingRepository Bookings { get; }
        public ChatRepository Chats { get; }
        public AuthService Auth { get; }

        /// <summary>The currently logged-in user, or null if nobody is signed in.</summary>
        public User CurrentUser { get; set; }

        public AppServices(Database database)
        {
            Database = database;
            Database.Initialize();

            Users = new UserRepository(database);
            SitterProfiles = new SitterProfileRepository(database);
            Pets = new PetRepository(database);
            Bookings = new BookingRepository(database);
            Chats = new ChatRepository(database);
            Auth = new AuthService(Users);
        }

        /// <summary>Builds the services against the real AppData database.</summary>
        public static AppServices CreateDefault()
        {
            return new AppServices(Database.CreateDefault());
        }
    }
}
