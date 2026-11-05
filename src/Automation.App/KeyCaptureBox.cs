using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Automation.Core.Input;

namespace Automation.App
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

        protected override void OnLostFocus(EventArgs e)
        {
            _down.Clear();
            base.OnLostFocus(e);
        }

        private void OnKey(Message m, bool down)
        {
            int vk = ResolveKey(m);
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
