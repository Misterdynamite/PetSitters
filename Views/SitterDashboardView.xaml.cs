using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

            LoadDetails();
            LoadProfile();
            LoadRequests();
        }

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

        // Current booking id for which chat is open
        private int? _activeChatBookingId;

        // ---- FR-7: personal details ------------------------------------------------
        private void LoadDetails()
        {
            EmailText.Text = Me.Email;
            NameBox.Text = Me.FullName;
            PhoneBox.Text = Me.Phone;
            LocationBox.Text = Me.Location;
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
            var rows = new List<SitterRequestRow>();
            // Only show pending requests in the requests list; accepted/declined are removed
            foreach (Booking b in _services.Bookings.GetForSitter(Me.Id).Where(b => b.Status == BookingStatus.Pending))
            {
                User owner = _services.Users.FindById(b.OwnerUserId);

                // The request may be for one specific pet, or for "all my pets"
                // (PetId is null). Look the pet up so the details popup can show it.
                Pet pet = null;
                if (b.PetId.HasValue)
                    pet = _services.Pets.GetByOwner(b.OwnerUserId).FirstOrDefault(p => p.Id == b.PetId.Value);

                rows.Add(new SitterRequestRow(b, owner, pet));
            }
            RequestsList.ItemsSource = rows;
            RequestMessage.Text = "Select a request to view its message.";
            RequestStatus.Text = string.Empty;
            // Also refresh active chats view
            LoadChats();
        }

        private void LoadChats()
        {
            var rows = new List<SitterRequestRow>();
            // Chat list shows accepted bookings where current user is participant
            foreach (Booking b in _services.Bookings.GetForSitter(Me.Id).Where(b => b.Status == BookingStatus.Accepted))
            {
                User owner = _services.Users.FindById(b.OwnerUserId);
                Pet pet = null;
                if (b.PetId.HasValue)
                    pet = _services.Pets.GetByOwner(b.OwnerUserId).FirstOrDefault(p => p.Id == b.PetId.Value);
                rows.Add(new SitterRequestRow(b, owner, pet));
            }
            // Also include bookings where the current user is the owner and sitter accepted
            foreach (Booking b in _services.Bookings.GetForOwner(Me.Id).Where(b => b.Status == BookingStatus.Accepted))
            {
                User sitter = _services.Users.FindById(b.SitterUserId);
                Pet pet = null;
                if (b.PetId.HasValue)
                    pet = _services.Pets.GetByOwner(b.OwnerUserId).FirstOrDefault(p => p.Id == b.PetId.Value);
                rows.Add(new SitterRequestRow(b, sitter, pet));
            }
            ChatsList.ItemsSource = rows;
            ChatSelectedDetails.Text = "Select a chat to open.";
        }

        private void UpdateSelected(BookingStatus status)
        {
            if (!(RequestsList.SelectedItem is SitterRequestRow row))
            {
                RequestStatus.Foreground = (Brush)FindResource("Danger");
                RequestStatus.Text = "Select a request first.";
                return;
            }

            _services.Bookings.UpdateStatus(row.BookingId, status);
            RequestStatus.Foreground = (Brush)FindResource("Brand");
            RequestStatus.Text = $"Request {status.ToString().ToLowerInvariant()}.";
            LoadRequests();
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
                RequestMessage.Text = string.IsNullOrWhiteSpace(row.Message) ? "(no message)" : row.Message;
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
            var booking = _services.Bookings.GetForSitter(Me.Id).FirstOrDefault(b => b.Id == bookingId)
                          ?? _services.Bookings.GetForOwner(Me.Id).FirstOrDefault(b => b.Id == bookingId);
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
            var messages = _services.Chats.GetForBooking(_activeChatBookingId.Value);
            ChatMessagesList.Items.Clear();
            foreach (var m in messages)
            {
                var text = new TextBlock { Text = $"{(_services.Users.FindById(m.SenderUserId)?.FullName ?? "Unknown")}: {m.MessageText}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,4,0,4) };
                ChatMessagesList.Items.Add(text);
            }
            // scroll to end
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

            // Security: ensure current user is participant in booking before inserting
            var booking = _services.Bookings.GetForSitter(Me.Id).FirstOrDefault(b => b.Id == _activeChatBookingId.Value)
                          ?? _services.Bookings.GetForOwner(Me.Id).FirstOrDefault(b => b.Id == _activeChatBookingId.Value);
            if (booking == null)
            {
                MessageBox.Show("You are not a participant in this booking.");
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
