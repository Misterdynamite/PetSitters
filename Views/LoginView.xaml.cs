using System.Windows;
using System.Windows.Controls;
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
            AuthResult result = _services.Auth.Login(EmailBox.Text, PasswordBox.Password);

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
