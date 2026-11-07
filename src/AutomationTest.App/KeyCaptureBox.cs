using System;
using System.Collections.Generic;
using System.Windows.Forms;
using AutomationTest.Core.Input;

namespace AutomationTest.App
{
    /// <summary>
    /// A read-only box that records the keys pressed while it has focus. In single mode only the first key of a
    /// press counts; in combo mode every key held together is added in the order pressed.
    /// </summary>
    internal sealed class KeyCaptureBox : TextBox
    {
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private readonly List<string> _names = new List<string>();
        private readonly HashSet<int> _down = new HashSet<int>();

        public KeyCaptureBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            BackColor = System.Drawing.SystemColors.Window;
        }

        /// <summary>True: record a combination; false: record a single key.</summary>
        public bool Combo { get; set; }

        /// <summary>Key names joined by "+" (a single name in single mode).</summary>
        public string Value
        {
            get { return KeyCodes.Join(_names); }
            set
            {
                _names.Clear();
                foreach (string part in (value ?? "").Split('+'))
                    if (part.Trim().Length > 0) _names.Add(part.Trim());
                Text = KeyCodes.Join(_names);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Handled here, before normal dialog processing, so Tab, arrows and Alt can be recorded as keys.
            if (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN)
            {
                OnKey(msg, true);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_KEYUP || m.Msg == WM_SYSKEYUP)
            {
                OnKey(m, false);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            InstallHook();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            RemoveHook();
            _down.Clear();
            base.OnLostFocus(e);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            RemoveHook();
            base.OnHandleDestroyed(e);
        }

        // A low-level hook sees combinations the window never receives (Alt+Tab is handled by the system before
        // the window gets the Tab) and lets them be swallowed, so the task switcher does not open while capturing.
        private const int WH_KEYBOARD_LL = 13;
        private const uint LLKHF_EXTENDED = 0x01;
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        private HookProc _hookProc;
        private IntPtr _hook;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        private void InstallHook()
        {
            if (_hook != IntPtr.Zero) return;
            _hookProc = HookCallback;
            IntPtr module = GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName);
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, module, 0); // zero = not available: the normal key path still works
        }

        private void RemoveHook()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Focused && Form.ActiveForm != null)
            {
                int msg = wParam.ToInt32();
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                if (down || up)
                {
                    var data = (KBDLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    int vk = (int)data.vkCode;
                    switch (vk)
                    {
                        case 0x10: vk = data.scanCode == 0x36 ? 0xA1 : 0xA0; break;
                        case 0x11: vk = (data.flags & LLKHF_EXTENDED) != 0 ? 0xA3 : 0xA2; break;
                        case 0x12: vk = (data.flags & LLKHF_EXTENDED) != 0 ? 0xA5 : 0xA4; break;
                    }
                    OnKeyCode(vk, down);
                    return (IntPtr)1; // swallow: nothing else reacts to the key while it is being captured
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void OnKey(Message m, bool down)
        {
            if (_hook != IntPtr.Zero) return; // the hook already reported this key
            OnKeyCode(ResolveKey(m), down);
        }

        private void OnKeyCode(int vk, bool down)
        {
            if (down)
            {
                if (!_down.Add(vk)) return; // auto-repeat
                if (!KeyCodes.TryGetName(vk, out string name)) return;
                bool fresh = _down.Count == 1;
                if (!Combo)
                {
                    if (!fresh) return; // several keys together: only the first counts
                    _names.Clear();
                    _names.Add(name);
                }
                else
                {
                    if (fresh) _names.Clear(); // a new press after all keys were released starts a new combination
                    if (!_names.Contains(name)) _names.Add(name);
                }
                Text = KeyCodes.Join(_names);
            }
            else
            {
                _down.Remove(vk);
            }
        }

        /// <summary>Turns the generic Shift/Ctrl/Alt codes into the left or right key, using the scan code and extended-key bit.</summary>
        private static int ResolveKey(Message m)
        {
            int vk = (int)(m.WParam.ToInt64() & 0xFF);
            long lParam = m.LParam.ToInt64();
            int scan = (int)((lParam >> 16) & 0xFF);
            bool extended = ((lParam >> 24) & 1) != 0;
            switch (vk)
            {
                case 0x10: return scan == 0x36 ? 0xA1 : 0xA0;
                case 0x11: return extended ? 0xA3 : 0xA2;
                case 0x12: return extended ? 0xA5 : 0xA4;
                default: return vk;
            }
        }
    }
}
