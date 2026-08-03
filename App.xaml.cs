using System.Windows;
using PetSitters.Services;

namespace PetSitters
{
    /// <summary>
    /// Application entry point. Builds the shared services (database +
    /// repositories) once, then opens the main window with them injected.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppServices services = AppServices.CreateDefault();

            var window = new MainWindow(services);
            window.Show();
        }
    }
}
