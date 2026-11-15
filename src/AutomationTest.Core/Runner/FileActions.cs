using System;
using System.IO;
using System.Linq;
using System.Text;
using AutomationTest.Core.Expressions;

namespace AutomationTest.Core.Runner
{
    /// <summary>
    /// The File/Folder actions. Each one works on a file or a folder, whichever is at the path. Every failure is an
    /// <see cref="InvalidOperationException"/> that says what went wrong and with which path.
    /// </summary>
    public static class FileActions
    {
        /// <summary>The full path of a path as typed: environment variables are expanded and a relative path is taken from the current folder.</summary>
        public static string FullPath(string path, string what)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Enter the " + what + ".");
            try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { throw new InvalidOperationException("\"" + path.Trim() + "\" is not a valid " + what + ": " + ex.Message); }
        }

        private static bool IsDirectory(string path) { return Directory.Exists(path); }

        private static string Trimmed(string path)
        {
            string root = Path.GetPathRoot(path);
            return path.Length > root.Length ? path.TrimEnd('\\', '/') : path;
        }

        private static void RequireSource(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw new InvalidOperationException("\"" + path + "\" does not exist.");
        }

        /// <summary>The full path a Copy or Move puts the item at: an existing folder (or a path ending with \) means "inside it, same name".</summary>
        private static string Destination(string source, string destination)
        {
            string text = Environment.ExpandEnvironmentVariables((destination ?? "").Trim());
            bool into = text.EndsWith("\\") || text.EndsWith("/");
            string full = FullPath(destination, "destination");
            if (into || Directory.Exists(full)) return Path.Combine(Trimmed(full), Path.GetFileName(Trimmed(source)));
            return Trimmed(full);
        }

        private static bool Inside(string folder, string path)
        {
            string a = Trimmed(folder) + "\\", b = Trimmed(path) + "\\";
            return b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        public static void Copy(string source, string destination, bool overwrite)
        {
            source = Trimmed(FullPath(source, "path"));
            RequireSource(source);
            string target = Destination(source, destination);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The destination is the same as the source (" + source + ").");
            try
            {
                if (IsDirectory(source))
                {
                    if (Inside(source, target)) throw new InvalidOperationException("Cannot copy the folder \"" + source + "\" into itself (" + target + ").");
                    if (Directory.Exists(target) && !overwrite) throw new InvalidOperationException("The folder \"" + target + "\" exists already (turn on Overwrite to merge into it).");
                    if (File.Exists(target)) throw new InvalidOperationException("\"" + target + "\" is a file, so the folder cannot be copied there.");
                    CopyFolder(source, target, overwrite);
                }
                else
                {
                    if (Directory.Exists(target)) throw new InvalidOperationException("\"" + target + "\" is a folder, so the file cannot be copied there.");
                    if (File.Exists(target) && !overwrite) throw new InvalidOperationException("\"" + target + "\" exists already (turn on Overwrite to replace it).");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target, overwrite);
                }
            }
            catch (Exception ex) when (IsIoError(ex)) { throw Wrap("copy", source, ex); }
        }

        private static void CopyFolder(string source, string target, bool overwrite)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite);
            foreach (string folder in Directory.GetDirectories(source))
                CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)), overwrite);
        }

        public static void Move(string source, string destination, bool overwrite)
        {
            source = Trimmed(FullPath(source, "path"));
            RequireSource(source);
            string target = Destination(source, destination);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The destination is the same as the source (" + source + ").");
            try
            {
                if (IsDirectory(source))
                {
                    if (Inside(source, target)) throw new InvalidOperationException("Cannot move the folder \"" + source + "\" into itself (" + target + ").");
                    if (File.Exists(target)) throw new InvalidOperationException("\"" + target + "\" is a file, so the folder cannot be moved there.");
                    bool sameVolume = string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);
                    if (Directory.Exists(target))
                    {
                        if (!overwrite) throw new InvalidOperationException("The folder \"" + target + "\" exists already (turn on Overwrite to merge into it).");
                        CopyFolder(source, target, true); // merged; the source goes away afterwards
                        Directory.Delete(source, true);
                        return;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (sameVolume) Directory.Move(source, target);
                    else { CopyFolder(source, target, false); Directory.Delete(source, true); }
                }
                else
                {
                    if (Directory.Exists(target)) throw new InvalidOperationException("\"" + target + "\" is a folder, so the file cannot be moved there.");
                    if (File.Exists(target))
                    {
                        if (!overwrite) throw new InvalidOperationException("\"" + target + "\" exists already (turn on Overwrite to replace it).");
                        File.Delete(target);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Move(source, target);
                }
            }
            catch (Exception ex) when (IsIoError(ex)) { throw Wrap("move", source, ex); }
        }

        public static void Rename(string source, string newName)
        {
            source = Trimmed(FullPath(source, "path"));
            RequireSource(source);
            string name = (newName ?? "").Trim();
            if (name.Length == 0) throw new InvalidOperationException("Enter the new name.");
            if (name.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException("\"" + name + "\" is not a valid name (a name has no path, and none of \\ / : * ? \" < > |). To move it somewhere else use Move.");
            string target = Path.Combine(Path.GetDirectoryName(source), name);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(target) || Directory.Exists(target)) throw new InvalidOperationException("\"" + target + "\" exists already.");
            try
            {
                if (IsDirectory(source)) Directory.Move(source, target); else File.Move(source, target);
            }
            catch (Exception ex) when (IsIoError(ex)) { throw Wrap("rename", source, ex); }
        }

        /// <summary>Deletes the file or folder; false when there was nothing at the path.</summary>
        public static bool Delete(string path, bool recurse, bool force)
        {
            string full = Trimmed(FullPath(path, "path"));
            if (string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to delete a whole drive (" + full + ").");
            try
            {
                if (Directory.Exists(full))
                {
                    if (!recurse && Directory.EnumerateFileSystemEntries(full).Any())
                        throw new InvalidOperationException("The folder \"" + full + "\" is not empty (turn on Recurse to delete it with everything inside).");
                    if (force) ClearReadOnly(full);
                    Directory.Delete(full, recurse);
                    return true;
                }
                if (File.Exists(full))
                {
                    if (force) File.SetAttributes(full, FileAttributes.Normal);
                    File.Delete(full);
                    return true;
                }
                return false;
            }
            catch (Exception ex) when (IsIoError(ex)) { throw Wrap("delete", full, ex); }
        }

        private static void ClearReadOnly(string folder)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
                File.SetAttributes(entry, FileAttributes.Normal);
            File.SetAttributes(folder, FileAttributes.Directory);
        }

        public static void CreateFolder(string path)
        {
            string full = Trimmed(FullPath(path, "path"));
            if (File.Exists(full)) throw new InvalidOperationException("\"" + full + "\" is a file, so a folder cannot be made there.");
            try { Directory.CreateDirectory(full); }
            catch (Exception ex) when (IsIoError(ex)) { throw Wrap("create the folder", full, ex); }
        }

        public static bool Exists(string path)
        {
            string full = Trimmed(FullPath(path, "path"));
            return File.Exists(full) || Directory.Exists(full);
        }

        public static string Read(string path)
        {
            string full = FullPath(path, "path");
            if (!File.Exists(full)) throw new InvalidOperationException("The file \"" + full + "\" does not exist.");
            try { return File.ReadAllText(full); }
            catch (Exception ex) when (IsIoError(ex)) { throw Wrap("read", full, ex); }
        }

        public static void Write(string path, string text, bool append)
        {
            string full = FullPath(path, "path");
            if (Directory.Exists(full)) throw new InvalidOperationException("\"" + full + "\" is a folder, not a file.");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                var utf8 = new UTF8Encoding(false);
                if (append) File.AppendAllText(full, text ?? "", utf8); else File.WriteAllText(full, text ?? "", utf8);
            }
            catch (Exception ex) when (IsIoError(ex)) { throw Wrap("write", full, ex); }
        }

        /// <summary>The files and folders inside a folder, as a table of one column with their full paths.</summary>
        public static TableValue List(string path, bool recurse)
        {
            string full = Trimmed(FullPath(path, "folder"));
            if (!Directory.Exists(full)) throw new InvalidOperationException("The folder \"" + full + "\" does not exist.");
            try
            {
                string[] entries = Directory.EnumerateFileSystemEntries(full, "*", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray();
                var cells = new object[entries.Length, 1];
                for (int i = 0; i < entries.Length; i++) cells[i, 0] = entries[i];
                return new TableValue(cells);
            }
            catch (Exception ex) when (IsIoError(ex)) { throw Wrap("list", full, ex); }
        }

        private static bool IsIoError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is NotSupportedException || ex is ArgumentException;
        }

        private static InvalidOperationException Wrap(string verb, string path, Exception ex)
        {
            return new InvalidOperationException("Cannot " + verb + " \"" + path + "\": " + ex.Message);
        }
    }
}
