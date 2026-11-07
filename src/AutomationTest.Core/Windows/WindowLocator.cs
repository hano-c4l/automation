using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AutomationTest.Core.Model;
using AutomationTest.Core.Native;

namespace AutomationTest.Core.Windows
{
    /// <summary>The target window's client area in physical screen pixels.</summary>
    public struct ClientArea
    {
        public int Left;
        public int Top;
        public int Width;
        public int Height;
    }

    public struct WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public string ProcessName;
    }

    /// <summary>Finds windows and converts between screen coordinates and window-relative offsets.</summary>
    public static class WindowLocator
    {
        /// <summary>Returns the first visible top-level window matching the target (preferring non-minimized), or IntPtr.Zero.</summary>
        /// <exception cref="ArgumentException">The title pattern is not a valid regular expression.</exception>
        public static IntPtr Find(WindowTarget target)
        {
            if (target == null || target.IsEmpty) return IntPtr.Zero;

            Regex title = null;
            if (!string.IsNullOrWhiteSpace(target.TitlePattern))
            {
                try { title = new Regex(target.TitlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                catch (ArgumentException ex) { throw new ArgumentException("Invalid title pattern: " + ex.Message); }
            }
            string process = string.IsNullOrWhiteSpace(target.ProcessName) ? null : target.ProcessName.Trim();
            if (process != null && process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                process = process.Substring(0, process.Length - 4);

            var processNames = new Dictionary<uint, string>();
            IntPtr firstMatch = IntPtr.Zero, firstRestored = IntPtr.Zero;

            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd)) return true;
                string text = GetTitle(hwnd);
                if (text.Length == 0) return true;
                if (title != null && !title.IsMatch(text)) return true;
                if (process != null)
                {
                    NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                    if (!processNames.TryGetValue(pid, out string name))
                        processNames[pid] = name = GetProcessName(pid);
                    if (!string.Equals(name, process, StringComparison.OrdinalIgnoreCase)) return true;
                }
                if (firstMatch == IntPtr.Zero) firstMatch = hwnd;
                if (!NativeMethods.IsIconic(hwnd))
                {
                    firstRestored = hwnd;
                    return false; // best possible match
                }
                return true;
            }, IntPtr.Zero);

            return firstRestored != IntPtr.Zero ? firstRestored : firstMatch;
        }

        /// <summary>True if the given window satisfies the target's process name and title pattern.</summary>
        /// <exception cref="ArgumentException">The title pattern is not a valid regular expression.</exception>
        public static bool Matches(IntPtr hwnd, WindowTarget target)
        {
            if (target == null || target.IsEmpty || !Exists(hwnd)) return false;

            if (!string.IsNullOrWhiteSpace(target.TitlePattern))
            {
                Regex title;
                try { title = new Regex(target.TitlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                catch (ArgumentException ex) { throw new ArgumentException("Invalid title pattern: " + ex.Message); }
                if (!title.IsMatch(GetTitle(hwnd))) return false;
            }
            if (!string.IsNullOrWhiteSpace(target.ProcessName))
            {
                string process = target.ProcessName.Trim();
                if (process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    process = process.Substring(0, process.Length - 4);
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (!string.Equals(GetProcessName(pid), process, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        public static bool Exists(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd);
        }

        public static bool IsForeground(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && NativeMethods.GetForegroundWindow() == hwnd;
        }

        /// <summary>True if the window, or another window of the same process (such as its dialog), is in front.</summary>
        public static bool IsActive(IntPtr hwnd)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            return foreground != IntPtr.Zero && (foreground == hwnd || SameProcess(foreground, hwnd));
        }

        public static bool SameProcess(IntPtr a, IntPtr b)
        {
            if (a == IntPtr.Zero || b == IntPtr.Zero) return false;
            NativeMethods.GetWindowThreadProcessId(a, out uint pidA);
            NativeMethods.GetWindowThreadProcessId(b, out uint pidB);
            return pidA != 0 && pidA == pidB;
        }

        public static bool IsMinimized(IntPtr hwnd)
        {
            return NativeMethods.IsIconic(hwnd);
        }

        public static string GetTitle(IntPtr hwnd)
        {
            int length = NativeMethods.GetWindowTextLength(hwnd);
            if (length <= 0) return string.Empty;
            var sb = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <exception cref="InvalidOperationException">The window is gone or has no client area.</exception>
        public static ClientArea GetClientArea(IntPtr hwnd)
        {
            if (!NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT rect))
                throw new InvalidOperationException("The target window is no longer available.");
            var origin = new NativeMethods.POINT();
            if (!NativeMethods.ClientToScreen(hwnd, ref origin))
                throw new InvalidOperationException("The target window is no longer available.");
            return new ClientArea { Left = origin.X, Top = origin.Y, Width = rect.Right - rect.Left, Height = rect.Bottom - rect.Top };
        }

        /// <summary>Converts a window-relative offset to absolute screen pixels.</summary>
        public static void ToScreen(ClientArea area, WindowAnchor anchor, int offsetX, int offsetY, out int x, out int y)
        {
            AnchorPoint(area, anchor, out int ax, out int ay);
            x = ax + offsetX;
            y = ay + offsetY;
        }

        /// <summary>Converts absolute screen pixels to an offset from the anchor point of the client area.</summary>
        public static void FromScreen(ClientArea area, WindowAnchor anchor, int screenX, int screenY, out int offsetX, out int offsetY)
        {
            AnchorPoint(area, anchor, out int ax, out int ay);
            offsetX = screenX - ax;
            offsetY = screenY - ay;
        }

        /// <summary>The anchor as fractions of a rectangle: (0,0) top-left, (1,1) bottom-right, (0.5,0.5) centre.</summary>
        public static void AnchorFraction(WindowAnchor anchor, out double fx, out double fy)
        {
            switch (anchor)
            {
                case WindowAnchor.TopRight: fx = 1; fy = 0; break;
                case WindowAnchor.BottomLeft: fx = 0; fy = 1; break;
                case WindowAnchor.BottomRight: fx = 1; fy = 1; break;
                case WindowAnchor.Center: fx = 0.5; fy = 0.5; break;
                default: fx = 0; fy = 0; break;
            }
        }

        private static void AnchorPoint(ClientArea area, WindowAnchor anchor, out int x, out int y)
        {
            switch (anchor)
            {
                case WindowAnchor.TopRight: x = area.Left + area.Width; y = area.Top; break;
                case WindowAnchor.BottomLeft: x = area.Left; y = area.Top + area.Height; break;
                case WindowAnchor.BottomRight: x = area.Left + area.Width; y = area.Top + area.Height; break;
                case WindowAnchor.Center: x = area.Left + area.Width / 2; y = area.Top + area.Height / 2; break;
                default: x = area.Left; y = area.Top; break;
            }
        }

        /// <summary>Returns the top-level window under a screen point, or null if there is none.</summary>
        public static WindowInfo? WindowAt(int x, int y)
        {
            IntPtr hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = x, Y = y });
            if (hwnd == IntPtr.Zero) return null;
            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero) hwnd = root;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return new WindowInfo { Handle = hwnd, Title = GetTitle(hwnd), ProcessName = GetProcessName(pid) };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);

        private static readonly HashSet<string> DesktopClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
        };

        private static readonly HashSet<string> DesktopProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "StartMenuExperienceHost", "SearchHost", "SearchApp", "ShellExperienceHost"
        };

        /// <summary>
        /// True for the desktop background, the taskbar and the Start / search menus. They belong to the shell (explorer)
        /// but are not a program to target: they count as the full desktop (no Process).
        /// </summary>
        public static bool IsDesktopSurface(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (DesktopClasses.Contains(cls.ToString())) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return DesktopProcesses.Contains(GetProcessName(pid));
        }

        /// <summary>Restores the window if minimized and tries to make it the foreground window (best effort).</summary>
        public static void BringToFront(IntPtr hwnd)
        {
            if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

            // Windows refuses SetForegroundWindow from a background process; attaching to the foreground thread's input queue lifts that.
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            uint foregroundThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
            uint thisThread = NativeMethods.GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != thisThread
                && NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                NativeMethods.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached) NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }

        private static string GetProcessName(uint pid)
        {
            try
            {
                using (var p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch (Exception)
            {
                return string.Empty; // process exited or access denied
            }
        }
    }
}
