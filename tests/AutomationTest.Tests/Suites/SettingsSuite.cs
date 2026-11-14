using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AutomationTest.App;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>The Settings Log, Message and Random Time: the options, the log file, the notice and the random extra time.</summary>
    internal static class SettingsSuite
    {
        private static string TempFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "at_log_" + Guid.NewGuid().ToString("N"));
            return folder;
        }

        [Suite("settings.options")]
        public static void Options(T t)
        {
            // the defaults are all off
            var defaults = AppSettings.Defaults();
            t.False("Log is off by default", defaults.LogRuns);
            t.False("Message is off by default", defaults.NotifyEnd);
            t.False("Random Time is off by default", defaults.RandomTime);

            // a Setting.ini from before these options has none of the keys: still off
            string path = Path.Combine(Path.GetTempPath(), "at_ini_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                var empty = IniFile.Load(path);
                var loaded = AppSettings.Load(empty);
                t.False("a file without the keys: Log off", loaded.LogRuns);
                t.False("a file without the keys: Message off", loaded.NotifyEnd);
                t.False("a file without the keys: Random Time off", loaded.RandomTime);

                // saved and read back
                var on = AppSettings.Defaults();
                on.LogRuns = true; on.NotifyEnd = true; on.RandomTime = true;
                on.Save(empty);
                empty.Save(path);
                var again = AppSettings.Load(IniFile.Load(path));
                t.True("Log kept", again.LogRuns);
                t.True("Message kept", again.NotifyEnd);
                t.True("Random Time kept", again.RandomTime);
                t.Eq("the file uses the keys Log, Message, RandomTime", IniFile.Load(path).Get("Options", "Log") + IniFile.Load(path).Get("Options", "Message") + IniFile.Load(path).Get("Options", "RandomTime"), "111");
                t.Eq("the shortcut keys are not disturbed", again.Start, AppSettings.DefaultStart);
            }
            finally { try { File.Delete(path); } catch (IOException) { } }

            // the dialog hands the three options back
            var form = new SettingsForm(AppSettings.Defaults());
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var name in new[] { "_log", "_message", "_randomTime" })
                ((ToggleSwitch)typeof(SettingsForm).GetField(name, flags).GetValue(form)).Checked = true;
            typeof(SettingsForm).GetMethod("Save", flags).Invoke(form, null);
            t.True("the dialog returns Log", form.Result != null && form.Result.LogRuns);
            t.True("the dialog returns Message", form.Result != null && form.Result.NotifyEnd);
            t.True("the dialog returns Random Time", form.Result != null && form.Result.RandomTime);
        }

        [Suite("settings.log")]
        public static void Log(T t)
        {
            string folder = TempFolder();
            try
            {
                var secretText = ActionNode.NewKeyboard(new KeyboardSettings { Action = KeyboardAction.Text, Value = "secret-value-123" });
                secretText.Name = "typing";
                var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 2 });
                var inner = ActionNode.NewVariable(new VariableSettings { Id = "a", Text = "42" });
                loop.Children.Add(inner);
                var tree = new List<ActionNode> { secretText, loop };

                string problem;
                var log = RunLog.Start(folder, "My Script", false, tree, true, out problem);
                t.True("the log was made", log != null && problem == null);
                if (log == null) return;
                log.Node(secretText);
                log.Node(loop);
                log.Node(inner);
                log.AddSecret("hunter2");
                log.Note("Caught: the password hunter2 was wrong");
                log.End("finished");
                log.Dispose();

                string text = File.ReadAllText(log.FilePath);
                t.True("it is in the Logs folder with the script name", Path.GetFileName(log.FilePath).EndsWith("_My Script.log") && Path.GetDirectoryName(log.FilePath) == folder);
                t.True("the start line says what ran", text.Contains("Run started: Start, script My Script, 2 action(s) at the top level, Random Time on"));
                t.True("an action line has its place, kind and name", text.Contains("Action 1: Keyboard Text \"typing\""));
                t.True("a nested action has a dotted place", text.Contains("Action 2.1: Variable"));
                t.False("the text that is typed is never written", text.Contains("secret-value-123"));
                t.False("a secret is hidden", text.Contains("hunter2"));
                t.True("and shown as stars", text.Contains("the password **** was wrong"));
                t.True("the end line says how it ended", text.Contains("Run finished after"));
                t.True("every line has a time", System.Text.RegularExpressions.Regex.IsMatch(text.Split('\n')[0], @"^\d\d:\d\d:\d\d\.\d\d\d  "));

                // a second run of the same script in the same second gets its own file
                var first = RunLog.Start(folder, "Twin", false, tree, false, out problem);
                var second = RunLog.Start(folder, "Twin", false, tree, false, out problem);
                t.True("two logs of the same name get two files", first != null && second != null && first.FilePath != second.FilePath);
                first?.Dispose(); second?.Dispose();

                // Start Selected is marked in the name; an unsaved script has a name too
                string selectedName = RunLog.FileName(new DateTime(2026, 11, 15, 1, 30, 45), "Do: it?", true);
                t.Eq("the file name of a Start Selected run", selectedName, "20261115_013045_Do_ it__selected.log");
                t.Eq("the file name of an unsaved script", RunLog.FileName(new DateTime(2026, 11, 15, 1, 30, 45), null, false), "20261115_013045_unsaved.log");

                // a folder that cannot be made does not stop anything: no log and a reason
                string nowhere = Path.Combine(folder, "a.txt", "inside");
                File.WriteAllText(Path.Combine(folder, "a.txt"), "x");
                var none = RunLog.Start(nowhere, "x", false, tree, false, out problem);
                t.True("an impossible folder gives no log and a reason", none == null && !string.IsNullOrEmpty(problem));
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        [Suite("settings.message")]
        public static void Message(T t)
        {
            string title, text;
            ToastForm.Describe(RunOutcome.Finished, null, out title, out text);
            t.True("a finished notice has a title and a default text", title.Contains("finished") && text.Length > 0);
            ToastForm.Describe(RunOutcome.Failed, "The window is covered.", out title, out text);
            t.True("an error notice carries the message", title.Contains("error") && text == "The window is covered.");
            ToastForm.Describe(RunOutcome.Failed, new string('x', 500), out title, out text);
            t.True("a long message is cut", text.Length <= 220 && text.EndsWith("..."));
        }

        [Suite("settings.random-time")]
        public static void RandomTime(T t)
        {
            var runner = new ScriptRunner(new MouseController()) { RandomTime = true };
            long min = long.MaxValue, max = 0;
            for (int i = 0; i < 2000; i++)
            {
                long v = runner.Jitter(1000);
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            t.True("never less than 10 percent more (" + min + ")", min >= 1100);
            t.True("never more than 20 percent more (" + max + ")", max <= 1200);
            t.True("the extra really varies (" + min + ".." + max + ")", max - min > 50);
            t.Eq("zero stays zero", runner.Jitter(0), 0L);
            t.Eq("a negative time is left alone", runner.Jitter(-5), -5L);
            runner.RandomTime = false;
            t.Eq("off: the time is as set", runner.Jitter(1000), 1000L);

            // a real action: the Delay of a File/Folder action is longer when Random Time is on, and exact when it is off
            var action = ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsExists, Value = Path.GetTempPath(), Id = "x", DelayMs = 1000 });
            var plain = new ScriptRunner(new MouseController());
            var watch = Stopwatch.StartNew();
            plain.Run(new List<ActionNode> { action }, null, CancellationToken.None);
            long off = watch.ElapsedMilliseconds;
            var random = new ScriptRunner(new MouseController()) { RandomTime = true };
            watch.Restart();
            random.Run(new List<ActionNode> { action }, null, CancellationToken.None);
            long on = watch.ElapsedMilliseconds;
            t.True("off: the Delay is about 1000 ms (" + off + ")", off >= 950 && off < 1090);
            t.True("on: the Delay is 1100 to 1200 ms plus a little (" + on + ")", on >= 1090 && on < 1350);

            // the number the list shows is still the one that was set
            var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 1 });
            loop.Children.Add(action);
            t.Eq("the Time column does not change with Random Time", ActionTiming.PerIterationMs(loop), 1000L);
        }
    }
}
