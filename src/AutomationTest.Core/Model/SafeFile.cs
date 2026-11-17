using System;
using System.IO;

namespace AutomationTest.Core.Model
{
    /// <summary>
    /// Writes a file so that the one that is there is never left half written: the new content goes to a temporary file next to it,
    /// which then takes its place. When the program or the computer stops in the middle, the old file is still whole.
    /// </summary>
    public static class SafeFile
    {
        /// <exception cref="IOException">The file cannot be written.</exception>
        /// <exception cref="UnauthorizedAccessException">The file or folder is read-only or not allowed.</exception>
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, bytes); // nothing to protect yet
                return;
            }
            string temp = path + ".tmp" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                File.WriteAllBytes(temp, bytes);
                try { File.Replace(temp, path, null); }
                catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException)
                {
                    // Some drives (network shares, FAT) cannot swap files: write over the old one as before.
                    File.Copy(temp, path, true);
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }
    }
}
