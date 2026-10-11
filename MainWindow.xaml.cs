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
        private AppServices _services;

        /// <summary>
        /// Opens showing "Loading..." (see RootContent in the XAML): the
        /// database is chosen in the background, then <see cref="Attach"/> is called.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called once the database is ready: shows the login screen. (The header
        /// no longer says which database is in use; if the app fell back to the
        /// local one, the login screen's offline notice says so instead.)
        /// </summary>
        public void Attach(AppServices services)
        {
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
            _services.CurrentUser = user;   // the dashboards read the signed-in user while loading
            UserControl dashboard;
            try
            {
                dashboard = user.Role == UserRole.Owner
                    ? (UserControl)new OwnerDashboardView(_services, this)
                    : new SitterDashboardView(_services, this);
            }
            catch
            {
                // Loading failed (e.g. the cloud connection dropped). Undo the sign-in
                // so the header doesn't say "Signed in" over the login screen; the
                // app's database-error handler then explains what happened.
                _services.CurrentUser = null;
                UpdateSessionBar();
                throw;
            }
            UpdateSessionBar();
            Navigate(dashboard);
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

        // Top navigation removed; dashboards still contain their own tabs.

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLogin();
        }
    }
}
