using System;
using System.IO;
using System.Linq;

namespace PetSitters.Data
{
    /// <summary>
    /// Where profile and pet pictures live (%AppData%\PetSitters\UserImages on
    /// THIS computer), and the guard on image paths read back from the database.
    ///
    /// Why the guard exists: with the shared cloud database, an image path in a
    /// row may have been written by another computer, or by anyone holding the
    /// database credential. Used as-is, a planted path could:
    /// - point at a network share (\\server\share\x.png), so just DISPLAYING it
    ///   makes Windows try to log in to that server, leaking the viewer's
    ///   Windows login (NTLM) hash;
    /// - point at any file on the viewer's PC, which "Delete pet" would then
    ///   delete, since it removes the pet's image file.
    /// So only a file directly inside this PC's image folder is trusted;
    /// anything else reads back as "no image". (Images uploaded on another PC
    /// can't be shown here anyway: only the path is stored, not the picture.)
    /// </summary>
    public static class LocalImages
    {
        // Windows device names: "CON.png" etc. open a device, not a file.
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>The folder imported pictures are copied into on this computer.</summary>
        public static string Folder
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "PetSitters", "UserImages");
            }
        }

        /// <summary>
        /// <paramref name="path"/> if it is exactly "&lt;Folder&gt;\&lt;plain file name&gt;";
        /// otherwise null.
        ///
        /// The check is done on the TEXT only, never by resolving the path:
        /// resolving it (Path.GetFullPath) can itself contact a network server.
        /// A path such as "/\server\share\x~1.png" is turned into a network path
        /// and the "~" makes .NET look up the long file name on that server. That
        /// happens before any folder comparison, so it would leak the login hash
        /// even though the path is then rejected. Comparing the text with our own
        /// folder also works when %AppData% itself is redirected to a network
        /// share (common on lab PCs): the app's own folder is then on a share, and
        /// it still matches.
        /// </summary>
        public static string TrustedPathOrNull(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            int lastSeparator = path.LastIndexOf('\\');
            if (lastSeparator <= 0 || path.IndexOf('/') >= 0)
                return null;   // not "folder\name", or uses '/' (never in paths this app writes)

            string directory = path.Substring(0, lastSeparator);
            string fileName = path.Substring(lastSeparator + 1);

            // The folder must be OUR folder, character for character (ignoring
            // case). This rules out other folders, UNC/device prefixes, "..",
            // sub-folders and every alternative spelling of a path.
            if (!string.Equals(directory, Folder, StringComparison.OrdinalIgnoreCase))
                return null;

            // A plain file name: no invalid characters (including ':' for
            // alternate data streams), not "." / "..", no trailing dot or space
            // (Windows silently strips those), and not a device name.
            if (fileName.Length == 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                fileName == "." || fileName == ".." || fileName.EndsWith(".") || fileName.EndsWith(" "))
                return null;
            string baseName = fileName.Split('.')[0];
            if (ReservedNames.Any(r => string.Equals(r, baseName, StringComparison.OrdinalIgnoreCase)))
                return null;

            return path;
        }
    }
}
