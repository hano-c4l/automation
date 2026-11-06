using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Automation.Core.Native;

namespace Automation.Core.Input
{
    /// <summary>
    /// Sends mouse input through SendInput. All coordinates are physical screen pixels
    /// (the process must be Per-Monitor DPI aware, see app.manifest).
    /// </summary>
    public sealed class MouseController
    {
        public (int X, int Y) GetPosition()
        {
            NativeMethods.GetCursorPos(out var p);
            return (p.X, p.Y);
        }

        /// <summary>Moves the cursor to the target point, interpolating over durationMs with smoothstep easing.</summary>
        public void MoveTo(int x, int y, int durationMs, CancellationToken token = default)
        {
            if (durationMs <= 0)
            {
                SetPosition(x, y);
                return;
            }

            var start = GetPosition();
            NativeMethods.timeBeginPeriod(1);
            try
            {
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    double t = Math.Min(1.0, sw.ElapsedMilliseconds / (double)durationMs);
                    double e = t * t * (3 - 2 * t);
                    SetPosition(
                        (int)Math.Round(start.X + (x - start.X) * e),
                        (int)Math.Round(start.Y + (y - start.Y) * e));
                    if (t >= 1.0) break;
                    Thread.Sleep(4);
                }
            }
            finally
            {
                NativeMethods.timeEndPeriod(1);
            }
        }

        public void Down(MouseButton button) => Send(0, 0, DownFlag(button));

        public void Up(MouseButton button) => Send(0, 0, UpFlag(button));

        /// <summary>Presses and releases the button, holding it for holdMs milliseconds.</summary>
        public void Click(MouseButton button, int holdMs = 30)
        {
            Down(button);
            if (holdMs > 0) Thread.Sleep(holdMs);
            Up(button);
        }

        /// <summary>Turns the wheel at the current cursor position; positive notches scroll up (away from the user).</summary>
        public void Scroll(int notches)
        {
            const int WheelDelta = 120;
            var input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT { mouseData = unchecked((uint)(notches * WheelDelta)), dwFlags = NativeMethods.MOUSEEVENTF_WHEEL }
            };
            uint sent = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
            if (sent != 1)
                throw new InvalidOperationException("SendInput failed, Win32 error " + Marshal.GetLastWin32Error());
        }

        private static void SetPosition(int x, int y)
        {
            int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

            // Normalize to 0..65535 across the whole virtual desktop (multi-monitor safe). Windows maps back with
            // floor(n * size / 65536), so rounding up here lands exactly on the requested pixel.
            int nx = (int)Math.Min(65535, Math.Ceiling((x - vx) * 65536.0 / Math.Max(1, vw)));
            int ny = (int)Math.Min(65535, Math.Ceiling((y - vy) * 65536.0 / Math.Max(1, vh)));

            Send(nx, ny, NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK);
        }

        private static void Send(int dx, int dy, uint flags)
        {
            var input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags }
            };
            uint sent = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
            if (sent != 1)
                throw new InvalidOperationException("SendInput failed, Win32 error " + Marshal.GetLastWin32Error());
        }

        private static uint DownFlag(MouseButton b)
        {
            switch (b)
            {
                case MouseButton.Right: return NativeMethods.MOUSEEVENTF_RIGHTDOWN;
                case MouseButton.Middle: return NativeMethods.MOUSEEVENTF_MIDDLEDOWN;
                default: return NativeMethods.MOUSEEVENTF_LEFTDOWN;
            }
        }

        private static uint UpFlag(MouseButton b)
        {
            switch (b)
            {
                case MouseButton.Right: return NativeMethods.MOUSEEVENTF_RIGHTUP;
                case MouseButton.Middle: return NativeMethods.MOUSEEVENTF_MIDDLEUP;
                default: return NativeMethods.MOUSEEVENTF_LEFTUP;
            }
        }
    }
}
