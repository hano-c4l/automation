using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using AutomationTest.Core.Model;
using AutomationTest.Core.Native;
using AutomationTest.Core.Windows;

namespace AutomationTest.Core.Runner
{
    /// <summary>The Close action: finds running programs and closes them.</summary>
    public static class ProgramActions
    {
        private const uint WM_CLOSE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRect { public int Left, Top, Right, Bottom; }

        private const int SwMaximize = 3, SwMinimize = 6, SwRestore = 9;
        private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;

        /// <summary>
        /// Minimizes, maximizes, restores, activates, moves or resizes the first window found (every window with
        /// <paramref name="all"/>). Returns how many windows were found; 0 when there is none.
        /// </summary>
        public static int Arrange(CommandKind kind, ProgramMatch match, string value, bool all, int x, int y, int width, int height)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) throw new InvalidOperationException("Enter the window to act on (" + Describe(match) + ").");
            var windows = FindWindows(match, text, all);
            foreach (IntPtr hwnd in windows)
            {
                switch (kind)
                {
                    case CommandKind.WindowActivate:
                        if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
                        WindowLocator.BringToFront(hwnd);
                        break;
                    case CommandKind.WindowMinimize: ShowWindow(hwnd, SwMinimize); break;
                    case CommandKind.WindowMaximize: ShowWindow(hwnd, SwMaximize); break;
                    case CommandKind.WindowRestore: ShowWindow(hwnd, SwRestore); break;
                    case CommandKind.WindowMove:
                        if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindow(hwnd, SwRestore); // a minimized or maximized window has no place to move
                        if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate))
                            throw new InvalidOperationException("The window could not be moved (a window of a program that runs as administrator can only be moved when this program does too).");
                        break;
                    case CommandKind.WindowResize:
                        if (width < 1 || height < 1) throw new InvalidOperationException("Enter a Width and a Height of at least 1.");
                        if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindow(hwnd, SwRestore);
                        if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, SwpNoMove | SwpNoZOrder | SwpNoActivate))
                            throw new InvalidOperationException("The window could not be resized (a window of a program that runs as administrator can only be resized when this program does too).");
                        break;
                }
            }
            return windows.Count;
        }

        /// <summary>The top-left corner and the size of a window on the screen (for the tests and for checking a result).</summary>
        public static void GetBounds(IntPtr hwnd, out int x, out int y, out int width, out int height)
        {
            WindowRect r;
            GetWindowRect(hwnd, out r);
            x = r.Left; y = r.Top; width = r.Right - r.Left; height = r.Bottom - r.Top;
        }

        /// <summary>
        /// The visible windows (with a title) of the programs that match, the one at the front first; only the first one unless
        /// <paramref name="all"/>. A title match gives the windows whose own title matches.
        /// </summary>
        public static List<IntPtr> FindWindows(ProgramMatch match, string text, bool all)
        {
            var found = new List<IntPtr>();
            foreach (var program in Find(match, text))
            {
                using (program.Process) found.AddRange(program.Windows);
            }
            if (found.Count == 0) return found;
            // Front to back, so "the first window" is the one the user sees on top.
            var order = new List<IntPtr>();
            NativeMethods.EnumWindows((hwnd, _) => { if (found.Contains(hwnd)) order.Add(hwnd); return true; }, IntPtr.Zero);
            return all ? order : order.Take(1).ToList();
        }

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);

        private const uint ProcessQueryLimitedInformation = 0x1000;

        /// <summary>
        /// The full path of the program file of a process; null when Windows does not tell (a protected process). Works across 32 and
        /// 64 bit and for most programs that run as administrator, which Process.MainModule does not.
        /// </summary>
        public static string ImagePath(int processId)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(handle); }
        }

        /// <summary>A running program that matched, and the windows that matched (Title) or all its visible windows.</summary>
        public sealed class Match : IDisposable
        {
            public Process Process;
            public List<IntPtr> Windows = new List<IntPtr>();
            public void Dispose() { Process.Dispose(); }
        }

        /// <summary>
        /// Closes the programs that match (only the first one unless <paramref name="all"/>). Without <paramref name="kill"/> the
        /// program is asked to close its windows and has <paramref name="timeoutMs"/> to end (a program that asks to save
        /// first stays open, which is an error); with it the program is ended at once and cannot save.
        /// </summary>
        /// <returns>How many programs were closed (0 when nothing matched).</returns>
        public static int Close(ProgramMatch match, string value, bool all, bool kill, long timeoutMs, CancellationToken token)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) throw new InvalidOperationException("Close: enter what to look for (" + Describe(match) + ").");
            var found = Find(match, text);
            try
            {
                if (found.Count == 0) return 0;
                var chosen = all ? found : found.Take(1).ToList();
                foreach (var target in chosen) CloseOne(target, kill, timeoutMs, token);
                return chosen.Count;
            }
            finally { foreach (var m in found) m.Dispose(); }
        }

        private static string Describe(ProgramMatch match)
        {
            switch (match)
            {
                case ProgramMatch.Name: return "the process name, for example notepad";
                case ProgramMatch.Path: return "the full path of the program file";
                case ProgramMatch.Id: return "the process id";
                default: return "a regular expression for the window title";
            }
        }

        /// <summary>The visible top-level windows with a title, by process id.</summary>
        private static Dictionary<uint, List<IntPtr>> WindowsByProcess()
        {
            var map = new Dictionary<uint, List<IntPtr>>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd)) return true;
                if (WindowLocator.GetTitle(hwnd).Length == 0) return true;
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (!map.TryGetValue(pid, out var list)) map[pid] = list = new List<IntPtr>();
                list.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return map;
        }

        /// <summary>The running programs that match, the one with the lowest process id first. The program itself is never in the list.</summary>
        public static List<Match> Find(ProgramMatch match, string text)
        {
            uint self = (uint)Process.GetCurrentProcess().Id;
            var windows = WindowsByProcess();
            var result = new List<Match>();

            if (match == ProgramMatch.Id)
            {
                if (!int.TryParse(text, out int id)) throw new InvalidOperationException("Close: \"" + text + "\" is not a process id (a number).");
                if ((uint)id == self) return result;
                try { result.Add(Make(Process.GetProcessById(id), windows)); } catch (ArgumentException) { }
                return result;
            }

            Regex title = null;
            string path = null, name = null;
            if (match == ProgramMatch.Title)
            {
                try { title = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
                catch (ArgumentException ex) { throw new InvalidOperationException("Close: the title is not a valid regular expression: " + ex.Message); }
            }
            else if (match == ProgramMatch.Path) path = FileActions.FullPath(text, "path");
            else
            {
                name = text;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            }

            foreach (var p in Process.GetProcesses().OrderBy(p => p.Id))
            {
                bool hit = false;
                var matched = new List<IntPtr>();
                try
                {
                    if ((uint)p.Id == self) { p.Dispose(); continue; }
                    if (match == ProgramMatch.Title)
                    {
                        // Any visible window of the program with a matching title counts (programs without a window never match).
                        if (windows.TryGetValue((uint)p.Id, out var list))
                            foreach (var hwnd in list)
                                if (title.IsMatch(WindowLocator.GetTitle(hwnd))) matched.Add(hwnd);
                        hit = matched.Count > 0;
                    }
                    else if (match == ProgramMatch.Name) hit = string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase);
                    else
                    {
                        string image = ImagePath(p.Id); // null for a protected process that Windows does not describe
                        hit = image != null && string.Equals(image, path, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (InvalidOperationException) { hit = false; } // it ended while we looked
                catch (Win32Exception) { hit = false; }
                if (!hit) { p.Dispose(); continue; }
                var m = Make(p, windows);
                if (match == ProgramMatch.Title) m.Windows = matched;
                result.Add(m);
            }
            return result;
        }

        private static Match Make(Process process, Dictionary<uint, List<IntPtr>> windows)
        {
            var m = new Match { Process = process };
            if (windows.TryGetValue((uint)process.Id, out var list)) m.Windows = list;
            return m;
        }

        private static void CloseOne(Match target, bool kill, long timeoutMs, CancellationToken token)
        {
            var process = target.Process;
            string label = SafeName(process) + " (id " + process.Id + ")";
            try
            {
                if (process.HasExited) return;
                if (kill)
                {
                    process.Kill();
                    WaitForEnd(process, 5000, token);
                    return;
                }
                if (target.Windows.Count == 0)
                    throw new InvalidOperationException("Close: " + label + " has no window to close. Turn on Kill to end it.");
                // Like clicking the X of each window; the program decides what to do (it may ask to save).
                foreach (var hwnd in target.Windows) PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                if (!WaitForEnd(process, Math.Max(timeoutMs, 1000), token))
                    throw new InvalidOperationException("Close: " + label + " did not close within " + Math.Max(timeoutMs, 1000) + " ms (it may be asking whether to save). Turn on Kill to end it, or give it more time.");
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException("Close: cannot close " + label + ": " + ex.Message + " (a program that runs as administrator can only be closed by this program when it runs as administrator too).");
            }
        }

        private static bool WaitForEnd(Process process, long ms, CancellationToken token)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                token.ThrowIfCancellationRequested();
                if (process.WaitForExit(100)) return true;
            }
            return process.HasExited;
        }

        private static string SafeName(Process p) { try { return p.ProcessName; } catch (Exception) { return "the program"; } }
    }
}
