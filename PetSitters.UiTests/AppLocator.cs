using System;
using System.IO;

namespace PetSitters.UiTests
{
    /// <summary>
    /// Finds the built PetSitters.exe and the app's live SQLite database on disk.
    ///
    /// The tests deliberately drive the same database the real app uses
    /// (<c>%AppData%\PetSitters\petsitters.db</c>) so they exercise the genuine
    /// startup + persistence path. Because of that, each run wipes that file
    /// first (see <see cref="WipeDatabase"/>) to start from a known-empty state.
    /// </summary>
    internal static class AppLocator
    {
        /// <summary>
        /// Locates <c>PetSitters.exe</c>. Resolution order:
        /// 1. the <c>PETSITTERS_EXE</c> environment variable, if it points at a real file;
        /// 2. <c>&lt;solutionDir&gt;\bin\Debug\PetSitters.exe</c> then <c>...\bin\Release\...</c>,
        ///    where the solution directory is the first ancestor containing PetSitters.sln.
        /// </summary>
        public static string FindExecutable()
        {
            string fromEnv = Environment.GetEnvironmentVariable("PETSITTERS_EXE");
            if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
                return fromEnv;

            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "PetSitters.sln")))
                {
                    foreach (string configuration in new[] { "Debug", "Release" })
                    {
                        string candidate = Path.Combine(dir.FullName, "bin", configuration, "PetSitters.exe");
                        if (File.Exists(candidate))
                            return candidate;
                    }

                    throw new FileNotFoundException(
                        "Found PetSitters.sln at '" + dir.FullName + "' but no PetSitters.exe under " +
                        "bin\\Debug or bin\\Release. Build the PetSitters app first (Build > Build Solution), " +
                        "or set the PETSITTERS_EXE environment variable to the executable's full path.");
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException(
                "Could not find PetSitters.sln in any parent of '" + AppContext.BaseDirectory +
                "'. Set the PETSITTERS_EXE environment variable to point at PetSitters.exe.");
        }

        /// <summary>Full path to the app's SQLite file: <c>%AppData%\PetSitters\petsitters.db</c>.</summary>
        public static string DatabasePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "PetSitters", "petsitters.db");
        }

        /// <summary>
        /// Deletes the live database (and any SQLite side-car files) so the next
        /// app launch recreates an empty schema. Safe to call when the file does
        /// not exist yet.
        ///
        /// This clean-slate step is a core part of the regression suite: every test
        /// must start from a known-empty system, otherwise data left behind by an
        /// earlier run (duplicate emails, stale bookings) silently changes what the
        /// UI shows and the results stop meaning anything. It therefore verifies
        /// the delete rather than failing quietly.
        /// </summary>
        /// <exception cref="IOException">
        /// The database could not be removed - most often because a PetSitters.exe
        /// from a previous run is still holding it open.
        /// </exception>
        public static void WipeDatabase()
        {
            string db = DatabasePath();

            foreach (string path in new[] { db, db + "-wal", db + "-shm", db + "-journal" })
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    File.Delete(path);
                }
                catch (IOException ex)
                {
                    throw new IOException(
                        "Could not delete '" + path + "' to start the regression run from a clean database. " +
                        "A PetSitters.exe from a previous run is probably still running - close it and retry.", ex);
                }
            }

            // Verify, so a silent failure can never let a test run against stale data.
            if (File.Exists(db))
            {
                throw new IOException(
                    "The database at '" + db + "' still exists after the wipe; refusing to run against stale data.");
            }
        }
    }
}
