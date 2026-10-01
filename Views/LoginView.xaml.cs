using System.Windows;
using System.Windows.Controls;
using PetSitters.Models;
using PetSitters.Services;

namespace PetSitters.Views
{
    /// <summary>FR-2: login screen.</summary>
    public partial class LoginView : UserControl
    {
        private readonly AppServices _services;
        private readonly MainWindow _shell;

        public LoginView(AppServices services, MainWindow shell)
        {
            InitializeComponent();
            _services = services;
            _shell = shell;
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            // The role is only sent once the user has been asked to choose one
            // (REQ-GR-06); until then login works exactly as before.
            UserRole? role = null;
            if (RoleChoicePanel.Visibility == Visibility.Visible)
            {
                if (LoginOwnerRadio.IsChecked == true) role = UserRole.Owner;
                else if (LoginSitterRadio.IsChecked == true) role = UserRole.Sitter;
                else
                {
                    ShowError("Choose Owner or Sitter, then log in.");
                    return;
                }
            }

            AuthResult result = _services.Auth.Login(EmailBox.Text, PasswordBox.Password, role);

            if (result.RequiresRoleChoice)
            {
                RoleChoicePanel.Visibility = Visibility.Visible;
                ShowError(result.ErrorMessage);
                return;
            }

            if (!result.Success)
            {
                ShowError(result.ErrorMessage);
                return;
            }

            _shell.OnLoggedIn(result.User);
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            _shell.ShowRegister();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
