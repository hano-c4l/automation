using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>
    /// File/Folder with a folder that is a link (a junction): Copy makes the same link, Delete and Force never reach what the link
    /// shows, List names it without going in. Everything is in a folder of the test's own under the temp folder.
    /// </summary>
    internal static class FileLinkSuite
    {
        private static bool MakeJunction(string link, string target)
        {
            var info = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(info))
            {
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 && Directory.Exists(link);
            }
        }

        private static bool IsLink(string path)
        {
            return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        [Suite("files.links")]
        public static void Links(T t)
        {
            string root = Path.Combine(Path.GetTempPath(), "at_link_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                // outside\keep.txt (read-only) is what the link shows; project\link is the link; project\loop leads back to project
                string outside = Path.Combine(root, "outside");
                string project = Path.Combine(root, "project");
                Directory.CreateDirectory(outside);
                Directory.CreateDirectory(Path.Combine(project, "real"));
                string keep = Path.Combine(outside, "keep.txt");
                File.WriteAllText(keep, "outside");
                File.SetAttributes(keep, FileAttributes.ReadOnly);
                File.WriteAllText(Path.Combine(project, "real", "f.txt"), "inside");
                if (!MakeJunction(Path.Combine(project, "link"), outside) || !MakeJunction(Path.Combine(project, "loop"), project))
                {
                    t.Fail("a junction could not be made (mklink /J), so the links cannot be tested");
                    return;
                }

                // what counts as a link: only a junction or a symbolic link (by its reparse tag), never an ordinary folder or a file
                t.True("a junction is a link", FolderLink.IsLink(Path.Combine(project, "link")));
                t.True("also written with a \\ at the end", FolderLink.IsLink(Path.Combine(project, "link") + "\\"));
                t.False("an ordinary folder is not", FolderLink.IsLink(Path.Combine(project, "real")));
                t.False("a file is not", FolderLink.IsLink(keep));
                t.False("nothing there is not", FolderLink.IsLink(Path.Combine(root, "nowhere")));

                // list: the link is named, what it shows is not; the loop does not go round
                var listed = FileActions.List(project, true);
                var names = Enumerable.Range(1, listed.Rows).Select(i => (string)listed.Get(i, 1)).ToList();
                t.Eq("list (recursive) names the two links, the folder and its file", names.Count, 4);
                t.True("the link itself is listed", names.Contains(Path.Combine(project, "link")));
                t.False("what the link shows is not listed", names.Any(n => n.EndsWith("keep.txt")));

                // copy: the links are made again as links to the same places; nothing outside is copied; the loop does not go round
                string copy = Path.Combine(root, "copy");
                FileActions.Copy(project, copy, false);
                t.Eq("the ordinary file is copied", File.ReadAllText(Path.Combine(copy, "real", "f.txt")), "inside");
                t.True("the link is copied as a link", IsLink(Path.Combine(copy, "link")));
                t.True("the copy of the link shows the same place", File.Exists(Path.Combine(copy, "link", "keep.txt")));
                t.True("the link that leads back is a link too", IsLink(Path.Combine(copy, "loop")));
                t.True("and still leads to the first folder, not to the copy", File.Exists(Path.Combine(copy, "loop", "real", "f.txt")) && !Directory.Exists(Path.Combine(copy, "loop", "loop", "loop", "copy")));
                File.WriteAllText(Path.Combine(outside, "later.txt"), "added later");
                t.True("a file added at the place shows through the copied link", File.Exists(Path.Combine(copy, "link", "later.txt")));

                // copy again with Overwrite: the link is replaced by the link, the place is untouched
                FileActions.Copy(project, copy + "\\..\\copy2\\..\\copy2", false);
                // (the destination is the folder that holds it: an existing folder means "inside it, same name")
                t.Throws("a link at the destination without Overwrite", () => FileActions.Copy(Path.Combine(project, "link"), copy, false), "exists already");
                FileActions.Copy(Path.Combine(project, "link"), copy, true);
                t.False("the link was replaced, not entered: nothing new at the place", Directory.Exists(Path.Combine(outside, "link")));
                t.True("with Overwrite the link is made again", IsLink(Path.Combine(copy, "link")) && File.Exists(keep));

                // a link copied on its own
                FileActions.Copy(Path.Combine(project, "link"), Path.Combine(root, "alone", "mylink"), false);
                t.True("a link copied on its own is a link", IsLink(Path.Combine(root, "alone", "mylink")) && File.Exists(Path.Combine(root, "alone", "mylink", "keep.txt")));

                // delete a link: only the link goes, no Recurse needed although the place is not empty
                t.True("delete a link", FileActions.Delete(Path.Combine(root, "alone", "mylink"), false, false));
                t.False("the link is gone", Directory.Exists(Path.Combine(root, "alone", "mylink")));
                t.True("what it showed stays", File.Exists(keep));

                // delete a folder with links in it, Recurse + Force: the place keeps its files and their read-only mark
                t.True("delete a folder that has links (Recurse + Force)", FileActions.Delete(copy, true, true));
                t.False("the folder is gone", Directory.Exists(copy));
                t.True("the file at the place is still there", File.Exists(keep) && File.Exists(Path.Combine(outside, "later.txt")));
                t.True("and Force did not take its read-only mark", (File.GetAttributes(keep) & FileAttributes.ReadOnly) != 0);
                t.True("the folder the loop led to is whole", File.Exists(Path.Combine(project, "real", "f.txt")));

                // move a folder with a link onto an existing folder (merge): the link arrives as a link, the place is untouched
                string merged = Path.Combine(root, "merged");
                Directory.CreateDirectory(merged);
                FileActions.Move(Path.Combine(root, "copy2"), merged + "\\x", false);
                FileActions.Copy(project, Path.Combine(root, "again"), false);
                Directory.CreateDirectory(Path.Combine(root, "target"));
                FileActions.Move(Path.Combine(root, "again"), Path.Combine(root, "target"), false); // into it: target\again
                t.True("a moved folder keeps its link", IsLink(Path.Combine(root, "target", "again", "link")));
                t.True("the place is still whole after the moves", File.Exists(keep));
            }
            finally
            {
                // Links first and one by one, so the cleaning itself can never reach what a link shows.
                try
                {
                    foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly)) Clean(dir);
                    Directory.Delete(root, false);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void Clean(string folder)
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(folder, false); return; }
            foreach (string file in Directory.GetFiles(folder)) { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            foreach (string sub in Directory.GetDirectories(folder)) Clean(sub);
            Directory.Delete(folder, false);
        }
    }
}
