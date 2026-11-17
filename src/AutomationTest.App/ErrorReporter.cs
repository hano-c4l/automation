using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutomationTest.App
{
    /// <summary>
    /// The last line of defence: an error that nothing else handled. On the window's thread the program tells the user and keeps
    /// running (the script that is open is not lost); every such error is also written to Logs\errors.log next to the program,
    /// with where it happened, so it can be reported. An error on another thread cannot be survived (Windows ends the program):
    /// it is written down and told before that happens.
    /// </summary>
    internal static class ErrorReporter
    {
        public const string FileName = "errors.log";
        private const long MaxFileBytes = 1024 * 1024; // the file starts again when it gets this big

        private static int _showing; // one message at a time: an error that repeats (a paint that fails) must not bury the screen

        /// <summary>Test hook: shows the message (the program uses a message box).</summary>
        internal static Action<string, bool> Show = ShowBox;

        /// <summary>Call once, before any window is made.</summary>
        public static void Install()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception, e.IsTerminating);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                e.SetObserved(); // a forgotten background task must not end the program
                Write(RunLog.Folder, e.Exception, "background task");
            };
        }

        /// <summary>Writes the error down and tells the user. Never throws.</summary>
        public static void Report(Exception ex, bool fatal)
        {
            try
            {
                if (ex is ThreadAbortException) return; // the program is closing
                string file = Write(RunLog.Folder, ex, fatal ? "fatal" : "window");
                if (Interlocked.Exchange(ref _showing, 1) == 1) return;
                try { Show(Describe(ex, fatal, file), fatal); }
                finally { Interlocked.Exchange(ref _showing, 0); }
            }
            catch (Exception)
            {
                // Reporting an error must never be the reason the program ends.
            }
        }

        /// <summary>What the user is told.</summary>
        public static string Describe(Exception ex, bool fatal, string file)
        {
            string what = ex == null ? "An unknown error." : ex.GetType().Name + ": " + ex.Message;
            if (what.Length > 600) what = what.Substring(0, 600) + "...";
            return (fatal
                    ? "Something unexpected went wrong and the program has to close."
                    : "Something unexpected went wrong. The program keeps running: save your script (Save As, to be safe) and start the program again if it behaves oddly.")
                + "\n\n" + what
                + (file != null ? "\n\nThe details were written to:\n" + file : "");
        }

        /// <summary>Adds the error to errors.log in the folder; the path, or null when it could not be written.</summary>
        public static string Write(string folder, Exception ex, string where)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, FileName);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxFileBytes) File.Delete(path);
                var text = new StringBuilder();
                text.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + where + "]  version " + Application.ProductVersion);
                text.AppendLine(ex == null ? "(no exception object)" : ex.ToString());
                text.AppendLine();
                File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
                return path;
            }
            catch (Exception)
            {
                return null; // a read-only folder: the message is still shown
            }
        }

        private static void ShowBox(string text, bool fatal)
        {
            MessageBox.Show(text, "Automation Test", MessageBoxButtons.OK, fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
        }
    }
}
