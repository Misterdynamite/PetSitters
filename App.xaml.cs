using System;
using System.Data.Common;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PetSitters.Services;

namespace PetSitters
{
    /// <summary>
    /// Application entry point. Shows the main window straight away in a
    /// "connecting" state, picks the database in the background (cloud MySQL
    /// first, local SQLite if that fails), then hands the services to the window.
    /// </summary>
    public partial class App : Application
    {
        private AppServices _services;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var window = new MainWindow();
            window.Show();

            try
            {
                // Connecting to the cloud server can take seconds (TLS over the
                // internet, or the full timeout when offline), so it must not
                // freeze the UI thread. The window shows "Connecting…" meanwhile.
                _services = await Task.Run(() => AppServices.CreateDefault());
            }
            catch (Exception ex)
            {
                // Only reachable if even the LOCAL database can't be opened (e.g.
                // %AppData% not writable): nothing to fall back to, so say so and exit.
                MessageBox.Show(window,
                    "Sitters4Us couldn't open its local database, so it can't start.\n\n" + ex.GetType().Name,
                    "Sitters4Us", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            window.Attach(_services);
        }

        /// <summary>
        /// Close pooled cloud connections politely (a protocol "quit" per session),
        /// so the shared server doesn't log an aborted connection every time
        /// someone closes the app.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            if (_services != null && _services.Storage.UsingCloud)
                MySqlConnector.MySqlConnection.ClearAllPools();
            base.OnExit(e);
        }

        /// <summary>
        /// Database calls happen on the UI thread after launch, so a failing one
        /// would otherwise crash the app and lose the session. Database errors
        /// become a message and the app keeps running. Two cases are told apart:
        /// the database couldn't be REACHED (cloud connection dropped: try
        /// again), or it REJECTED the change (a data or schema problem: retrying
        /// won't help, so the error code is shown for diagnosis). Exception text
        /// is never shown: it can include server and user names. Non-database
        /// exceptions are left alone, as before.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (!IsDatabaseFailure(e.Exception))
                return;

            e.Handled = true;
            bool cloud = _services != null && _services.Storage.UsingCloud;

            string message;
            if (DatabaseSelector.IsConnectivityFailure(e.Exception) || IsTransient(e.Exception))
            {
                message = "Sitters4Us couldn't reach its database, so your last action may not have been saved." +
                          (cloud
                              ? "\n\nCheck your internet connection and try again. If it keeps happening, restart the app: " +
                                "it will use this computer's local database if the cloud one is still unavailable."
                              : "\n\nTry again, and restart the app if it keeps happening.");
            }
            else
            {
                message = "The database couldn't save this change, so it wasn't saved.\n\n" +
                          "Check what you entered and try again. If it keeps happening, report this code: " +
                          ErrorCode(e.Exception) + ".";
            }

            MessageBox.Show(MainWindow, message, "Database problem", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>True if the exception, or anything it wraps, came from the database or the network.</summary>
        internal static bool IsDatabaseFailure(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (current is DbException || current is System.Net.Sockets.SocketException || current is TimeoutException)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// MySQL errors the driver marks as transient (lost connection, deadlock,
        /// ...): worth retrying. (.NET Framework's DbException has no IsTransient,
        /// so this asks MySqlConnector directly.)
        /// </summary>
        private static bool IsTransient(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (current is MySqlConnector.MySqlException mysql && mysql.IsTransient)
                    return true;
            }
            return false;
        }

        /// <summary>A short, secret-free code identifying the database error, e.g. "MySQL 1406" or "SQLite Constraint".</summary>
        private static string ErrorCode(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (current is MySqlConnector.MySqlException mysql)
                    return "MySQL " + (int)mysql.ErrorCode + " (" + mysql.ErrorCode + ")";
                if (current is System.Data.SQLite.SQLiteException sqlite)
                    return "SQLite " + sqlite.ResultCode;
            }
            return ex.GetType().Name;
        }
    }
}
