using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace PetSitters.Services
{
    /// <summary>
    /// Runtime settings, read once at launch. Sources, highest priority first:
    /// 1. real environment variables (so CI, the UI tests or a shell can override);
    /// 2. a <c>.env</c> file next to PetSitters.exe (copied there by the build
    ///    from the repo root, see README "Configuring the database").
    /// Nothing here is ever logged or shown: DATABASE_URL contains a password.
    /// </summary>
    public sealed class AppConfig
    {
        /// <summary>Name of the settings file looked for beside the exe.</summary>
        public const string EnvFileName = ".env";

        /// <summary>mysql://user:password@host:port/database?ssl-mode=REQUIRED. Absent means "local only".</summary>
        public const string DatabaseUrlKey = "DATABASE_URL";

        /// <summary>
        /// "auto" (default): MySQL if DATABASE_URL is set and reachable, else SQLite.
        /// "sqlite": always use the local database (used by the UI tests, or to work offline on purpose).
        /// </summary>
        public const string DatabaseModeKey = "PETSITTERS_DB";

        /// <summary>Seconds the launch-time MySQL connection may take before falling back (1-60).</summary>
        public const string ConnectTimeoutKey = "DB_CONNECT_TIMEOUT_SECONDS";

        public string DatabaseUrl { get; }

        /// <summary>True when PETSITTERS_DB=sqlite: skip MySQL entirely.</summary>
        public bool ForceLocalDatabase { get; }

        public int ConnectTimeoutSeconds { get; }

        private AppConfig(string databaseUrl, bool forceLocal, int connectTimeoutSeconds)
        {
            DatabaseUrl = databaseUrl;
            ForceLocalDatabase = forceLocal;
            ConnectTimeoutSeconds = connectTimeoutSeconds;
        }

        /// <summary>The app's real configuration: .env beside the exe, overridden by environment variables.</summary>
        public static AppConfig Load(string baseDirectory)
        {
            return Load(Path.Combine(baseDirectory, EnvFileName), Environment.GetEnvironmentVariables());
        }

        /// <summary>Testable form: an explicit .env path (may not exist) and an explicit environment.</summary>
        public static AppConfig Load(string envFilePath, IDictionary environment)
        {
            Dictionary<string, string> values = EnvFile.Load(envFilePath);

            // Environment variables win over the file, for the keys this app reads.
            if (environment != null)
            {
                foreach (string key in new[] { DatabaseUrlKey, DatabaseModeKey, ConnectTimeoutKey })
                {
                    if (environment.Contains(key) && environment[key] is string value && value.Length > 0)
                        values[key] = value;
                }
            }

            return FromValues(values);
        }

        /// <summary>Builds the settings from already-merged key/value pairs.</summary>
        public static AppConfig FromValues(IDictionary<string, string> values)
        {
            values.TryGetValue(DatabaseUrlKey, out string url);
            values.TryGetValue(DatabaseModeKey, out string mode);
            values.TryGetValue(ConnectTimeoutKey, out string timeoutText);

            // Only "sqlite" (or its alias "local") changes behaviour; anything
            // else, including a typo, means the normal MySQL-first behaviour.
            bool forceLocal = string.Equals(mode?.Trim(), "sqlite", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(mode?.Trim(), "local", StringComparison.OrdinalIgnoreCase);

            int timeout = Data.MySqlUrl.DefaultConnectTimeoutSeconds;
            if (int.TryParse(timeoutText?.Trim(), out int parsed) && parsed >= 1 && parsed <= 60)
                timeout = parsed;

            return new AppConfig(string.IsNullOrWhiteSpace(url) ? null : url.Trim(), forceLocal, timeout);
        }
    }

    /// <summary>
    /// Minimal reader for <c>.env</c> files, in the common dotenv format:
    /// <c>KEY=value</c> per line; blank lines and lines starting with # are ignored;
    /// an optional leading <c>export </c> is allowed; values may be wrapped in
    /// single or double quotes; everything after the first '=' is the value
    /// (URLs and passwords may contain '=').
    /// </summary>
    public static class EnvFile
    {
        /// <summary>Reads the file if it exists; a missing file means "no settings", not an error.</summary>
        public static Dictionary<string, string> Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new Dictionary<string, string>(StringComparer.Ordinal);
            return Parse(File.ReadAllLines(path));
        }

        public static Dictionary<string, string> Parse(IEnumerable<string> lines)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                if (line.StartsWith("export ", StringComparison.Ordinal))
                    line = line.Substring("export ".Length).TrimStart();

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;   // not KEY=VALUE: ignore rather than fail the launch

                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\'')))
                    value = value.Substring(1, value.Length - 2);

                values[key] = value;   // later lines override earlier ones, as in dotenv
            }
            return values;
        }
    }
}
