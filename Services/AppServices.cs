using System;
using PetSitters.Data;
using PetSitters.Models;

namespace PetSitters.Services
{
    /// <summary>
    /// Simple composition root: builds the database and repositories once and
    /// exposes them (plus the current session user) to the views. Created at
    /// startup in App.xaml.cs, after <see cref="DatabaseSelector"/> has picked
    /// the cloud (MySQL) or local (SQLite) database.
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
        public BookingService BookingActions { get; }

        /// <summary>Which database is in use and why (shown in the window header).</summary>
        public DatabaseSelection Storage { get; }

        /// <summary>The currently logged-in user, or null if nobody is signed in.</summary>
        public User CurrentUser { get; set; }

        /// <summary>Wires everything to <paramref name="database"/> (tests use this with a temp SQLite file).</summary>
        public AppServices(Database database)
            : this(DatabaseSelection.For(database ?? throw new ArgumentNullException(nameof(database))))
        {
        }

        public AppServices(DatabaseSelection storage)
        {
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Database = storage.Database;
            Database.Initialize();   // no-op if the launch check already did it

            Users = new UserRepository(Database);
            SitterProfiles = new SitterProfileRepository(Database);
            Pets = new PetRepository(Database);
            Bookings = new BookingRepository(Database);
            Chats = new ChatRepository(Database);
            Auth = new AuthService(Users);
            BookingActions = new BookingService(Bookings, Pets);
        }

        /// <summary>
        /// The real app's services: settings from the .env beside the exe (and
        /// environment variables), then MySQL first with SQLite fallback. May take
        /// several seconds (it connects over the internet), so call it OFF the UI
        /// thread.
        /// </summary>
        public static AppServices CreateDefault()
        {
            return Create(AppConfig.Load(AppDomain.CurrentDomain.BaseDirectory));
        }

        public static AppServices Create(AppConfig config)
        {
            return new AppServices(DatabaseSelector.Select(config));
        }
    }
}
