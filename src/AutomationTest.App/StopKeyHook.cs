using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutomationTest.App
{
    /// <summary>
    /// Watches F6 and Esc with a low-level keyboard hook while a script runs. Unlike RegisterHotKey it does not swallow
    /// the keys, and it ignores keys the script itself injects, so a script can still send F6 or Esc to another program.
    /// Must be created and used on a thread with a message loop (the UI thread).
    /// </summary>
    internal sealed class StopKeyHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint LLKHF_INJECTED = 0x10;
        private const int VK_F6 = 0x75;
        private const int VK_ESCAPE = 0x1B;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private readonly HookProc _proc;
        private readonly HashSet<int> _down = new HashSet<int>();
        private IntPtr _hook;
        private Action _onStop;

        public StopKeyHook()
        {
            _proc = Callback; // kept in a field so the delegate is not garbage collected while hooked
        }

        public bool IsActive { get { return _hook != IntPtr.Zero; } }

        /// <summary>Starts watching; returns false if the hook could not be installed.</summary>
        public bool Start(Action onStop)
        {
            if (IsActive) return true;
            _onStop = onStop;
            _down.Clear();
            // A key still held from the press that started the script must not count as a new press.
            foreach (int vk in new[] { VK_F6, VK_ESCAPE })
                if ((GetAsyncKeyState(vk) & 0x8000) != 0) _down.Add(vk);

            string module = Process.GetCurrentProcess().MainModule.ModuleName;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module), 0);
            return IsActive;
        }

        public void Stop()
        {
            if (!IsActive) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                int vk = (int)data.vkCode;
                bool injected = (data.flags & LLKHF_INJECTED) != 0;
                if (!injected && (vk == VK_F6 || vk == VK_ESCAPE))
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        if (_down.Add(vk)) _onStop?.Invoke(); // auto-repeat of a held key is ignored
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        _down.Remove(vk);
                    }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
