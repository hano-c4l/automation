using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AutomationTest.Core.Runner
{
    /// <summary>
    /// A folder that is a link (a junction or a symbolic link): it stands in one place and shows the content of another. The
    /// File/Folder actions never go through one: Copy makes the same link at the destination, Delete removes only the link.
    /// </summary>
    internal static class FolderLink
    {
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint GENERIC_WRITE = 0x40000000, OPEN_EXISTING = 3;
        private const uint FILE_SHARE_ALL = 7;
        private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8, FSCTL_SET_REPARSE_POINT = 0x000900A4;
        private const int ERROR_PRIVILEGE_NOT_HELD = 1314;
        private const int MaxReparseBytes = 16 * 1024;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

        private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003, IO_REPARSE_TAG_SYMLINK = 0xA000000C;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime, ftLastAccessTime, ftLastWriteTime;
            public uint nFileSizeHigh, nFileSizeLow;
            public uint dwReserved0; // the reparse tag when the entry is a reparse point
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFile(string name, out WIN32_FIND_DATA data);

        [DllImport("kernel32.dll")]
        private static extern bool FindClose(IntPtr handle);

        /// <summary>
        /// True when the path is a folder that is a junction or a symbolic link. Other folders that Windows marks the same way (a
        /// OneDrive folder with files on demand, for example) are ordinary folders here: their reparse tag is something else.
        /// </summary>
        public static bool IsLink(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) == 0) return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) { return false; }

            WIN32_FIND_DATA data;
            IntPtr find = FindFirstFile(path.TrimEnd('\\', '/'), out data);
            if (find == new IntPtr(-1)) return false;
            FindClose(find);
            return data.dwReserved0 == IO_REPARSE_TAG_MOUNT_POINT || data.dwReserved0 == IO_REPARSE_TAG_SYMLINK;
        }

        /// <summary>Makes at <paramref name="target"/> a link like the one at <paramref name="source"/> (to the same place); what the link shows is not read.</summary>
        /// <exception cref="IOException">The link cannot be read or made.</exception>
        public static void Copy(string source, string target, bool overwrite)
        {
            if (File.Exists(target)) throw new IOException("\"" + target + "\" is a file, so the link \"" + source + "\" cannot be copied there.");
            if (Directory.Exists(target))
            {
                if (!IsLink(target)) throw new IOException("\"" + target + "\" is a folder, so the link \"" + source + "\" cannot be copied there.");
                if (!overwrite) throw new IOException("The link \"" + target + "\" exists already (turn on Overwrite to replace it).");
                Directory.Delete(target, false); // the link only, not what it shows
            }

            byte[] data = new byte[MaxReparseBytes];
            int length;
            using (var handle = CreateFile(source, 0, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw Problem("read the link", source, Marshal.GetLastWin32Error());
                if (!DeviceIoControl(handle, FSCTL_GET_REPARSE_POINT, null, 0, data, data.Length, out length, IntPtr.Zero))
                    throw Problem("read the link", source, Marshal.GetLastWin32Error());
            }

            Directory.CreateDirectory(target);
            bool made = false;
            try
            {
                using (var handle = CreateFile(target, GENERIC_WRITE, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero))
                {
                    if (handle.IsInvalid) throw Problem("make the link", target, Marshal.GetLastWin32Error());
                    int ignored;
                    if (!DeviceIoControl(handle, FSCTL_SET_REPARSE_POINT, data, length, null, 0, out ignored, IntPtr.Zero))
                        throw Problem("make the link", target, Marshal.GetLastWin32Error());
                }
                made = true;
            }
            finally
            {
                if (!made)
                {
                    try { Directory.Delete(target, false); } // the empty folder that was to become the link
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                }
            }
        }

        private static IOException Problem(string verb, string path, int error)
        {
            string why = error == ERROR_PRIVILEGE_NOT_HELD
                ? "Windows lets only an administrator (or a computer in Developer Mode) make a symbolic link"
                : new Win32Exception(error).Message;
            return new IOException("Cannot " + verb + " \"" + path + "\": " + why + ".");
        }
    }
}
