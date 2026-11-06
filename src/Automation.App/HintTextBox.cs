using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation.App
{
    /// <summary>
    /// A text box that shows a faint hint while it is empty. Single-line boxes use the system cue banner; multi-line boxes
    /// (which the system banner does not support) paint the hint themselves.
    /// </summary>
    public class HintTextBox : TextBox
    {
        private const int EM_SETCUEBANNER = 0x1501;
        private const int WM_PAINT = 0x000F;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private string _placeholder = "";

        /// <summary>Faint hint shown while the box is empty, such as "Process Name".</summary>
        public string Placeholder
        {
            get { return _placeholder; }
            set
            {
                _placeholder = value ?? "";
                ApplyPlaceholder();
                Invalidate();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyPlaceholder();
        }

        private void ApplyPlaceholder()
        {
            if (IsHandleCreated && !Multiline) SendMessage(Handle, EM_SETCUEBANNER, (IntPtr)1, _placeholder);
        }

        protected override void OnMultilineChanged(EventArgs e)
        {
            base.OnMultilineChanged(e);
            ApplyPlaceholder();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT && Multiline && _placeholder.Length > 0 && Text.Length == 0)
            {
                using (var g = CreateGraphics())
                    TextRenderer.DrawText(g, _placeholder, Font, new Point(2, 1), SystemColors.GrayText, TextFormatFlags.NoPadding);
            }
        }
    }
}
