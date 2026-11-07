using System;
using System.Runtime.InteropServices;

namespace AutomationTest.Core.Input
{
    /// <summary>
    /// Keeps the computer from taking a long run for idleness (lock, screen off, sleep): asks Windows to stay awake and sends one
    /// harmless input event (a mouse move of zero distance) so the inactivity timers restart.
    /// </summary>
    public static class KeepAwake
    {
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        public static void Poke(MouseController mouse)
        {
            if (mouse == null) throw new ArgumentNullException(nameof(mouse));
            SetThreadExecutionState(ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED); // restarts the sleep and display-off timers once
            mouse.Nudge();                                                      // restarts the inactivity timers (lock, screen saver)
        }

        /// <summary>Milliseconds since the last keyboard or mouse input Windows has seen (real or injected); -1 if unknown.</summary>
        public static long IdleMilliseconds()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref info)) return -1;
            return unchecked((uint)Environment.TickCount - info.dwTime);
        }
    }
}
