using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Automation.Core.Input;
using Automation.Core.Recording;

namespace Automation.App
{
    /// <summary>
    /// Records the user's mouse buttons, wheel and keys with low-level hooks (the input still reaches the programs).
    /// Input on this program's own windows is ignored, and so are the hot keys F6 and F7. Must run on the UI thread.
    /// </summary>
    internal sealed class InputRecorder : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        private const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MOUSEWHEEL = 0x020A;
        private const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x01;
        public const int StartStopKey = 0x75;  // F6
        public const int RecordKey = 0x76;     // F7

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private readonly HookProc _keyProc, _mouseProc;
        private readonly List<RecordedEvent> _events = new List<RecordedEvent>();
        private readonly HashSet<int> _keysDown = new HashSet<int>();
        private readonly uint _ownProcess = (uint)Process.GetCurrentProcess().Id;
        private readonly Stopwatch _clock = new Stopwatch();
        private const int WM_MOUSEMOVE = 0x0200;
        private const long MoveIntervalMs = 8; // dense on purpose: RecordConverter drops the points that add no shape
        private int _buttonsDown;
        private long _lastMoveMs = -MoveIntervalMs;
        private IntPtr _keyHook, _mouseHook;
        private Action _onStopKey;

        public InputRecorder()
        {
            _keyProc = KeyCallback;     // kept in fields so the delegates are not collected while hooked
            _mouseProc = MouseCallback;
        }

        /// <summary>Normally injected (synthetic) input is not recorded; tests turn this off to drive the recorder.</summary>
        public bool RecordInjected { get; set; }

        /// <summary>Normally input on this program's own windows is not recorded; tests that host their target window in this process turn this off.</summary>
        public bool IgnoreOwnWindows { get; set; } = true;

        /// <summary>False: cursor moves are recorded only while a button is held (drags). True: every move is recorded.</summary>
        public bool AllEvents { get; set; }

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ToUnicodeEx(uint vk, uint scan, byte[] keyState, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder buffer, int size, uint flags, IntPtr layout);

        /// <summary>The text a key types on the current keyboard layout (null for keys that type nothing or only a dead key).</summary>
        public static string CharOf(int virtualKey, bool shift)
        {
            var state = new byte[256];
            if (shift) state[0x10] = 0x80;
            var buffer = new System.Text.StringBuilder(8);
            uint scan = MapVirtualKey((uint)virtualKey, 0);
            // Flag 4: do not change the keyboard's dead-key state.
            int count = ToUnicodeEx((uint)virtualKey, scan, state, buffer, buffer.Capacity, 4, GetKeyboardLayout(0));
            if (count != 1) return null;
            char c = buffer[0];
            return char.IsControl(c) ? null : c.ToString();
        }

        public bool IsRecording { get { return _keyHook != IntPtr.Zero; } }

        public int Count { get { return _events.Count; } }

        /// <param name="onStopKey">Called when F7 is pressed (the key itself is not recorded).</param>
        public bool Start(Action onStopKey)
        {
            if (IsRecording) return true;
            _events.Clear();
            _keysDown.Clear();
            _buttonsDown = 0;
            _lastMoveMs = -MoveIntervalMs;
            // F7 still held from the press that started recording must not count as the press that stops it.
            if ((GetAsyncKeyState(RecordKey) & 0x8000) != 0) _keysDown.Add(RecordKey);
            _onStopKey = onStopKey;
            _clock.Restart();
            IntPtr module = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, module, 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
            if (_keyHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            {
                Stop();
                return false;
            }
            return true;
        }

        /// <summary>Stops recording and returns what was recorded.</summary>
        public List<RecordedEvent> Stop()
        {
            if (_keyHook != IntPtr.Zero) UnhookWindowsHookEx(_keyHook);
            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
            _keyHook = _mouseHook = IntPtr.Zero;
            _clock.Stop();
            return new List<RecordedEvent>(_events);
        }

        private bool IsOwn(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IgnoreOwnWindows) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == _ownProcess;
        }

        private IntPtr KeyCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                int msg = wParam.ToInt32();
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                bool injected = (data.flags & LLKHF_INJECTED) != 0;
                int vk = Resolve((int)data.vkCode, (data.flags & LLKHF_EXTENDED) != 0, (int)data.scanCode);
                if ((down || up) && (!injected || RecordInjected))
                {
                    if (vk == RecordKey)
                    {
                        if (down && _keysDown.Add(vk)) _onStopKey?.Invoke();
                        else if (up) _keysDown.Remove(vk);
                    }
                    else if (vk != StartStopKey && !IsOwn(GetForegroundWindow()))
                    {
                        _events.Add(new RecordedEvent { Kind = down ? RecordedKind.KeyDown : RecordedKind.KeyUp, VirtualKey = vk, TimeMs = _clock.ElapsedMilliseconds });
                    }
                }
            }
            return CallNextHookEx(_keyHook, nCode, wParam, lParam);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                bool injected = (data.flags & LLMHF_INJECTED) != 0;
                int msg = wParam.ToInt32();
                if ((!injected || RecordInjected) && !IsOwn(WindowFromPoint(data.pt)))
                {
                    var e = new RecordedEvent { X = data.pt.X, Y = data.pt.Y, TimeMs = _clock.ElapsedMilliseconds };
                    bool keep = true;
                    switch (msg)
                    {
                        case WM_LBUTTONDOWN: e.Kind = RecordedKind.MouseDown; e.Button = MouseButton.Left; break;
                        case WM_LBUTTONUP: e.Kind = RecordedKind.MouseUp; e.Button = MouseButton.Left; break;
                        case WM_RBUTTONDOWN: e.Kind = RecordedKind.MouseDown; e.Button = MouseButton.Right; break;
                        case WM_RBUTTONUP: e.Kind = RecordedKind.MouseUp; e.Button = MouseButton.Right; break;
                        case WM_MBUTTONDOWN: e.Kind = RecordedKind.MouseDown; e.Button = MouseButton.Middle; break;
                        case WM_MBUTTONUP: e.Kind = RecordedKind.MouseUp; e.Button = MouseButton.Middle; break;
                        case WM_MOUSEWHEEL:
                            e.Kind = RecordedKind.Wheel;
                            int delta = (short)((data.mouseData >> 16) & 0xFFFF);
                            e.Notches = delta / 120 != 0 ? delta / 120 : Math.Sign(delta);
                            break;
                        case WM_MOUSEMOVE:
                            // Moves count only while a button is held (a drag); throttled so a drag stays a manageable number of actions.
                            e.Kind = RecordedKind.Move;
                            keep = (AllEvents || _buttonsDown > 0) && e.TimeMs - _lastMoveMs >= MoveIntervalMs;
                            if (keep) _lastMoveMs = e.TimeMs;
                            break;
                        default: keep = false; break;
                    }
                    if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN) _buttonsDown++;
                    else if ((msg == WM_LBUTTONUP || msg == WM_RBUTTONUP || msg == WM_MBUTTONUP) && _buttonsDown > 0) _buttonsDown--;
                    if (keep) _events.Add(e);
                }
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        /// <summary>The low-level hook reports generic Shift/Ctrl/Alt for some sources; resolve to left or right.</summary>
        private static int Resolve(int vk, bool extended, int scan)
        {
            switch (vk)
            {
                case 0x10: return scan == 0x36 ? 0xA1 : 0xA0;
                case 0x11: return extended ? 0xA3 : 0xA2;
                case 0x12: return extended ? 0xA5 : 0xA4;
                default: return vk;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
