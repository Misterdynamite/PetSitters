using System;
using PetSitters.Data;

namespace PetSitters.Services
{
    /// <summary>
    /// Which database the app ended up using at launch, and why, for the
    /// header status indicator and its tooltip. Never contains credentials.
    /// </summary>
    public sealed class DatabaseSelection
    {
        public Database Database { get; }

        /// <summary>True when the shared cloud (MySQL) database is in use.</summary>
        public bool UsingCloud { get; }

        /// <summary>
        /// True when a cloud database WAS configured but couldn't be used
        /// (unreachable, or connected but the schema step failed), so the app is
        /// running on local data that other users won't see.
        /// </summary>
        public bool FellBack { get; }

        /// <summary>Short label for the header, e.g. "Cloud database".</summary>
        public string Summary { get; }

        /// <summary>One or two sentences for the tooltip: where the data is, and why.</summary>
        public string Detail { get; }

        public DatabaseSelection(Database database, bool usingCloud, bool fellBack, string summary, string detail)
        {
            Database = database ?? throw new ArgumentNullException(nameof(database));
            UsingCloud = usingCloud;
            FellBack = fellBack;
            Summary = summary;
            Detail = detail;
        }

        /// <summary>A selection for a database chosen directly (tests, tools), without the launch logic.</summary>
        public static DatabaseSelection For(Database database)
        {
            bool cloud = database.Provider == DatabaseProvider.MySql;
            return new DatabaseSelection(database, cloud, false,
                cloud ? "Cloud database" : "Local database",
                cloud ? "Using the shared cloud database." : "Using the local database on this computer.");
        }
    }

    /// <summary>
    /// Launch-time choice of database: the shared MySQL server first, and the
    /// local SQLite file if MySQL isn't configured, is switched off
    /// (PETSITTERS_DB=sqlite), or can't be reached. The choice is made once per
    /// launch; there is no switching or syncing afterwards, so anything saved
    /// while running on the fallback stays on this computer.
    /// </summary>
    public static class DatabaseSelector
    {
        /// <summary>The real launch path, with the real database constructors.</summary>
        public static DatabaseSelection Select(AppConfig config)
        {
            return Select(config,
                Database.CreateLocalSqlite,
                url => Database.ForMySql(MySqlUrl.ToConnectionString(url, config.ConnectTimeoutSeconds)));
        }

        /// <summary>
        /// The decision itself, with injectable constructors so it can be tested
        /// without a network. <paramref name="createCloud"/> receives the URL.
        /// The cloud database's <see cref="Database.Initialize"/> is called here,
        /// because connecting and creating the schema IS the reachability check.
        /// </summary>
        public static DatabaseSelection Select(AppConfig config, Func<Database> createLocal, Func<string, Database> createCloud)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            if (config.ForceLocalDatabase)
                return Local(createLocal(), false, "Local database",
                    "Using the local database because " + AppConfig.DatabaseModeKey + " is set to use it.");

            if (config.DatabaseUrl == null)
                return Local(createLocal(), false, "Local database",
                    "No cloud database is configured (" + AppConfig.DatabaseUrlKey + " is not set), so data is stored on this computer.");

            try
            {
                Database cloud = createCloud(config.DatabaseUrl);
                cloud.Initialize();   // connects (TLS + login) and creates any missing tables; throws if unreachable
                // Deliberately no host name: the header can end up in screenshots and reports.
                return new DatabaseSelection(cloud, true, false, "Cloud database",
                    "Connected to the shared cloud database. Everyone using the app sees the same data.");
            }
            catch (Exception ex)
            {
                // ANY failure must not stop the app opening, so it always falls
                // back. But "can't connect" (offline, DNS, timeout, wrong password,
                // bad URL) and "connected, but the schema step failed" (a real bug
                // or a server change) are reported differently, so a database
                // error isn't silently passed off as being offline.
                bool connectivity = IsConnectivityFailure(ex);
                return Local(createLocal(), true,
                    connectivity ? "Offline: local database" : "Cloud database error: local database",
                    (connectivity ? "Couldn't connect to the cloud database: " : "Connected to the cloud database, but couldn't prepare it: ") +
                    DescribeFailure(ex, config.DatabaseUrl) + ". " +
                    "Using the local database instead: changes are saved on this computer only and " +
                    "won't be seen by other users. Restart the app to try the cloud database again.");
            }
        }

        /// <summary>
        /// True when the cloud database couldn't be reached or logged into, as
        /// opposed to a SQL error after connecting.
        /// </summary>
        public static bool IsConnectivityFailure(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (current is FormatException || current is System.Net.Sockets.SocketException ||
                    current is TimeoutException || current is System.Security.Authentication.AuthenticationException ||
                    current is System.IO.IOException)
                    return true;

                if (current is MySqlConnector.MySqlException mysql &&
                    (mysql.ErrorCode == MySqlConnector.MySqlErrorCode.UnableToConnectToHost ||
                     mysql.ErrorCode == MySqlConnector.MySqlErrorCode.AccessDenied ||
                     mysql.ErrorCode == MySqlConnector.MySqlErrorCode.UnknownDatabase))
                    return true;
            }
            return false;
        }

        private static DatabaseSelection Local(Database database, bool fellBack, string summary, string detail)
        {
            return new DatabaseSelection(database, false, fellBack, summary, detail);
        }

        /// <summary>
        /// A short, fixed-wording reason for the tooltip, plus an error code where
        /// one exists. Raw driver messages are NOT shown: they can name the server
        /// and the database user, which shouldn't end up in screenshots. The text is still run through
        /// <see cref="Redact"/> in case a future message echoes the URL.
        /// </summary>
        public static string DescribeFailure(Exception ex, string databaseUrl)
        {
            string reason = null;
            for (Exception current = ex; current != null && reason == null; current = current.InnerException)
            {
                switch (current)
                {
                    case FormatException format:
                        reason = format.Message;   // our own fixed text from MySqlUrl, never the URL
                        break;
                    case MySqlConnector.MySqlException mysql:
                        switch (mysql.ErrorCode)
                        {
                            case MySqlConnector.MySqlErrorCode.UnableToConnectToHost:
                                reason = "the server couldn't be reached (offline, blocked, or timed out)"; break;
                            case MySqlConnector.MySqlErrorCode.AccessDenied:
                                reason = "the user name or password in DATABASE_URL was rejected"; break;
                            case MySqlConnector.MySqlErrorCode.UnknownDatabase:
                                reason = "the database named in DATABASE_URL doesn't exist"; break;
                            default:
                                reason = "MySQL error " + (int)mysql.ErrorCode + " (" + mysql.ErrorCode + ")"; break;
                        }
                        break;
                    case System.Net.Sockets.SocketException socket:
                        reason = "network error " + socket.SocketErrorCode; break;
                    case TimeoutException _:
                        reason = "the connection timed out"; break;
                    case System.Security.Authentication.AuthenticationException _:
                        reason = "the secure (TLS) connection failed"; break;
                }
            }
            return Redact(reason ?? ex.GetType().Name, databaseUrl);
        }

        /// <summary>Replaces the whole URL and its password (raw and decoded) with "***".</summary>
        public static string Redact(string text, string databaseUrl)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(databaseUrl))
                return text;

            text = text.Replace(databaseUrl, "***");

            // Pull the password out of "scheme://user:password@host" without Uri,
            // which could reject a malformed URL that still contains a secret.
            int schemeEnd = databaseUrl.IndexOf("://", StringComparison.Ordinal);
            int at = databaseUrl.LastIndexOf('@');
            if (schemeEnd >= 0 && at > schemeEnd)
            {
                string userInfo = databaseUrl.Substring(schemeEnd + 3, at - schemeEnd - 3);
                int colon = userInfo.IndexOf(':');
                if (colon >= 0 && colon < userInfo.Length - 1)
                {
                    string password = userInfo.Substring(colon + 1);
                    text = text.Replace(password, "***");
                    string decoded = Uri.UnescapeDataString(password);
                    if (decoded.Length > 0)
                        text = text.Replace(decoded, "***");
                }
            }
            return text;
        }
    }
}
