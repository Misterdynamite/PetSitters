using System;
using System.Collections.Generic;
using MySqlConnector;

namespace PetSitters.Data
{
    /// <summary>
    /// Turns a database URL of the form
    /// <c>mysql://user:password@host:port/database?ssl-mode=REQUIRED</c>
    /// (the format most managed MySQL hosts hand out) into a MySqlConnector
    /// connection string. MySqlConnector doesn't parse URLs itself.
    ///
    /// SECURITY: the URL contains the password. Every error message here is
    /// fixed text that never echoes the URL or any part of it, so a bad value
    /// can't leak through an error dialog, a log or a test failure.
    /// </summary>
    public static class MySqlUrl
    {
        /// <summary>MySQL's standard port, used when the URL doesn't give one.</summary>
        public const int DefaultPort = 3306;

        /// <summary>
        /// How long the launch-time connection attempt may take before the app
        /// gives up and falls back to the local database. Opening a TLS
        /// connection to the cloud server measured about 1.2-1.9 s, so 8 s leaves
        /// headroom without making an offline launch wait too long.
        /// </summary>
        public const int DefaultConnectTimeoutSeconds = 8;

        /// <summary>Seconds any single MySQL command may run before it fails.</summary>
        public const int CommandTimeoutSeconds = 10;

        /// <summary>
        /// Upper bound on pooled connections per running app. The server's
        /// connection limit is small and shared, so each copy of this desktop
        /// app stays a small, polite client.
        /// </summary>
        public const int MaximumPoolSize = 5;

        /// <param name="url">The DATABASE_URL value.</param>
        /// <param name="connectTimeoutSeconds">Seconds before a connection attempt fails (minimum 1).</param>
        /// <exception cref="FormatException">The URL is missing parts or isn't a mysql:// URL (message never contains the URL).</exception>
        public static string ToConnectionString(string url, int connectTimeoutSeconds = DefaultConnectTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new FormatException("DATABASE_URL is empty.");

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri) ||
                !string.Equals(uri.Scheme, "mysql", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("DATABASE_URL must be a mysql:// URL: mysql://user:password@host:port/database");

            if (string.IsNullOrEmpty(uri.Host))
                throw new FormatException("DATABASE_URL has no host name.");

            // UserInfo is still percent-encoded, so split on the first ':' BEFORE
            // decoding (an encoded %3A in a password must stay part of the password).
            string[] userInfo = uri.UserInfo.Split(new[] { ':' }, 2);
            if (userInfo.Length < 2 || userInfo[0].Length == 0)
                throw new FormatException("DATABASE_URL must include a user name and password (mysql://user:password@host/database).");

            string database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
            if (database.Length == 0 || database.Contains("/"))
                throw new FormatException("DATABASE_URL must name exactly one database after the host (…/database).");

            Dictionary<string, string> query = ParseQuery(uri.Query);

            var settings = new MySqlConnectionStringBuilder
            {
                Server = uri.Host,
                // Uri reports -1 when an unregistered scheme like mysql:// gives no port.
                Port = uri.Port > 0 ? (uint)uri.Port : DefaultPort,
                UserID = Uri.UnescapeDataString(userInfo[0]),
                Password = Uri.UnescapeDataString(userInfo[1]),
                Database = database,
                SslMode = ParseSslMode(query),
                ConnectionTimeout = (uint)Math.Max(1, connectTimeoutSeconds),
                // Queries run on the UI thread, so a stalled query must give up
                // well before the driver's 30 s default. The longest launch wait
                // is therefore ConnectionTimeout + 10 s.
                DefaultCommandTimeout = CommandTimeoutSeconds,
                CharacterSet = "utf8mb4",
                MaximumPoolSize = MaximumPoolSize,
                // Keep one connection open and warm: opening a new one costs a
                // ~1.5 s TLS handshake, so without this the first click after a
                // pause would be noticeably slow.
                MinimumPoolSize = 1,
                ConnectionIdleTimeout = 600,
                // Skip the "reset session" round trip on every pooled Open. Measured
                // on the cloud server: 392 ms per open + query with the reset, 195 ms
                // without, so it halves the cost of every database call. Safe because
                // the app never sets session state (variables, temp tables, open
                // transactions) that a reset would need to clear.
                ConnectionReset = false,
                // TCP keepalive (seconds) so home routers don't silently drop the
                // idle pooled connection.
                Keepalive = 60,
            };
            return settings.ConnectionString;
        }

        /// <summary>
        /// The ssl-mode query option, using MySQL's own names. When absent we
        /// default to REQUIRED (encrypt), not MySQL's PREFERRED: this app sends
        /// passwords and personal details over the internet.
        /// </summary>
        private static MySqlSslMode ParseSslMode(Dictionary<string, string> query)
        {
            if (!query.TryGetValue("ssl-mode", out string mode) && !query.TryGetValue("sslmode", out mode))
                return MySqlSslMode.Required;

            switch (mode.Trim().ToUpperInvariant().Replace('-', '_'))
            {
                case "DISABLED": return MySqlSslMode.None;
                case "PREFERRED": return MySqlSslMode.Preferred;
                case "REQUIRED": return MySqlSslMode.Required;
                case "VERIFY_CA": return MySqlSslMode.VerifyCA;
                case "VERIFY_IDENTITY": return MySqlSslMode.VerifyFull;
                default:
                    throw new FormatException(
                        "DATABASE_URL has an unknown ssl-mode. Use DISABLED, PREFERRED, REQUIRED, VERIFY_CA or VERIFY_IDENTITY.");
            }
        }

        /// <summary>Parses "?a=1&amp;b=2" into a case-insensitive dictionary (no System.Web dependency).</summary>
        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split(new[] { '=' }, 2);
                result[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            }
            return result;
        }
    }
}
