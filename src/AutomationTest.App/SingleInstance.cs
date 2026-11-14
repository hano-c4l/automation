using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutomationTest.App
{
    /// <summary>
    /// Lets only one copy of the program run for a user. A second start does not open a new window: it asks the running copy to
    /// come to the front (also when that copy is minimized or hidden in the notification area) and exits.
    /// </summary>
    internal static class SingleInstance
    {
        private static Mutex _mutex; // kept for as long as the program runs

        /// <summary>The window message the running copy answers by showing its window.</summary>
        public static readonly int ShowMessage = RegisterWindowMessage("AutomationTest.ShowMainWindow");

        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);
        private const int AsfwAny = -1;

        /// <summary>True when this is the first copy. Otherwise the running copy has been asked to show itself.</summary>
        public static bool TryStart()
        {
            _mutex = new Mutex(true, @"Local\AutomationTest.SingleInstance", out bool first);
            if (first) return true;
            // The program was just started by the user, so it may hand the foreground to the running copy.
            AllowSetForegroundWindow(AsfwAny);
            PostMessage(HwndBroadcast, ShowMessage, IntPtr.Zero, IntPtr.Zero);
            return false;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
