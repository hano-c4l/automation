using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AutomationTest.Core.Model;

namespace AutomationTest.App
{
    /// <summary>
    /// The log of one run (Settings, Log = ON): one text file in the Logs folder next to the program, named after the time, the
    /// script and the kind of run. It says which action started and when, what the runner noted (such as a Try that caught an
    /// error) and how the run ended. It never writes the values an action works with (paths, text, variables); only what the
    /// runner and Windows say in messages, which can name a file or a window.
    /// </summary>
    internal sealed class RunLog : IDisposable
    {
        private readonly object _lock = new object();
        private readonly Dictionary<ActionNode, string> _paths = new Dictionary<ActionNode, string>();
        private readonly List<string> _secrets = new List<string>();
        private StreamWriter _writer;
        private readonly DateTime _started = DateTime.Now;

        /// <summary>The file this run writes to.</summary>
        public string FilePath { get; private set; }

        /// <summary>Why the file could not be made or written (null when all is well).</summary>
        public string Problem { get; private set; }

        /// <summary>The Logs folder: next to the program, like Setting.ini.</summary>
        public static string Folder
        {
            get { return Path.Combine(Path.GetDirectoryName(IniFile.DefaultPath) ?? "", "Logs"); }
        }

        /// <summary>Starts the log of a run, or returns null with the reason in <paramref name="problem"/>.</summary>
        public static RunLog Start(string folder, string scriptName, bool selected, List<ActionNode> tree, bool randomTime, out string problem)
        {
            var log = new RunLog();
            try
            {
                Directory.CreateDirectory(folder);
                // Two runs of the same script within one second get _2, _3... instead of a clash.
                string name = FileName(DateTime.Now, scriptName, selected);
                for (int attempt = 1; ; attempt++)
                {
                    string candidate = attempt == 1 ? name : Path.GetFileNameWithoutExtension(name) + "_" + attempt + ".log";
                    log.FilePath = Path.Combine(folder, candidate);
                    try
                    {
                        log._writer = new StreamWriter(new FileStream(log.FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                        break;
                    }
                    catch (IOException) when (File.Exists(log.FilePath) && attempt < 50) { /* taken: try the next number */ }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is ArgumentException)
            {
                problem = "The log could not be made: " + ex.Message;
                return null;
            }
            log.Number(tree, "");
            log.Line("Run started: " + (selected ? "Start Selected" : "Start") + ", script " + (string.IsNullOrWhiteSpace(scriptName) ? "(unsaved)" : scriptName)
                + ", " + (tree == null ? 0 : tree.Count) + " action(s) at the top level" + (randomTime ? ", Random Time on" : ""));
            problem = null;
            return log;
        }

        /// <summary>The name of a log file: 20261115_013045_MyScript.log (or _MyScript_selected.log for Start Selected).</summary>
        public static string FileName(DateTime now, string scriptName, bool selected)
        {
            string name = string.IsNullOrWhiteSpace(scriptName) ? "unsaved" : scriptName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length > 60) name = name.Substring(0, 60);
            return now.ToString("yyyyMMdd_HHmmss") + "_" + name + (selected ? "_selected" : "") + ".log";
        }

        /// <summary>Text to hide wherever it would appear in the log (a password, later on).</summary>
        public void AddSecret(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_lock) _secrets.Add(text);
        }

        private void Number(List<ActionNode> nodes, string prefix)
        {
            if (nodes == null) return;
            int i = 1;
            foreach (var node in nodes)
            {
                string path = prefix + i++;
                _paths[node] = path;
                Number(node.Children, path + ".");
            }
        }

        /// <summary>An action starts: its place in the script (1, 2.1, 2.2...) and what kind of action it is; no values.</summary>
        public void Node(ActionNode node)
        {
            string path;
            if (_paths.TryGetValue(node, out path)) Line("Action " + path + ": " + Describe(node));
            else Line("Action (in an Auto Script): " + Describe(node)); // its place is in the other script, not in this one
        }

        /// <summary>The kind of action, and its name when it has one; never the values in its fields.</summary>
        public static string Describe(ActionNode node)
        {
            string kind;
            switch (node.Category)
            {
                case ActionCategory.Mouse: kind = "Mouse " + node.Mouse.Action; break;
                case ActionCategory.Keyboard: kind = "Keyboard " + node.Keyboard.Action; break;
                case ActionCategory.Utilities:
                case ActionCategory.Program: kind = ActionGroups.Display(node.Command.Kind); break;
                case ActionCategory.Web: kind = ActionGroups.Display(node.Web.Kind); break;
                case ActionCategory.Excel: kind = ActionGroups.Display(node.Excel.Kind); break;
                default: kind = node.Control.ToString(); break;
            }
            return string.IsNullOrWhiteSpace(node.Name) ? kind : kind + " \"" + node.Name.Trim() + "\"";
        }

        public void Note(string text)
        {
            Line("Note: " + text);
        }

        /// <summary>The run is over: how it ended.</summary>
        public void End(string how)
        {
            Line("Run " + how + " after " + (DateTime.Now - _started).TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s");
        }

        public void Line(string text)
        {
            lock (_lock)
            {
                if (_writer == null) return;
                foreach (string secret in _secrets) text = text.Replace(secret, "****");
                try { _writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text); }
                catch (IOException ex) { Problem = ex.Message; _writer = null; } // a full disk must not stop the run
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_writer == null) return;
                try { _writer.Dispose(); } catch (IOException) { }
                _writer = null;
            }
        }
    }
}
