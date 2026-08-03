using System.Windows;
using System.Windows.Controls;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Views
{
    /// <summary>FR-1: account creation, including personal details and location (FR-3 / FR-7).</summary>
    public partial class RegisterView : UserControl
    {
        private readonly AppServices _services;
        private readonly MainWindow _shell;

        public RegisterView(AppServices services, MainWindow shell)
        {
            InitializeComponent();
            _services = services;
            _shell = shell;
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            UserRole role = OwnerRadio.IsChecked == true ? UserRole.Owner : UserRole.Sitter;

            AuthResult result = _services.Auth.Register(
                EmailBox.Text, PasswordBox.Password, role,
                NameBox.Text, PhoneBox.Text, LocationBox.Text);

            if (!result.Success)
            {
                ErrorText.Text = result.ErrorMessage;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            // Registration succeeds -> sign the new user straight in.
            _shell.OnLoggedIn(result.User);
        }

        private void BackToLogin_Click(object sender, RoutedEventArgs e)
        {
            _shell.ShowLogin();
        }
    }
}
