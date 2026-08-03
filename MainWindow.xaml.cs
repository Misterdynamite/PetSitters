using System.Windows;
using System.Windows.Controls;
using PetSitters.Models;
using PetSitters.Services;
using PetSitters.Views;

namespace PetSitters
{
    /// <summary>
    /// Shell window. Owns the session bar and swaps the active view (login,
    /// register, or a role-specific dashboard) into <c>RootContent</c>.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AppServices _services;

        public MainWindow(AppServices services)
        {
            InitializeComponent();
            _services = services;
            ShowLogin();
        }

        /// <summary>Replaces the whole content area with the given view.</summary>
        public void Navigate(UserControl view)
        {
            RootContent.Content = view;
        }

        public void ShowLogin()
        {
            _services.CurrentUser = null;
            UpdateSessionBar();
            Navigate(new LoginView(_services, this));
        }

        public void ShowRegister()
        {
            Navigate(new RegisterView(_services, this));
        }

        /// <summary>Called after a successful login or registration.</summary>
        public void OnLoggedIn(User user)
        {
            _services.CurrentUser = user;
            UpdateSessionBar();

            if (user.Role == UserRole.Owner)
                Navigate(new OwnerDashboardView(_services, this));
            else
                Navigate(new SitterDashboardView(_services, this));
        }

        private void UpdateSessionBar()
        {
            User user = _services.CurrentUser;
            if (user == null)
            {
                SessionBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                SessionText.Text = $"Signed in as {user.FullName} ({user.Role})";
                SessionBar.Visibility = Visibility.Visible;
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLogin();
        }
    }
}
