using System;
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

            LoadDetails();
            LoadPets();
            LoadSitters();
            LoadBookings();
        }

        private User Me => _services.CurrentUser;

        // ---- FR-3: personal details ------------------------------------------------
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
            PetsList.ItemsSource = _services.Pets.GetByOwner(Me.Id);
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
                PetStatus.Text = "Age must be a whole number (0 or more).";
                return;
            }

            _services.Pets.Insert(new Pet
            {
                OwnerUserId = Me.Id,
                Name = PetNameBox.Text.Trim(),
                Species = PetSpeciesBox.Text.Trim(),
                Breed = PetBreedBox.Text.Trim(),
                Age = age,
                Notes = PetNotesBox.Text.Trim()
            });

            PetNameBox.Clear();
            PetSpeciesBox.Clear();
            PetBreedBox.Clear();
            PetAgeBox.Text = "0";
            PetNotesBox.Clear();

            LoadPets();
            RefreshBookingPetCombo();
        }

        private void DeletePet_Click(object sender, RoutedEventArgs e)
        {
            if (PetsList.SelectedItem is Pet pet)
            {
                _services.Pets.Delete(pet.Id);
                LoadPets();
                RefreshBookingPetCombo();
            }
            else
            {
                PetStatus.Text = "Select a pet in the list to delete it.";
            }
        }

        // ---- FR-5: browse sitters --------------------------------------------------
        private void LoadSitters()
        {
            var rows = new List<SitterRow>();
            foreach (User sitter in _services.Users.GetByRole(UserRole.Sitter))
            {
                SitterProfile profile = _services.SitterProfiles.GetByUserId(sitter.Id);
                rows.Add(new SitterRow(sitter, profile));
            }
            SittersList.ItemsSource = rows;
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
            pets.AddRange(_services.Pets.GetByOwner(Me.Id));
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

            if (start < DateTime.Today)
            {
                BookingStatus.Text = "Start date cannot be in the past.";
                return;
            }
            if (end <= start)
            {
                BookingStatus.Text = "End date must be after the start date.";
                return;
            }

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
            _services.Bookings.Insert(booking);

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
            var rows = new List<OwnerBookingRow>();
            foreach (Booking b in _services.Bookings.GetForOwner(Me.Id))
            {
                User sitter = _services.Users.FindById(b.SitterUserId);
                string petName = "All my pets";
                if (b.PetId.HasValue)
                {
                    Pet p = _services.Pets.GetByOwner(Me.Id).FirstOrDefault(x => x.Id == b.PetId.Value);
                    petName = p?.Name ?? "(removed pet)";
                }
                rows.Add(new OwnerBookingRow(b, sitter?.FullName ?? "(unknown)", petName));
            }
            BookingsList.ItemsSource = rows;
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
        public string SitterName { get; }
        public string PetName { get; }
        public string DateRange { get; }
        public int Nights { get; }
        public string Total { get; }
        public string Status { get; }

        public OwnerBookingRow(Booking b, string sitterName, string petName)
        {
            SitterName = sitterName;
            PetName = petName;
            DateRange = b.StartDate.ToString("d MMM yyyy") + " – " + b.EndDate.ToString("d MMM yyyy");
            Nights = b.Nights;
            Total = OwnerDashboardView.Currency(b.EstimatedTotal);
            Status = b.Status.ToString();
        }
    }
}
