using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using AutomationTest.App;

namespace AutomationTest.Tests.Suites
{
    /// <summary>Settings, Clear Log (old log files go when the program starts) and the report of an error nothing else handled.</summary>
    internal static class ClearLogSuite
    {
        private static string TempFolder(string what)
        {
            string folder = Path.Combine(Path.GetTempPath(), "at_" + what + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string Make(string folder, string name, DateTime written)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTime(path, written);
            return path;
        }

        [Suite("settings.clear-log")]
        public static void ClearLog(T t)
        {
            // the option: 7 days by default, kept in Setting.ini, a bad number is the default
            t.Eq("Clear Log is 7 days by default", AppSettings.Defaults().ClearLogDays, 7);
            string ini = Path.Combine(Path.GetTempPath(), "at_ini_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                var file = IniFile.Load(ini);
                t.Eq("a file without the key: 7", AppSettings.Load(file).ClearLogDays, 7);
                var edited = AppSettings.Defaults();
                edited.ClearLogDays = 30;
                edited.Save(file);
                file.Save(ini);
                t.Eq("kept", AppSettings.Load(IniFile.Load(ini)).ClearLogDays, 30);
                t.Eq("under the key ClearLog", IniFile.Load(ini).Get("Options", "ClearLog"), "30");
                foreach (string bad in new[] { "0", "-3", "abc", "99999" })
                {
                    file.Set("Options", "ClearLog", bad);
                    t.Eq("\"" + bad + "\" in the file gives 7", AppSettings.Load(file).ClearLogDays, 7);
                }
            }
            finally { try { File.Delete(ini); } catch (IOException) { } }

            // the API answer limit: 50 MB by default, kept, a bad number is 50
            t.Eq("the API answer limit is 50 MB by default", AppSettings.Defaults().ApiAnswerLimitMb, 50);
            string ini2 = Path.Combine(Path.GetTempPath(), "at_ini_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                var file = IniFile.Load(ini2);
                t.Eq("a file without the key: 50", AppSettings.Load(file).ApiAnswerLimitMb, 50);
                var edited = AppSettings.Defaults();
                edited.ApiAnswerLimitMb = 200;
                edited.Save(file);
                file.Save(ini2);
                t.Eq("the limit is kept", AppSettings.Load(IniFile.Load(ini2)).ApiAnswerLimitMb, 200);
                t.Eq("under the key ApiAnswerLimitMB", IniFile.Load(ini2).Get("Options", "ApiAnswerLimitMB"), "200");
                foreach (string bad in new[] { "0", "-1", "x", "501" })
                {
                    file.Set("Options", "ApiAnswerLimitMB", bad);
                    t.Eq("\"" + bad + "\" in the file gives 50", AppSettings.Load(file).ApiAnswerLimitMb, 50);
                }
            }
            finally { try { File.Delete(ini2); } catch (IOException) { } }
            using (var form = new SettingsForm(AppSettings.Defaults()))
            {
                var flagsLimit = BindingFlags.NonPublic | BindingFlags.Instance;
                var limit = (NumberBox)typeof(SettingsForm).GetField("_apiLimit", flagsLimit).GetValue(form);
                t.Eq("the dialog shows 50", limit.Text, "50");
                limit.Text = "120";
                typeof(SettingsForm).GetMethod("Save", flagsLimit).Invoke(form, null);
                t.Eq("the dialog returns the limit", form.Result == null ? -1 : form.Result.ApiAnswerLimitMb, 120);
            }
            using (var form = new SettingsForm(AppSettings.Defaults()))
            {
                var flagsLimit = BindingFlags.NonPublic | BindingFlags.Instance;
                ((NumberBox)typeof(SettingsForm).GetField("_apiLimit", flagsLimit).GetValue(form)).Text = "";
                typeof(SettingsForm).GetMethod("Save", flagsLimit).Invoke(form, null);
                t.Eq("an empty limit box is the default, 50 MB", form.Result == null ? -1 : form.Result.ApiAnswerLimitMb, 50);
            }

            // the dialog: the box is there only while Log is on, and its number comes back
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var start = AppSettings.Defaults();
            start.ClearLogDays = 14;
            // Visible of a control on a form that is not shown is always false; state 2 is the control's own "visible" flag.
            Func<Control, bool> shown = c => (bool)typeof(Control).GetMethod("GetState", flags).Invoke(c, new object[] { 2 });
            using (var form = new SettingsForm(start))
            {
                var log = (ToggleSwitch)typeof(SettingsForm).GetField("_log", flags).GetValue(form);
                var days = (NumberBox)typeof(SettingsForm).GetField("_clearLog", flags).GetValue(form);
                var caption = days.Parent.Controls.OfType<Label>().Single(l => l.Text.StartsWith("Clear Log"));
                t.False("Log off: the box is hidden", shown(days));
                t.False("Log off: its label is hidden", shown(caption));
                log.Checked = true;
                t.True("Log on: the box is there", shown(days));
                t.True("Log on: its label is there", shown(caption));
                t.Eq("the box shows the number of days", days.Text, "14");
                days.Text = "21";
                typeof(SettingsForm).GetMethod("Save", flags).Invoke(form, null);
                t.Eq("the dialog returns the days", form.Result == null ? -1 : form.Result.ClearLogDays, 21);
                log.Checked = false;
                t.False("Log off again: hidden again", shown(days));
            }
            using (var form = new SettingsForm(AppSettings.Defaults()))
            {
                var days = (NumberBox)typeof(SettingsForm).GetField("_clearLog", flags).GetValue(form);
                days.Text = "";
                typeof(SettingsForm).GetMethod("Save", flags).Invoke(form, null);
                t.Eq("an empty box is the default, 7 days (not 1, which would remove almost every log)", form.Result == null ? -1 : form.Result.ClearLogDays, 7);
            }

            // the cleaning
            string folder = TempFolder("clear");
            try
            {
                var now = new DateTime(2026, 11, 17, 12, 0, 0);
                string old = Make(folder, "20261101_101010_Old.log", now.AddDays(-8));
                string edge = Make(folder, "20261110_101010_Edge.log", now.AddDays(-7).AddMinutes(1));
                string fresh = Make(folder, "20261117_101010_New_selected.log", now.AddHours(-1));
                string errors = Make(folder, "errors.log", now.AddDays(-100));
                string other = Make(folder, "notes.txt", now.AddDays(-100));
                string foreign = Make(folder, "mine.log", now.AddDays(-100));
                Directory.CreateDirectory(Path.Combine(folder, "sub"));
                string nested = Make(Path.Combine(folder, "sub"), "20200101_101010_Deep.log", now.AddDays(-100));

                t.Eq("one file is older than 7 days", RunLog.ClearOld(folder, 7, now), 1);
                t.False("the old log is gone", File.Exists(old));
                t.True("a log just inside the 7 days stays", File.Exists(edge));
                t.True("a new log stays", File.Exists(fresh));
                t.True("errors.log is never cleared", File.Exists(errors));
                t.True("a file that is not a log stays", File.Exists(other));
                t.True("a .log that is not one of ours stays", File.Exists(foreign));
                t.True("folders inside are not looked into", File.Exists(nested));
                t.Eq("a second time there is nothing to do", RunLog.ClearOld(folder, 7, now), 0);
                t.Eq("1 day clears the one from last week", RunLog.ClearOld(folder, 1, now), 1);
                t.True("and keeps today's", File.Exists(fresh));
                t.Eq("0 days is not a setting: nothing is removed", RunLog.ClearOld(folder, 0, now), 0);
                t.True("today's log is still there", File.Exists(fresh));

                // a log that is in use is left and nothing fails
                string busy = Make(folder, "20260101_101010_Busy.log", now.AddDays(-50));
                using (new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))
                    t.Eq("a file in use is skipped", RunLog.ClearOld(folder, 7, now), 0);
                t.True("it is still there", File.Exists(busy));
                t.Eq("a folder that does not exist is fine", RunLog.ClearOld(Path.Combine(folder, "nowhere"), 7, now), 0);
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        [Suite("robustness.error-report")]
        public static void ErrorReport(T t)
        {
            string folder = TempFolder("errors");
            try
            {
                Exception thrown;
                try { throw new InvalidOperationException("the thing broke"); }
                catch (InvalidOperationException ex) { thrown = ex; }

                string path = ErrorReporter.Write(folder, thrown, "window");
                t.Eq("the error goes to errors.log", path, Path.Combine(folder, "errors.log"));
                string text = File.ReadAllText(path);
                t.True("with the kind and message", text.Contains("InvalidOperationException: the thing broke"));
                t.True("with where it happened", text.Contains("ClearLogSuite.ErrorReport"));
                t.True("with the time and the place it was caught", System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d  \[window\]"));
                ErrorReporter.Write(folder, new ArgumentException("second"), "fatal");
                text = File.ReadAllText(path);
                t.True("a second error is added, the first stays", text.Contains("the thing broke") && text.Contains("ArgumentException: second"));
                t.Eq("no exception object is still written", ErrorReporter.Write(folder, null, "fatal"), path);

                // a folder that cannot be made: no file, no failure
                File.WriteAllText(Path.Combine(folder, "a.txt"), "x");
                t.Eq("an impossible folder gives no file", ErrorReporter.Write(Path.Combine(folder, "a.txt", "in"), thrown, "window"), null);

                // what the user is told
                string keeps = ErrorReporter.Describe(thrown, false, path);
                t.True("it says the program keeps running", keeps.Contains("keeps running") && keeps.Contains("the thing broke") && keeps.Contains(path));
                t.True("a fatal one says the program closes", ErrorReporter.Describe(thrown, true, null).Contains("has to close"));
                t.True("a very long message is cut", ErrorReporter.Describe(new Exception(new string('x', 5000)), false, null).Length < 1000);
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
