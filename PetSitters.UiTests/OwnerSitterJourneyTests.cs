using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PetSitters.UiTests
{
    /// <summary>
    /// One end-to-end journey through the whole PetSitters app, driven through the
    /// real UI with FlaUI:
    ///
    ///   1. Register a Sitter account and fill in personal details (FR-1, FR-7).
    ///   2. Fill in the sitting profile - availability, experience, rate (FR-8).
    ///   3. Register an Owner account and fill in personal details (FR-1, FR-3).
    ///   4. Add a pet (FR-4).
    ///   5. Browse sitters, and book the sitter for that pet (FR-5, FR-6).
    ///   6. Log in as the sitter and accept the request (sitter side of FR-6).
    ///   7. Log back in as the owner and confirm the booking now shows Accepted.
    ///
    /// The database is wiped before the app launches (see <see cref="Setup"/>), so
    /// the run always starts from an empty system and the two accounts created
    /// here are the only data in play.
    ///
    /// This is a single ordered test on purpose: it is one continuous story, and
    /// each step depends on the state the previous step left behind.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class OwnerSitterJourneyTests
    {
        // ---- test data -------------------------------------------------------
        private const string SitterEmail = "sam.sitter@example.com";
        private const string SitterPassword = "sitter123";
        private const string SitterName = "Sam Sitter";
        private const string SitterPhone = "021 555 0001";
        private const string SitterLocation = "Wellington";
        private const string SitterRate = "40";

        private const string OwnerEmail = "olivia.owner@example.com";
        private const string OwnerPassword = "owner123";
        private const string OwnerName = "Olivia Owner";
        private const string OwnerPhone = "021 555 0002";
        private const string OwnerLocation = "Wellington";

        private const string PetName = "Buddy";
        private const string BookingMessage = "Please look after Buddy while I'm away next weekend.";

        private PetSittersDriver _app;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void Setup()
        {
            // Fresh start: dump the database, then launch the app so it rebuilds
            // an empty schema on startup.
            AppLocator.WipeDatabase();
            _app = new PetSittersDriver(AppLocator.FindExecutable());
        }

        [TestCleanup]
        public void Teardown()
        {
            _app?.Dispose();
        }

        [TestMethod]
        public void OwnerBooksSitter_AndSitterAcceptsTheRequest()
        {
            RegisterSitter();
            SaveSitterDetails();
            FillSittingProfile();
            LogOut();

            RegisterOwner();
            SaveOwnerDetails();
            AddPet();
            BookTheSitter();
            ConfirmBookingIsPending();
            LogOut();

            SitterAcceptsTheRequest();
            LogOut();

            LogInAsOwner();
            ConfirmBookingIsAccepted();
        }

        // ---- steps -----------------------------------------------------------

        private void RegisterSitter()
        {
            Step("Register a new Sitter account");

            _app.ClickButton("Create an account");   // from the login screen
            _app.SelectRadio("SitterRadio");
            _app.EnterText("NameBox", SitterName);
            _app.EnterText("EmailBox", SitterEmail);
            _app.EnterPassword("PasswordBox", SitterPassword);
            _app.EnterText("PhoneBox", SitterPhone);
            _app.EnterText("LocationBox", SitterLocation);
            _app.ClickButton("Create account");

            // Registration signs the user straight in and lands on the sitter
            // dashboard, which has a "My Sitting Profile" tab that owners never see.
            Assert.IsTrue(_app.HasText("My Sitting Profile"),
                "Expected to land on the sitter dashboard after registering as a sitter.");
        }

        private void SaveSitterDetails()
        {
            Step("Fill in and save the sitter's personal details");

            _app.SelectTab("My Details");
            _app.EnterText("NameBox", SitterName);
            _app.EnterText("PhoneBox", SitterPhone);
            _app.EnterText("LocationBox", SitterLocation);
            _app.ClickButton("Save details");

            Assert.AreEqual("Saved.", _app.ReadText("DetailsStatus"),
                "Saving the sitter's details should confirm with 'Saved.'.");
        }

        private void FillSittingProfile()
        {
            Step("Fill in the sitting profile (availability, experience, rate)");

            _app.SelectTab("My Sitting Profile");
            _app.EnterText("BioBox", "Friendly, reliable sitter who treats every pet like my own.");
            _app.EnterText("AvailabilityBox", "Weekends and weekday evenings");
            _app.EnterText("ExperienceBox", "5");
            _app.EnterText("PreferencesBox", "Great with dogs and cats");
            _app.EnterText("QualificationsBox", "Pet first-aid certified");
            _app.EnterText("RateBox", SitterRate);
            _app.ClickButton("Save profile");

            StringAssert.Contains(_app.ReadText("ProfileStatus"), "Profile saved",
                "Saving the sitting profile should confirm it was saved.");
        }

        private void RegisterOwner()
        {
            Step("Register a new Owner account");

            _app.ClickButton("Create an account");   // from the login screen
            _app.SelectRadio("OwnerRadio");
            _app.EnterText("NameBox", OwnerName);
            _app.EnterText("EmailBox", OwnerEmail);
            _app.EnterPassword("PasswordBox", OwnerPassword);
            _app.EnterText("PhoneBox", OwnerPhone);
            _app.EnterText("LocationBox", OwnerLocation);
            _app.ClickButton("Create account");

            // The owner dashboard has a "Find Sitters" tab; the sitter one does not.
            Assert.IsTrue(_app.HasText("Find Sitters"),
                "Expected to land on the owner dashboard after registering as an owner.");
        }

        private void SaveOwnerDetails()
        {
            Step("Fill in and save the owner's personal details");

            _app.SelectTab("My Details");
            _app.EnterText("NameBox", OwnerName);
            _app.EnterText("PhoneBox", OwnerPhone);
            _app.EnterText("LocationBox", OwnerLocation);
            _app.ClickButton("Save details");

            Assert.AreEqual("Saved.", _app.ReadText("DetailsStatus"),
                "Saving the owner's details should confirm with 'Saved.'.");
        }

        private void AddPet()
        {
            Step("Add a pet");

            _app.SelectTab("My Pets");
            _app.EnterText("PetNameBox", PetName);
            _app.EnterText("PetSpeciesBox", "Dog");
            _app.EnterText("PetBreedBox", "Labrador");
            _app.EnterText("PetAgeBox", "3");
            _app.EnterText("PetNotesBox", "Needs two walks a day; friendly with other dogs.");
            _app.ClickButton("Add pet");

            Assert.IsTrue(_app.HasText(PetName),
                "The new pet '" + PetName + "' should appear in the owner's pet list.");
        }

        private void BookTheSitter()
        {
            Step("Browse sitters and send a booking request for the pet");

            _app.SelectTab("Find Sitters");
            _app.SelectFirstListItem("SittersList");

            // Selecting the sitter reveals their profile and the booking form,
            // pre-filling the dates (today -> tomorrow) and the pet drop-down.
            Assert.AreEqual(SitterName, _app.ReadText("DetailName"),
                "The selected sitter's profile should show their name.");

            _app.SelectComboItem("BookingPetCombo", PetName);
            _app.EnterText("BookingMessageBox", BookingMessage);
            _app.ClickButton("Send booking request");

            StringAssert.Contains(_app.ReadText("BookingStatus"), "Request sent",
                "Sending a booking request should confirm it was sent.");
        }

        private void ConfirmBookingIsPending()
        {
            Step("Confirm the owner's booking shows as Pending");

            _app.SelectTab("My Bookings");
            Assert.IsTrue(_app.HasText("Pending"),
                "The owner's new booking should be listed with status Pending.");
        }

        private void SitterAcceptsTheRequest()
        {
            Step("Log in as the sitter and accept the request");

            _app.EnterText("EmailBox", SitterEmail);
            _app.EnterPassword("PasswordBox", SitterPassword);
            _app.ClickButton("Log in");

            _app.SelectTab("Booking Requests");
            _app.SelectFirstListItem("RequestsList");

            StringAssert.Contains(_app.ReadText("RequestMessage"), "Buddy",
                "Selecting the request should show the owner's message.");

            // Open the "View details" popup and confirm it surfaces the richer
            // pet information (name + breed), then close it.
            _app.ClickButton("View details");
            Assert.IsTrue(_app.DialogHasText("Booking request details", PetName),
                "The details popup should show the pet's name.");
            Assert.IsTrue(_app.DialogHasText("Booking request details", "Labrador"),
                "The details popup should show the pet's breed.");
            _app.ClickDialogButton("Booking request details", "Close");

            _app.ClickButton("Accept");

            // Note: the app sets a "Request accepted." status label but then
            // immediately reloads the list, which blanks that label on the same
            // UI thread - so it never stays on screen. We instead verify the
            // thing the app *does* leave visible: the request row's Status cell
            // flipping from Pending to Accepted.
            Assert.IsTrue(_app.HasText("Accepted"),
                "After accepting, the request row's status should show Accepted.");
        }

        private void LogInAsOwner()
        {
            Step("Log back in as the owner");

            _app.EnterText("EmailBox", OwnerEmail);
            _app.EnterPassword("PasswordBox", OwnerPassword);
            _app.ClickButton("Log in");
        }

        private void ConfirmBookingIsAccepted()
        {
            Step("Confirm the owner now sees the booking as Accepted");

            _app.SelectTab("My Bookings");
            Assert.IsTrue(_app.HasText("Accepted"),
                "After the sitter accepts, the owner's booking should show status Accepted.");
        }

        // ---- helpers ---------------------------------------------------------

        private void LogOut()
        {
            _app.ClickButtonById("LogoutButton");
        }

        private void Step(string description)
        {
            TestContext.WriteLine("STEP: " + description);
        }
    }
}
