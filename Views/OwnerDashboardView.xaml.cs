using System;
using System.IO;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Views
{
    /// <summary>
    /// Owner home. Tabs cover FR-3 (personal details), FR-4 (pets),
    /// FR-5 (browse sitters) and FR-6 (request bookings).
    /// </summary>
    public partial class OwnerDashboardView : UserControl
    {
        private readonly AppServices _services;
        private readonly MainWindow _shell;

        public OwnerDashboardView(AppServices services, MainWindow shell)
        {
            InitializeComponent();
            _services = services;
            _shell = shell;

            // Each loader is ONE query (see the JOIN methods in the repositories):
            // at ~195 ms per cloud round trip, per-row lookups made this dashboard
            // take ~11 s to open. LoadBookings also fills the Chats tab.
            LoadDetails();
            LoadPets();
            LoadSitters();
            LoadBookings();

            // Refresh the lists whenever a booking's status changes (this dashboard's
            // own cancel, for example). The handler is detached when the dashboard is
            // removed (logout): without that, every past login's dashboard stayed
            // subscribed and kept reloading in the background, for the wrong user.
            _onBookingStatusChanged = (id, status) => Dispatcher.Invoke(LoadBookings);
            _services.Bookings.BookingStatusChanged += _onBookingStatusChanged;
            Unloaded += (s, e) => _services.Bookings.BookingStatusChanged -= _onBookingStatusChanged;
        }

        private readonly Action<int, BookingStatus> _onBookingStatusChanged;

        // The owner's pets, as last loaded: reused for the booking form's pet
        // list so picking a sitter doesn't query the database again.
        private List<Pet> _myPets = new List<Pet>();

        private User Me => _services.CurrentUser;
        // temporary storage for the image chosen when adding a new pet
        private string _newPetImagePath;

        // ---- FR-3: personal details ------------------------------------------------
        private void LoadDetails()
        {
            EmailText.Text = Me.Email;
            NameBox.Text = Me.FullName;
            // show the user's role (Owner/Sitter) by looking up the named TextBlock
            try
            {
                var roleTb = FindName("RoleText") as TextBlock;
                if (roleTb != null) roleTb.Text = Me.Role.ToString();
            }
            catch { }
            PhoneBox.Text = Me.Phone;
            LocationBox.Text = Me.Location;
            // load profile image if set
            if (!string.IsNullOrEmpty(Me.ProfileImagePath) && File.Exists(Me.ProfileImagePath))
            {
                try
                {
                    var uri = new Uri(Me.ProfileImagePath);
                    ProfileImageBrush.ImageSource = new System.Windows.Media.Imaging.BitmapImage(uri);
                }
                catch { /* ignore image load errors */ }
            }
        }

        /// <summary>
        /// Select the named tab in the dashboard (called from the main header nav).
        /// </summary>
        public void SelectTab(string header)
        {
            var tabs = FindName("RootTabs") as System.Windows.Controls.TabControl;
            if (tabs == null) return;
            foreach (var item in tabs.Items)
            {
                if (item is System.Windows.Controls.TabItem t && t.Header != null && t.Header.ToString() == header)
                {
                    tabs.SelectedItem = t;
                    return;
                }
            }
        }

        private void SaveDetails_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidationHelper.IsNonEmpty(NameBox.Text))
            {
                DetailsStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
                DetailsStatus.Text = "Full name is required.";
                return;
            }

            Me.FullName = NameBox.Text.Trim();
            Me.Phone = PhoneBox.Text.Trim();
            Me.Location = LocationBox.Text.Trim();
            _services.Users.UpdateDetails(Me);

            DetailsStatus.Foreground = (System.Windows.Media.Brush)FindResource("Brand");
            DetailsStatus.Text = "Saved.";
        }

        // ---- FR-4: pets ------------------------------------------------------------
        private void LoadPets()
        {
            _myPets = _services.Pets.GetByOwner(Me.Id);
            PetsList.ItemsSource = _myPets;
            try { var btn = FindName("DeletePetButton") as Button; if (btn != null) btn.IsEnabled = false; } catch { }
        }

        private void PetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var btn = FindName("DeletePetButton") as Button;
                if (btn != null) btn.IsEnabled = PetsList.SelectedItem is Pet;
            }
            catch { }
        }

        private void AddPet_Click(object sender, RoutedEventArgs e)
        {
            PetStatus.Text = string.Empty;

            if (!ValidationHelper.IsNonEmpty(PetNameBox.Text))
            {
                PetStatus.Text = "Please enter your pet's name.";
                return;
            }
            if (!ValidationHelper.TryParseNonNegativeInt(PetAgeBox.Text, out int age))
            {
                PetStatus.Text = "Age in years must be a whole number (0 or more).";
                return;
            }
            // Months are optional; when supplied they must be 0-11 (12 = another year).
            if (!ValidationHelper.TryParseAgeMonths(PetAgeMonthsBox.Text, out int ageMonths))
            {
                PetStatus.Text = $"Months must be a whole number between 0 and {ValidationHelper.MaxAgeMonths}, or left blank.";
                return;
            }

            _services.Pets.Insert(new Pet
            {
                OwnerUserId = Me.Id,
                Name = PetNameBox.Text.Trim(),
                Species = PetSpeciesBox.Text.Trim(),
                Breed = PetBreedBox.Text.Trim(),
                Age = age,
                AgeMonths = ageMonths,
                Notes = PetNotesBox.Text.Trim(),
                ImagePath = _newPetImagePath
            });

            PetNameBox.Clear();
            PetSpeciesBox.Clear();
            PetBreedBox.Clear();
            PetAgeBox.Text = "0";
            PetAgeMonthsBox.Clear();
            PetNotesBox.Clear();

            LoadPets();
            RefreshBookingPetCombo();
        }

        private void ImportProfileImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (dlg.ShowDialog() == true)
            {
                string dest = CopyImageToUserFolder(dlg.FileName);
                if (dest != null)
                {
                    Me.ProfileImagePath = dest;
                    _services.Users.UpdateDetails(Me); // persist path
                    try { ProfileImageBrush.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri(dest)); } catch { }
                }
            }
        }

        private void ImportPetImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (dlg.ShowDialog() == true)
            {
                string dest = CopyImageToUserFolder(dlg.FileName);
                if (dest != null)
                {
                    _newPetImagePath = dest;
                    try { PetPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(dest)); } catch { }
                }
            }
        }

        private string CopyImageToUserFolder(string sourcePath)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "PetSitters", "UserImages");
                Directory.CreateDirectory(folder);
                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(sourcePath);
                string dest = Path.Combine(folder, fileName);
                File.Copy(sourcePath, dest, true);
                return dest;
            }
            catch
            {
                return null;
            }
        }

        private void DeletePet_Click(object sender, RoutedEventArgs e)
        {
            if (PetsList.SelectedItem is Pet pet)
            {
                var confirm = MessageBox.Show($"Are you sure you want to delete '{pet.Name}'? This cannot be undone.",
                    "Delete pet", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                // Attempt to remove the pet image file from disk if present
                try
                {
                    if (!string.IsNullOrWhiteSpace(pet.ImagePath) && File.Exists(pet.ImagePath))
                    {
                        try { File.Delete(pet.ImagePath); } catch { /* ignore failures */ }
                    }
                }
                catch { }

                _services.Pets.Delete(pet.Id);
                LoadPets();
                RefreshBookingPetCombo();
                PetStatus.Foreground = (System.Windows.Media.Brush)FindResource("Brand");
                PetStatus.Text = "Pet deleted.";
            }
            else
            {
                PetStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
                PetStatus.Text = "Select a pet in the list to delete it.";
            }
        }

        // ---- FR-5: browse sitters --------------------------------------------------
        private void LoadSitters()
        {
            // One query for every sitter and their profile (was 1 + one per sitter).
            SittersList.ItemsSource = _services.Users.GetSittersWithProfiles()
                .Select(listing => new SitterRow(listing.Sitter, listing.Profile))
                .ToList();
        }

        private void SittersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(SittersList.SelectedItem is SitterRow row))
            {
                SitterDetailContent.Visibility = Visibility.Collapsed;
                NoSitterSelected.Visibility = Visibility.Visible;
                return;
            }

            NoSitterSelected.Visibility = Visibility.Collapsed;
            SitterDetailContent.Visibility = Visibility.Visible;

            DetailName.Text = row.Name;
            DetailMeta.Text = row.SubHeading;
            DetailBio.Text = Fallback(row.Bio, "No introduction provided yet.");
            DetailPreferences.Text = Fallback(row.Preferences, "Not specified.");
            DetailQualifications.Text = Fallback(row.Qualifications, "Not specified.");
            DetailAvailability.Text = Fallback(row.Availability, "Not specified.");

            RefreshBookingPetCombo();
            StartDatePicker.SelectedDate = DateTime.Today;
            EndDatePicker.SelectedDate = DateTime.Today.AddDays(1);
            BookingMessageBox.Clear();
            BookingStatus.Text = string.Empty;
        }

        // ---- FR-6: request booking -------------------------------------------------
        private void RefreshBookingPetCombo()
        {
            var pets = new List<Pet> { new Pet { Id = 0, Name = "All my pets" } };
            pets.AddRange(_myPets);   // cached by LoadPets, which runs after every pet add/delete
            BookingPetCombo.ItemsSource = pets;
            BookingPetCombo.SelectedIndex = 0;
        }

        private void RequestBooking_Click(object sender, RoutedEventArgs e)
        {
            BookingStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");

            if (!(SittersList.SelectedItem is SitterRow row))
            {
                BookingStatus.Text = "Please select a sitter first.";
                return;
            }
            if (StartDatePicker.SelectedDate == null || EndDatePicker.SelectedDate == null)
            {
                BookingStatus.Text = "Please choose a start and end date.";
                return;
            }

            DateTime start = StartDatePicker.SelectedDate.Value.Date;
            DateTime end = EndDatePicker.SelectedDate.Value.Date;
            // Date/duration/pet rules (REQ-GR-04) live in BookingService.ValidateRequest,
            // so they are applied, and unit-tested, in one place.

            int? petId = null;
            if (BookingPetCombo.SelectedItem is Pet pet && pet.Id != 0)
                petId = pet.Id;

            var booking = new Booking
            {
                OwnerUserId = Me.Id,
                SitterUserId = row.UserId,
                PetId = petId,
                StartDate = start,
                EndDate = end,
                Message = BookingMessageBox.Text.Trim(),
                Status = BookingStatus_Pending(),
                DailyRateAtBooking = row.DailyRate,
                CreatedUtc = DateTime.UtcNow
            };

            // Goes through BookingService so REQ-PO-08 (no overlapping bookings
            // for the same pet) is enforced; nothing is saved when refused.
            BookingResult result = _services.BookingActions.RequestBooking(booking);
            if (!result.Success)
            {
                BookingStatus.Text = result.ErrorMessage;
                return;
            }

            BookingStatus.Foreground = (System.Windows.Media.Brush)FindResource("Brand");
            BookingStatus.Text = $"Request sent to {row.Name}. Estimated total {Currency(booking.EstimatedTotal)} " +
                                 $"for {booking.Nights} night(s).";
            LoadBookings();
        }

        private static BookingStatus BookingStatus_Pending()
        {
            return Models.BookingStatus.Pending;
        }

        // ---- FR-6: my bookings list ------------------------------------------------
        private void LoadBookings()
        {
            // ONE query for all the owner's bookings with sitter and pet; the
            // Chats tab is the accepted subset of the same rows.
            List<OwnerBookingRow> rows = _services.Bookings.GetDetailsForOwner(Me.Id)
                .Select(d => new OwnerBookingRow(d.Booking, d.Sitter?.FullName ?? "(unknown)", PetName(d)))
                .ToList();
            BookingsList.ItemsSource = rows;
            ShowChats(rows);
        }

        /// <summary>"All my pets" when no pet was named; "(removed pet)" if it was deleted since.</summary>
        private static string PetName(BookingDetails details)
        {
            if (!details.Booking.PetId.HasValue)
                return "All my pets";
            return details.Pet?.Name ?? "(removed pet)";
        }

        // ---- REQ-PO-07: owner cancels a booking ------------------------------------
        private void CancelBooking_Click(object sender, RoutedEventArgs e)
        {
            CancelStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");

            if (!(BookingsList.SelectedItem is OwnerBookingRow row))
            {
                CancelStatus.Text = "Select a booking to cancel.";
                return;
            }

            // Cancelling can't be undone, so confirm first. Owned by this window
            // so it stays on top of the app (and UI tests find it as its child).
            MessageBoxResult answer = MessageBox.Show(Window.GetWindow(this),
                $"Cancel your booking with {row.SitterName} for {row.DateRange}?",
                "Cancel booking", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;

            BookingResult result = _services.BookingActions.CancelBooking(row.BookingId, Me.Id);
            if (!result.Success)
            {
                CancelStatus.Text = result.ErrorMessage;
                return;
            }

            // A cancelled booking has no chat (REQ-PO-05): close it if it is open.
            if (_activeChatBookingId == row.BookingId)
            {
                _activeChatBookingId = null;
                ChatTab.Visibility = Visibility.Collapsed;
            }

            // No explicit reload: CancelBooking changes the status, which raises
            // BookingStatusChanged, and the listener above has already reloaded
            // both lists (reloading again would be a wasted cloud round trip).
            CancelStatus.Foreground = (System.Windows.Media.Brush)FindResource("Brand");
            CancelStatus.Text = $"Booking with {row.SitterName} cancelled.";
        }

        // ---- Chats for the owner ----------------------------------------------
        // Only this account's bookings as OWNER: since REQ-GR-06 the same person's
        // sitter side is a separate account, so there is no "also a sitter" case.
        private void ShowChats(List<OwnerBookingRow> bookingRows)
        {
            ChatsList.ItemsSource = bookingRows.Where(r => r.Status == PetSitters.Models.BookingStatus.Accepted.ToString()).ToList();
            ChatSelectedDetails.Text = "Select a chat to open.";
        }

        /// <summary>
        /// The booking, if this user is its owner or sitter; null otherwise.
        /// One lookup by id (it used to load every booking the user has).
        /// </summary>
        private Booking FindMyBooking(int bookingId)
        {
            Booking booking = _services.Bookings.GetById(bookingId);
            return booking != null && (booking.OwnerUserId == Me.Id || booking.SitterUserId == Me.Id) ? booking : null;
        }

        private void ChatsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChatsList.SelectedItem is OwnerBookingRow row)
            {
                ChatSelectedDetails.Text = $"With: {row.SitterName}\nDates: {row.DateRange}\nStatus: {row.Status}";
            }
        }

        private void OpenChat_Click(object sender, RoutedEventArgs e)
        {
            if (!(ChatsList.SelectedItem is OwnerBookingRow row))
            {
                MessageBox.Show("Select a chat first.");
                return;
            }
            ShowChatForBooking(row.BookingId);
        }

        private int? _activeChatBookingId;

        private void ShowChatForBooking(int bookingId)
        {
            var booking = FindMyBooking(bookingId);
            if (booking == null)
            {
                MessageBox.Show("You are not a participant in this booking.");
                return;
            }

            _activeChatBookingId = bookingId;
            ChatTab.Visibility = Visibility.Visible;
            RefreshChat();
            ChatTab.IsSelected = true;
        }

        private void RefreshChat()
        {
            if (!_activeChatBookingId.HasValue) return;
            // One query: messages with their senders' names (was one lookup per message).
            var messages = _services.Chats.GetForBookingWithSenderNames(_activeChatBookingId.Value);
            ChatMessagesList.Items.Clear();
            foreach (var m in messages)
            {
                var text = new TextBlock { Text = $"{m.SenderName ?? "Unknown"}: {m.MessageText}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,4,0,4) };
                ChatMessagesList.Items.Add(text);
            }
            ChatScroll.ScrollToEnd();
        }

        private void ChatSend_Click(object sender, RoutedEventArgs e)
        {
            if (!_activeChatBookingId.HasValue)
            {
                MessageBox.Show("No chat is open.");
                return;
            }
            var text = ChatInput.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            var booking = FindMyBooking(_activeChatBookingId.Value);
            if (booking == null)
            {
                MessageBox.Show("You are not a participant in this booking.");
                return;
            }
            // Chat is only open while the booking is accepted (REQ-PO-05/PS-04).
            // Re-checked here because a chat can stay open after the booking is
            // cancelled; the chat lists alone only filter what can be opened.
            if (booking.Status != PetSitters.Models.BookingStatus.Accepted)
            {
                MessageBox.Show($"This booking is {booking.Status.ToString().ToLowerInvariant()}, so its chat is closed.");
                return;
            }

            var msg = new ChatMessage
            {
                BookingId = _activeChatBookingId.Value,
                SenderUserId = Me.Id,
                MessageText = text,
                CreatedUtc = DateTime.UtcNow
            };
            _services.Chats.Insert(msg);
            ChatInput.Text = string.Empty;
            RefreshChat();
        }

        private static string Fallback(string value, string ifEmpty)
        {
            return string.IsNullOrWhiteSpace(value) ? ifEmpty : value;
        }

        internal static string Currency(decimal value)
        {
            return "$" + value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Display row for the sitter browse list (FR-5).</summary>
    public class SitterRow
    {
        public int UserId { get; }
        public string Name { get; }
        public string Location { get; }
        public decimal DailyRate { get; }
        public int ExperienceYears { get; }
        public string Preferences { get; }
        public string Qualifications { get; }
        public string Availability { get; }
        public string Bio { get; }

        public SitterRow(User sitter, SitterProfile profile)
        {
            UserId = sitter.Id;
            Name = sitter.FullName;
            Location = sitter.Location;
            DailyRate = profile?.DailyRate ?? 0m;
            ExperienceYears = profile?.ExperienceYears ?? 0;
            Preferences = profile?.Preferences;
            Qualifications = profile?.Qualifications;
            Availability = profile?.Availability;
            Bio = profile?.Bio;
        }

        public string SubHeading
        {
            get
            {
                string loc = string.IsNullOrWhiteSpace(Location) ? "Location N/A" : Location;
                return $"{loc}  ·  {OwnerDashboardView.Currency(DailyRate)}/day  ·  {ExperienceYears} yr(s) exp";
            }
        }
    }

    /// <summary>Display row for the owner's bookings list (FR-6).</summary>
    public class OwnerBookingRow
    {
        public int BookingId { get; }
        public string SitterName { get; }
        public string PetName { get; }
        public string DateRange { get; }
        public int Nights { get; }
        public string Total { get; }
        public string Status { get; }

        public OwnerBookingRow(Booking b, string sitterName, string petName)
        {
            BookingId = b.Id;
            SitterName = sitterName;
            PetName = petName;
            DateRange = b.StartDate.ToString("d MMM yyyy") + " – " + b.EndDate.ToString("d MMM yyyy");
            Nights = b.Nights;
            Total = OwnerDashboardView.Currency(b.EstimatedTotal);
            Status = b.Status.ToString();
        }
    }
}
