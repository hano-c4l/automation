using System;
using System.IO;
using System.Threading;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>The File/Folder actions, always inside a folder of their own under the temp folder.</summary>
    internal static class FileSuite
    {
        private static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "at_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        [Suite("files")]
        public static void Files(T t)
        {
            string root = NewRoot();
            try
            {
                string a = Path.Combine(root, "a.txt");

                // write, read, append
                FileActions.Write(a, "hello", false);
                t.Eq("read", FileActions.Read(a), "hello");
                FileActions.Write(a, " world", true);
                t.Eq("append", FileActions.Read(a), "hello world");
                FileActions.Write(a, "replaced", false);
                t.Eq("write replaces", FileActions.Read(a), "replaced");
                t.True("exists (file)", FileActions.Exists(a));
                t.False("exists (missing)", FileActions.Exists(Path.Combine(root, "nope")));
                t.Throws("read missing", () => FileActions.Read(Path.Combine(root, "nope")), "does not exist");
                t.Throws("write into a folder", () => FileActions.Write(root, "x", false), "is a folder");

                // copy a file: into a folder, with and without overwrite, parents are made
                string sub = Path.Combine(root, "sub");
                FileActions.CreateFolder(sub);
                FileActions.Copy(a, sub, false);
                t.True("copy into an existing folder", File.Exists(Path.Combine(sub, "a.txt")));
                t.Throws("copy onto an existing file", () => FileActions.Copy(a, sub, false), "exists already");
                File.WriteAllText(a, "v2");
                FileActions.Copy(a, sub + "\\", true);
                t.Eq("copy with overwrite", File.ReadAllText(Path.Combine(sub, "a.txt")), "v2");
                FileActions.Copy(a, Path.Combine(root, "deep", "er", "b.txt"), false);
                t.True("copy makes the folders above", File.Exists(Path.Combine(root, "deep", "er", "b.txt")));
                t.Throws("copy onto itself", () => FileActions.Copy(a, a, true), "same as the source");
                t.Throws("copy a missing file", () => FileActions.Copy(Path.Combine(root, "zzz"), sub, false), "does not exist");

                // copy a folder
                string tree = Path.Combine(root, "tree");
                Directory.CreateDirectory(Path.Combine(tree, "x", "y"));
                File.WriteAllText(Path.Combine(tree, "x", "y", "f.txt"), "deepfile");
                FileActions.Copy(tree, Path.Combine(root, "tree2"), false);
                t.Eq("folder copy is deep", File.ReadAllText(Path.Combine(root, "tree2", "x", "y", "f.txt")), "deepfile");
                t.Throws("folder into itself", () => FileActions.Copy(tree, Path.Combine(tree, "x", "inner"), false), "into itself");
                FileActions.Copy(tree, Path.Combine(root, "tree2"), false);   // the existing folder means "inside it": tree2\tree
                t.True("copy into an existing folder puts it inside", Directory.Exists(Path.Combine(root, "tree2", "tree", "x", "y")));
                t.Throws("folder exists without overwrite", () => FileActions.Copy(tree, Path.Combine(root, "tree2"), false), "exists already");
                FileActions.Copy(tree, Path.Combine(root, "tree2"), true);    // merge
                t.True("folder merge with overwrite", Directory.Exists(Path.Combine(root, "tree2", "tree")));

                // move
                string m = Path.Combine(root, "m.txt");
                File.WriteAllText(m, "m");
                FileActions.Move(m, Path.Combine(root, "moved", "m2.txt"), false);
                t.True("move a file", File.Exists(Path.Combine(root, "moved", "m2.txt")) && !File.Exists(m));
                File.WriteAllText(m, "again");
                t.Throws("move onto an existing file", () => FileActions.Move(m, Path.Combine(root, "moved", "m2.txt"), false), "exists already");
                FileActions.Move(m, Path.Combine(root, "moved", "m2.txt"), true);
                t.Eq("move with overwrite", File.ReadAllText(Path.Combine(root, "moved", "m2.txt")), "again");
                FileActions.Move(Path.Combine(root, "tree2"), Path.Combine(root, "tree3"), false);
                t.True("move a folder", Directory.Exists(Path.Combine(root, "tree3", "x", "y")) && !Directory.Exists(Path.Combine(root, "tree2")));
                t.Throws("move a folder into itself", () => FileActions.Move(tree, Path.Combine(tree, "x", "inner"), false), "into itself");

                // rename
                FileActions.Rename(Path.Combine(root, "tree3"), "tree4");
                t.True("rename a folder", Directory.Exists(Path.Combine(root, "tree4")));
                t.Throws("rename with a path", () => FileActions.Rename(Path.Combine(root, "tree4"), "a\\b"), "not a valid name");
                t.Throws("rename onto an existing name", () => FileActions.Rename(Path.Combine(root, "tree4"), "tree"), "exists already");

                // list
                t.Eq("list (top level)", FileActions.List(tree, false).Rows, 1);
                t.Eq("list (recursive)", FileActions.List(tree, true).Rows, 3);
                t.Eq("list gives full paths", FileActions.List(tree, false).Get(1, 1), Path.Combine(tree, "x"));
                t.Throws("list a missing folder", () => FileActions.List(Path.Combine(root, "nope"), false), "does not exist");

                // delete and its guards
                t.Throws("delete a folder that is not empty", () => FileActions.Delete(tree, false, false), "not empty");
                t.Throws("delete a whole drive", () => FileActions.Delete("C:\\", true, true), "whole drive");
                string ro = Path.Combine(root, "ro.txt");
                File.WriteAllText(ro, "r");
                File.SetAttributes(ro, FileAttributes.ReadOnly);
                t.Throws("delete a read-only file without Force", () => FileActions.Delete(ro, false, false), "cannot delete");
                t.True("delete a read-only file with Force", FileActions.Delete(ro, false, true));
                Directory.CreateDirectory(Path.Combine(root, "rotree"));
                string rof = Path.Combine(root, "rotree", "f.txt");
                File.WriteAllText(rof, "x");
                File.SetAttributes(rof, FileAttributes.ReadOnly);
                t.True("delete a tree with a read-only file (Recurse + Force)", FileActions.Delete(Path.Combine(root, "rotree"), true, true));
                t.False("that tree is gone", Directory.Exists(Path.Combine(root, "rotree")));
                t.False("delete where nothing is is not an error", FileActions.Delete(Path.Combine(root, "nope"), true, true));
                t.True("delete a folder with Recurse", FileActions.Delete(tree, true, false));

                // create
                FileActions.CreateFolder(Path.Combine(root, "c1", "c2"));
                t.True("create makes the folders above", Directory.Exists(Path.Combine(root, "c1", "c2")));
                t.Throws("create where a file is", () => FileActions.CreateFolder(a), "is a file");

                // paths: environment variables and empty paths
                FileActions.Write("%TEMP%\\" + Path.GetFileName(root) + "\\env.txt", "e", false);
                t.True("environment variable in a path", File.Exists(Path.Combine(root, "env.txt")));
                t.Throws("empty path", () => FileActions.Exists(""), "Enter the path");
            }
            finally { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        [Suite("files.in-a-script")]
        public static void InAScript(T t)
        {
            string root = NewRoot();
            try
            {
                var runner = new ScriptRunner(new MouseController());
                var script = new AutomationScript();
                script.Actions.Add(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsWrite, Value = root + "\\r.txt", Target = "\"line \" + 3", TargetIsExpression = true }));
                script.Actions.Add(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsRead, Value = root + "\\r.txt", Id = "txt" }));
                script.Actions.Add(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsExists, Value = "{p}", ValueIsExpression = true, Id = "ok" }));
                script.Actions.Add(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsList, Value = root, Id = "files" }));
                runner.Run(script.Actions, new System.Collections.Generic.Dictionary<string, object> { { "p", root + "\\r.txt" } }, CancellationToken.None);

                object v;
                runner.Variables.TryGet("txt", out v);
                t.Eq("Read puts the text in the variable", v, "line 3");
                runner.Variables.TryGet("ok", out v);
                t.Eq("Exists with an expression path", v, true);
                runner.Variables.TryGet("files", out v);
                t.Eq("List puts a table in the variable", ((TableValue)v).Rows, 1);

                // an unknown kind (a newer file in an older build) must not turn into Open
                var unknown = new AutomationScript();
                unknown.Actions.Add(ActionNode.NewCommand(new CommandSettings { Kind = (CommandKind)99 }));
                t.Throws("unknown Utilities kind", () => new ScriptRunner(new MouseController()).Run(unknown, CancellationToken.None), "not known");
            }
            finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        }
    }
}
