using System.Windows;

namespace PetSitters.Views
{
    /// <summary>
    /// A read-only popup that shows the full details of a single incoming booking
    /// request so a sitter can decide whether to accept it: the pet, the booking
    /// dates and cost, the owner's contact details and their message. Opened from
    /// the "View details" button on the sitter's Booking Requests tab.
    /// </summary>
    public partial class JobDetailsWindow : Window
    {
        public JobDetailsWindow(SitterRequestRow request)
        {
            InitializeComponent();

            SubtitleText.Text = "From " + request.OwnerName;

            if (request.HasSpecificPet)
            {
                PetSummaryText.Text = request.PetSummary;
                PetSummaryText.FontWeight = FontWeights.SemiBold;
                PetSpeciesText.Text = request.PetSpecies;
                PetBreedText.Text = request.PetBreed;
                PetAgeText.Text = request.PetAge;
                PetNotesText.Text = request.PetNotes;
                PetDetailPanel.Visibility = Visibility.Visible;
            }
            else
            {
                PetSummaryText.Text = request.PetSummary;
                PetDetailPanel.Visibility = Visibility.Collapsed;
            }

            DatesText.Text = request.DateRange;
            NightsText.Text = request.Nights.ToString();
            TotalText.Text = request.Total;
            StatusText.Text = request.Status;

            OwnerNameText.Text = request.OwnerName;
            OwnerLocationText.Text = request.OwnerLocation;
            OwnerPhoneText.Text = request.OwnerPhone;

            MessageText.Text = string.IsNullOrWhiteSpace(request.Message) ? "(no message)" : request.Message;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
