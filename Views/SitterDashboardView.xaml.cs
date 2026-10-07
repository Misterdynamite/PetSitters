using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Views
{
    /// <summary>
    /// Sitter home. Tabs cover FR-7 (personal details), FR-8 (sitting profile:
    /// availability, experience, preferences, qualifications, daily rate) and the
    /// sitter side of FR-6 (respond to booking requests).
    /// </summary>
    public partial class SitterDashboardView : UserControl
    {
        private readonly AppServices _services;
        private readonly MainWindow _shell;

        public SitterDashboardView(AppServices services, MainWindow shell)
        {
            InitializeComponent();
            _services = services;
            _shell = shell;

            // Each loader is ONE query (see the JOIN methods in the repositories):
            // at ~195 ms per cloud round trip, per-row lookups made this dashboard
            // slow to open. LoadRequests also fills My Chats.
            LoadDetails();
            LoadProfile();
            LoadRequests();

            // Refresh the lists whenever a booking's status changes (this dashboard's
            // own accept/decline/cancel). Detached when the dashboard is removed
            // (logout): without that, every past login's dashboard stayed subscribed
            // and kept reloading in the background, for the wrong user.
            _onBookingStatusChanged = (id, status) => Dispatcher.Invoke(LoadRequests);
            _services.Bookings.BookingStatusChanged += _onBookingStatusChanged;
            Unloaded += (s, e) => _services.Bookings.BookingStatusChanged -= _onBookingStatusChanged;
        }

        private readonly Action<int, BookingStatus> _onBookingStatusChanged;

        private void ChatsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChatsList.SelectedItem is SitterRequestRow row)
            {
                ChatSelectedDetails.Text = $"With: {row.OwnerName}\nDates: {row.DateRange}\nStatus: {row.Status}";
            }
        }

        private void OpenChat_Click(object sender, RoutedEventArgs e)
        {
            if (!(ChatsList.SelectedItem is SitterRequestRow row))
            {
                MessageBox.Show("Select a chat first.");
                return;
            }
            ShowChatForBooking(row.BookingId);
        }

        private User Me => _services.CurrentUser;

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
            catch { return null; }
        }

        // Current booking id for which chat is open
        private int? _activeChatBookingId;

        // ---- FR-7: personal details ------------------------------------------------
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
            if (!string.IsNullOrEmpty(Me.ProfileImagePath) && File.Exists(Me.ProfileImagePath))
            {
                try { ProfileImageBrushSitter.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri(Me.ProfileImagePath)); } catch { }
            }
        }

        /// <summary>
        /// Allow the main window to instruct this view to switch tabs by header text.
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
                DetailsStatus.Foreground = (Brush)FindResource("Danger");
                DetailsStatus.Text = "Full name is required.";
                return;
            }

            Me.FullName = NameBox.Text.Trim();
            Me.Phone = PhoneBox.Text.Trim();
            Me.Location = LocationBox.Text.Trim();
            _services.Users.UpdateDetails(Me);

            DetailsStatus.Foreground = (Brush)FindResource("Brand");
            DetailsStatus.Text = "Saved.";
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
                    _services.Users.UpdateDetails(Me);
                    try { ProfileImageBrushSitter.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri(dest)); } catch { }
                }
            }
        }

        // ---- FR-8: sitting profile -------------------------------------------------
        private void LoadProfile()
        {
            SitterProfile profile = _services.SitterProfiles.GetByUserId(Me.Id);
            if (profile == null) return;

            BioBox.Text = profile.Bio;
            AvailabilityBox.Text = profile.Availability;
            ExperienceBox.Text = profile.ExperienceYears.ToString();
            PreferencesBox.Text = profile.Preferences;
            QualificationsBox.Text = profile.Qualifications;
            RateBox.Text = profile.DailyRate.ToString("0.##");
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidationHelper.TryParseNonNegativeInt(ExperienceBox.Text, out int years))
            {
                ShowProfileError("Years of experience must be a whole number (0 or more).");
                return;
            }
            if (!ValidationHelper.TryParseRate(RateBox.Text, out decimal rate))
            {
                ShowProfileError("Daily rate must be a number (0 or more).");
                return;
            }

            _services.SitterProfiles.Upsert(new SitterProfile
            {
                UserId = Me.Id,
                Bio = BioBox.Text.Trim(),
                Availability = AvailabilityBox.Text.Trim(),
                ExperienceYears = years,
                Preferences = PreferencesBox.Text.Trim(),
                Qualifications = QualificationsBox.Text.Trim(),
                DailyRate = rate
            });

            ProfileStatus.Foreground = (Brush)FindResource("Brand");
            ProfileStatus.Text = "Profile saved. Owners can now find you under \"Find Sitters\".";
        }

        private void ShowProfileError(string message)
        {
            ProfileStatus.Foreground = (Brush)FindResource("Danger");
            ProfileStatus.Text = message;
        }

        // ---- Sitter side of FR-6: respond to requests ------------------------------
        private void LoadRequests()
        {
            // ONE query for all this sitter's bookings with owner and pet (the
            // details popup needs both). Pending ones are the requests; accepted
            // ones are the chats. A request may name one pet, or "all my pets"
            // (Pet is null).
            List<BookingDetails> bookings = _services.Bookings.GetDetailsForSitter(Me.Id);

            RequestsList.ItemsSource = bookings
                .Where(d => d.Booking.Status == BookingStatus.Pending)
                .Select(d => new SitterRequestRow(d.Booking, d.Owner, d.Pet))
                .ToList();
            RequestMessage.Text = "Select a request to view its message.";
            RequestStatus.Text = string.Empty;
            try { var img = FindName("SelectedPetImage") as Image; if (img != null) img.Source = null; } catch { }

            // Chats: only this account's bookings as SITTER. Since REQ-GR-06 the same
            // person's owner side is a separate account, so there is no "also an owner" case.
            ChatsList.ItemsSource = bookings
                .Where(d => d.Booking.Status == BookingStatus.Accepted)
                .Select(d => new SitterRequestRow(d.Booking, d.Owner, d.Pet))
                .ToList();
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

        private void UpdateSelected(BookingStatus status)
        {
            if (!(RequestsList.SelectedItem is SitterRequestRow row))
            {
                RequestStatus.Foreground = (Brush)FindResource("Danger");
                RequestStatus.Text = "Select a request first.";
                return;
            }

            if (status == BookingStatus.Accepted)
            {
                // Accepting goes through BookingService so the REQ-GR-08 overlap
                // rule is enforced. On rejection, return BEFORE LoadRequests():
                // it blanks RequestStatus, and the request must stay in the list.
                BookingResult result = _services.BookingActions.AcceptRequest(row.BookingId, Me.Id);
                if (!result.Success)
                {
                    RequestStatus.Foreground = (Brush)FindResource("Danger");
                    RequestStatus.Text = result.ErrorMessage;
                    return;
                }
            }
            else
            {
                _services.Bookings.UpdateStatus(row.BookingId, status);
            }

            // No explicit reload: the status change raised BookingStatusChanged and
            // the listener has already reloaded the requests and chats.
            RequestStatus.Foreground = (Brush)FindResource("Brand");
            RequestStatus.Text = $"Request {status.ToString().ToLowerInvariant()}.";
            if (status == BookingStatus.Accepted)
            {
                ShowChatForBooking(row.BookingId);
            }
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelected(BookingStatus.Accepted);
        }

        private void Decline_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelected(BookingStatus.Declined);
        }

        private void RequestsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RequestsList.SelectedItem is SitterRequestRow row)
            {
                RequestMessage.Text = string.IsNullOrWhiteSpace(row.Message) ? "(no message)" : row.Message;

                // Show pet image if available
                try
                {
                    var img = FindName("SelectedPetImage") as Image;
                    if (img != null)
                    {
                        if (!string.IsNullOrWhiteSpace(row.PetImagePath) && File.Exists(row.PetImagePath))
                        {
                            img.Visibility = Visibility.Visible;
                            img.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(row.PetImagePath));
                        }
                        else
                        {
                            img.Source = null;
                        }
                    }
                }
                catch
                {
                    try { var img2 = FindName("SelectedPetImage") as Image; if (img2 != null) img2.Source = null; } catch { }
                }
            }
        }

        private void ViewDetails_Click(object sender, RoutedEventArgs e)
        {
            if (!(RequestsList.SelectedItem is SitterRequestRow row))
            {
                RequestStatus.Foreground = (Brush)FindResource("Danger");
                RequestStatus.Text = "Select a request first.";
                return;
            }

            var dialog = new JobDetailsWindow(row) { Owner = _shell };
            dialog.ShowDialog();
        }

        // --- Chat support ---------------------------------------------------------
        private void ShowChatForBooking(int bookingId)
        {
            // Only allow opening chat for bookings where current user is either owner or sitter
            var booking = FindMyBooking(bookingId);
            if (booking == null)
            {
                MessageBox.Show("You are not a participant in this booking.");
                return;
            }

            _activeChatBookingId = bookingId;
            ChatTab.Visibility = Visibility.Visible;
            // Load chat messages
            RefreshChat();
            // switch focus to chat tab
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
            // scroll to end
            ChatScroll.ScrollToEnd();
        }

        private void CancelBooking_Click(object sender, RoutedEventArgs e)
        {
            if (!(ChatsList.SelectedItem is SitterRequestRow row))
            {
                MessageBox.Show("Select a chat first.");
                return;
            }

            // Only allow cancelling bookings where the current user is the sitter
            var booking = FindMyBooking(row.BookingId);
            if (booking == null || booking.SitterUserId != Me.Id)
            {
                MessageBox.Show("Booking not found or you are not the sitter for this booking.");
                return;
            }

            var confirm = MessageBox.Show("Are you sure you want to cancel this booking?", "Confirm cancel", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _services.Bookings.UpdateStatus(booking.Id, BookingStatus.Cancelled);

            // If this booking's chat was open, close it
            if (_activeChatBookingId.HasValue && _activeChatBookingId.Value == booking.Id)
            {
                _activeChatBookingId = null;
                ChatTab.Visibility = Visibility.Collapsed;
            }

            // No explicit reload: the status change raised BookingStatusChanged and
            // the listener has already reloaded the requests and chats.
            ChatSelectedDetails.Text = "Booking cancelled.";
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

            // Security: ensure current user is participant in booking before inserting
            var booking = FindMyBooking(_activeChatBookingId.Value);
            if (booking == null)
            {
                MessageBox.Show("You are not a participant in this booking.");
                return;
            }
            // Chat is only open while the booking is accepted (REQ-PO-05/PS-04).
            // Re-checked here because a chat can stay open after the booking is
            // cancelled; the chat lists alone only filter what can be opened.
            if (booking.Status != BookingStatus.Accepted)
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
    }

    /// <summary>
    /// Display row for a sitter's incoming booking requests. Besides the columns
    /// shown in the grid, it carries the owner and pet details that the
    /// "View details" popup needs so the sitter can make an informed decision.
    /// </summary>
    public class SitterRequestRow
    {
        public int BookingId { get; }

        // Shown in the grid.
        public string OwnerName { get; }
        public string DateRange { get; }
        public int Nights { get; }
        public string Total { get; }
        public string Status { get; }
        public string Message { get; }

        // Extra owner details for the details popup.
        public string OwnerLocation { get; }
        public string OwnerPhone { get; }

        // Pet details for the details popup. When the owner did not pick a
        // specific pet, <see cref="HasSpecificPet"/> is false and only
        // <see cref="PetSummary"/> is meaningful.
        public bool HasSpecificPet { get; }
        public string PetSummary { get; }
        public string PetSpecies { get; }
        public string PetBreed { get; }
        public string PetAge { get; }
        public string PetNotes { get; }
        public string PetImagePath { get; }

        public SitterRequestRow(Booking b, User owner, Pet pet)
        {
            BookingId = b.Id;
            OwnerName = owner?.FullName ?? "(unknown)";
            OwnerLocation = Or(owner?.Location, "Not provided");
            OwnerPhone = Or(owner?.Phone, "Not provided");
            DateRange = b.StartDate.ToString("d MMM yyyy") + " – " + b.EndDate.ToString("d MMM yyyy");
            Nights = b.Nights;
            Total = OwnerDashboardView.Currency(b.EstimatedTotal);
            Status = b.Status.ToString();
            Message = b.Message;

            if (pet != null)
            {
                HasSpecificPet = true;
                PetSummary = pet.Name;
                PetImagePath = pet.ImagePath;
                PetSpecies = Or(pet.Species, "Not specified");
                PetBreed = Or(pet.Breed, "Not specified");
                PetAge = pet.AgeDisplay;
                PetNotes = Or(pet.Notes, "None provided");
            }
            else
            {
                HasSpecificPet = false;
                PetSummary = "The owner didn't choose a specific pet — this request covers all of their pets.";
            }
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
