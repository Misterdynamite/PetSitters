using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PetSitters.Data;

namespace PetSitters.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LocalImages.TrustedPathOrNull"/>, the guard on
    /// image paths read from the (shared) database. Only a file directly inside
    /// this PC's image folder is trusted. Anything else could make the app
    /// contact a network share (leaking the viewer's Windows login hash) or
    /// delete an arbitrary file via "Delete pet".
    /// </summary>
    [TestClass]
    public class LocalImagesTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        public void TrustedPathOrNull_FileDirectlyInTheImageFolder_IsTrusted()
        {
            string path = Path.Combine(LocalImages.Folder, "3f2a.png");

            Assert.AreEqual(path, LocalImages.TrustedPathOrNull(path));
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Security")]
        [TestCategory("InvalidInput")]
        [TestCategory("Negative")]
        [DataRow(@"\\attacker\share\pic.png", DisplayName = "UNC network share")]
        [DataRow(@"//attacker/share/pic.png", DisplayName = "UNC with forward slashes")]
        [DataRow(@"/\attacker\share\x~1.png", DisplayName = "UNC with mixed separators and a short name")]
        [DataRow(@"\/attacker/share/x~1.png", DisplayName = "UNC with mixed separators, other order")]
        [DataRow(@"\\?\C:\Windows\win.ini", DisplayName = "Win32 device path")]
        [DataRow(@"C:\Windows\win.ini", DisplayName = "Another folder")]
        [DataRow(@"pic.png", DisplayName = "Relative path")]
        [DataRow(@"C:\Windows\x:stream.png", DisplayName = "Malformed (alternate data stream)")]
        [DataRow("", DisplayName = "Empty")]
        [DataRow(null, DisplayName = "Null")]
        public void TrustedPathOrNull_AnythingElse_IsNull(string path)
        {
            Assert.IsNull(LocalImages.TrustedPathOrNull(path));
        }

        [DataTestMethod]
        [TestCategory("Unit")]
        [TestCategory("Security")]
        [TestCategory("Boundary")]
        [DataRow(@"..\..\..\Windows\win.ini", DisplayName = "Escapes the folder with ..")]
        [DataRow(@"sub\pic.png", DisplayName = "A sub-folder (the app never writes one)")]
        [DataRow(@"CON.png", DisplayName = "A device name")]
        [DataRow(@"pic.png.", DisplayName = "Trailing dot (Windows strips it)")]
        [DataRow(@"pic.png:secret", DisplayName = "Alternate data stream")]
        [DataRow(@"x/pic.png", DisplayName = "Forward slash inside")]
        // Paths that START inside the folder but aren't a plain file directly in it.
        public void TrustedPathOrNull_StartsInsideButEscapesOrNests_IsNull(string relativeToFolder)
        {
            Assert.IsNull(LocalImages.TrustedPathOrNull(LocalImages.Folder + @"\" + relativeToFolder));
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Positive")]
        [TestCategory("Boundary")]
        // The folder comparison ignores case (Windows paths do), but nothing else.
        public void TrustedPathOrNull_FolderInDifferentCase_IsTrusted()
        {
            string path = LocalImages.Folder.ToUpperInvariant() + @"\3f2a.png";

            Assert.AreEqual(path, LocalImages.TrustedPathOrNull(path));
        }
    }
}
