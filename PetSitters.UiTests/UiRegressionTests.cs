using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PetSitters.UiTests
{
    /// <summary>
    /// End-to-end UI <b>regression</b> suite for Sitters4Us.
    ///
    /// Purpose: lock down the cross-screen workflows that the fast logic tests in
    /// PetSitters.Tests cannot see. Those tests prove the services and
    /// repositories behave; these prove the *application* is still wired together 
    /// - navigation, view swapping, role routing, dialogs and tab state - by
    /// driving the real PetSitters.exe through Windows UI Automation.
    ///
    /// Requirement coverage (see the traceability matrix in docs/UnitTests.md):
    ///   FR-A1 account creation - both roles      FR-O3 owner registers a pet
    ///   FR-A2 login                              FR-O4 owner requests a booking
    ///   FR-O1 owner browses sitters              FR-S1 sitter personal details
    ///   FR-O2 owner personal details             FR-S2 sitter sitting profile
    ///   FR-S4 sitter accepts a request           FR-S5 sitter chats once accepted
    ///   FR-S3 sitter views full job details  <-- UI-only; this is its ONLY coverage
    ///
    /// Not covered: FR-O5 (owner-side chat) is not implemented in the app yet, so
    /// the journey only exercises chat from the sitter side.
    ///
    /// Regression guards - specific breakages this suite exists to catch, each of
    /// which has bitten this project before:
    ///   G1. Accepting a request must not be verified via the RequestStatus label:
    ///       the app sets it and then immediately reloads the list, which blanks
    ///       it on the same UI thread. Assert on durable state instead.
    ///   G2. A modal ShowDialog window is a UI Automation child of its owner
    ///       window, not a desktop-level window (see PetSittersDriver).
    ///   G3. Switching accounts requires an explicit log out first; the login
    ///       fields do not exist while a dashboard is showing.
    ///   G4. Accepting a request removes it from the pending Booking Requests
    ///       list and auto-opens the booking-scoped Chat tab.
    ///
    /// Each test starts from a wiped database and its own freshly launched app,
    /// so they are independent and order does not matter.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class UiRegressionTests
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
        private const string PetBreed = "Labrador";
        private const string BookingMessage = "Please look after Buddy while I'm away next weekend.";
        private const string ChatGreeting = "Thanks Olivia, Buddy is booked in!";

        private PetSittersDriver _app;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void Setup()
        {
            // CLEAN SLATE - the foundation of this suite. Every test dumps the
            // live database and lets the app rebuild an empty schema on startup,
            // so no test can be influenced by data left behind by a previous run
            // (duplicate emails, stale bookings) or by manual use of the app.
            // Because this runs per-test, the tests are independent and their
            // execution order does not matter.
            Step("Dump the database so the run starts from a clean, empty system");
            AppLocator.WipeDatabase();

            _app = new PetSittersDriver(AppLocator.FindExecutable());
        }

        [TestCleanup]
        public void Teardown()
        {
            // On failure, keep a screenshot of what was actually on screen. UI
            // automation drives the real desktop, so a run can also be derailed by
            // someone using the mouse or keyboard at the same time - the image
            // makes that immediately obvious instead of looking like a real defect.
            if (_app != null && TestContext.CurrentTestOutcome != UnitTestOutcome.Passed)
            {
                try
                {
                    string file = Path.Combine(
                        TestContext.TestRunDirectory ?? AppContext.BaseDirectory,
                        TestContext.TestName + "-failure.png");

                    _app.CaptureScreenshot(file);
                    TestContext.WriteLine("Failure screenshot: " + file);
                }
                catch (Exception ex)
                {
                    TestContext.WriteLine("Could not capture a failure screenshot: " + ex.Message);
                }
            }

            _app?.Dispose();
        }

        /// <summary>
        /// The full marketplace workflow across both roles: a sitter advertises,
        /// an owner registers a pet and books them, and the sitter reviews the job
        /// details, accepts, and chats. This is the primary regression check.
        /// </summary>
        [TestMethod]
        [TestCategory("Regression")]
        [TestCategory("EndToEnd")]
        [TestProperty("Requirements", "FR-A1, FR-A2, FR-O1, FR-O2, FR-O3, FR-O4, FR-S1, FR-S2, FR-S3, FR-S4, FR-S5")]
        public void BookingJourney_OwnerBooksSitterAndSitterAccepts_CompletesWithChatOpen()
        {
            RegisterSitter();
            SaveSitterDetails();
            FillSittingProfile();
            LogOut();

            RegisterOwner();
            SaveOwnerDetails();
            AddPet();
            BookTheSitter();
            ConfirmOwnerSeesPendingBooking();
            LogOut();

            LogInAsSitter();
            ReviewJobDetails();
            AcceptTheRequest();
            ConfirmChatOpenedAndSendMessage();
            ConfirmRequestLeftThePendingList();
            ConfirmSitterSeesAcceptedChat();
            LogOut();

            LogInAsOwner();
            ConfirmOwnerSeesAcceptedBooking();
        }

        /// <summary>
        /// Guards the login failure path and the "no user enumeration" quality
        /// attribute: a bad sign-in must show the generic message and leave the
        /// user on the login screen rather than routing into a dashboard.
        /// </summary>
        [TestMethod]
        [TestCategory("Regression")]
        [TestProperty("Requirements", "FR-A2")]
        public void Login_WithUnknownCredentials_ShowsGenericErrorAndStaysOnLogin()
        {
            Step("Attempt to log in with credentials that do not exist");

            _app.EnterText("EmailBox", "nobody@example.com");
            _app.EnterPassword("PasswordBox", "wrongpassword");
            _app.ClickButton("Log in");

            Assert.AreEqual("Incorrect email or password.", _app.ReadText("ErrorText"),
                "A failed login should show the generic message that does not reveal whether the email exists.");
            Assert.IsTrue(_app.Exists("EmailBox"),
                "A failed login should leave the user on the login screen.");
        }

        // ---- journey steps ---------------------------------------------------

        private void RegisterSitter()
        {
            Step("Register a new Sitter account (FR-A1)");

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
                "Expected to land on the sitter dashboard after registering as a sitter. " +
                "Validation error on the register form: " + (_app.TryReadText("ErrorText") ?? "(none)"));
        }

        private void SaveSitterDetails()
        {
            Step("Fill in and save the sitter's personal details (FR-S1)");

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
            Step("Fill in the sitting profile - availability, experience, rate (FR-S2)");

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
            Step("Register a new Owner account (FR-A1)");

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
                "Expected to land on the owner dashboard after registering as an owner. " +
                "Validation error on the register form: " + (_app.TryReadText("ErrorText") ?? "(none)"));
        }

        private void SaveOwnerDetails()
        {
            Step("Fill in and save the owner's personal details (FR-O2)");

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
            Step("Add a pet (FR-O3)");

            _app.SelectTab("My Pets");
            _app.EnterText("PetNameBox", PetName);
            _app.EnterText("PetSpeciesBox", "Dog");
            _app.EnterText("PetBreedBox", PetBreed);
            _app.EnterText("PetAgeBox", "3");
            _app.EnterText("PetNotesBox", "Needs two walks a day; friendly with other dogs.");
            _app.ClickButton("Add pet");

            Assert.IsTrue(_app.HasText(PetName),
                "The new pet '" + PetName + "' should appear in the owner's pet list.");
        }

        private void BookTheSitter()
        {
            Step("Browse sitters and send a booking request for the pet (FR-O1, FR-O4)");

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

        private void ConfirmOwnerSeesPendingBooking()
        {
            Step("Confirm the owner's booking shows as Pending");

            _app.SelectTab("My Bookings");
            Assert.IsTrue(_app.HasText("Pending"),
                "The owner's new booking should be listed with status Pending.");
        }

        private void ReviewJobDetails()
        {
            Step("Sitter reviews the full job details before deciding (FR-S3)");

            _app.SelectTab("Booking Requests");
            _app.SelectFirstListItem("RequestsList");

            StringAssert.Contains(_app.ReadText("RequestMessage"), PetName,
                "Selecting the request should show the owner's message.");

            // FR-S3 lives only in the UI, so this popup has no unit-test coverage:
            // it must surface the pet and owner information the sitter needs.
            _app.ClickButton("View details");
            Assert.IsTrue(_app.DialogHasText(JobDetailsTitle, PetName),
                "The details popup should show the pet's name.");
            Assert.IsTrue(_app.DialogHasText(JobDetailsTitle, PetBreed),
                "The details popup should show the pet's breed.");
            Assert.IsTrue(_app.DialogHasText(JobDetailsTitle, OwnerLocation),
                "The details popup should show the owner's location.");
            _app.ClickDialogButton(JobDetailsTitle, "Close");
        }

        private void AcceptTheRequest()
        {
            Step("Sitter accepts the request (FR-S4)");

            _app.ClickButton("Accept");

            // G1: deliberately NOT asserted via the RequestStatus label - the app
            // blanks it immediately when it reloads the list. The durable effects
            // are checked by the steps that follow.
        }

        private void ConfirmChatOpenedAndSendMessage()
        {
            Step("Confirm accepting opened the booking chat, and send a message (FR-S5)");

            // G4: accepting auto-opens the booking-scoped Chat tab.
            Assert.IsTrue(_app.Exists("ChatInput"),
                "Accepting a request should open the booking's Chat tab.");

            _app.EnterText("ChatInput", ChatGreeting);
            _app.ClickButton("Send");

            Assert.IsTrue(_app.HasText(SitterName + ": " + ChatGreeting),
                "The sent chat message should appear in the conversation, attributed to the sitter.");
        }

        private void ConfirmRequestLeftThePendingList()
        {
            Step("Confirm the accepted request is no longer in the pending list");

            // G4: the Booking Requests tab shows pending requests only.
            _app.SelectTab("Booking Requests");
            Assert.AreEqual(0, _app.CountListItems("RequestsList"),
                "Once accepted, the request should no longer appear in the pending requests list.");
        }

        private void ConfirmSitterSeesAcceptedChat()
        {
            Step("Confirm the booking appears under the sitter's active chats");

            _app.SelectTab("My Chats");
            Assert.IsTrue(_app.HasText(OwnerName),
                "The accepted booking should be listed under the sitter's active chats.");
            Assert.IsTrue(_app.HasText("Accepted"),
                "The active chat row should show the booking status as Accepted.");
        }

        private void ConfirmOwnerSeesAcceptedBooking()
        {
            Step("Confirm the owner now sees the booking as Accepted (FR-O4)");

            _app.SelectTab("My Bookings");
            Assert.IsTrue(_app.HasText("Accepted"),
                "After the sitter accepts, the owner's booking should show status Accepted.");
        }

        // ---- helpers ---------------------------------------------------------

        /// <summary>Title of the job details popup, matched on when driving it.</summary>
        private const string JobDetailsTitle = "Booking request details";

        private void LogInAsSitter()
        {
            Step("Log in as the sitter (FR-A2)");
            LogIn(SitterEmail, SitterPassword);
        }

        private void LogInAsOwner()
        {
            Step("Log back in as the owner (FR-A2)");
            LogIn(OwnerEmail, OwnerPassword);
        }

        private void LogIn(string email, string password)
        {
            _app.EnterText("EmailBox", email);
            _app.EnterPassword("PasswordBox", password);
            _app.ClickButton("Log in");
        }

        /// <summary>
        /// G3: always log out before signing in as somebody else - the login
        /// fields only exist once the dashboard has been swapped out.
        /// </summary>
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
