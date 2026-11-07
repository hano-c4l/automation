using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutomationTest.App
{
    /// <summary>
    /// A click-through red frame drawn around a window on screen (shown while picking a process so the user can see
    /// which program the mouse points at). It never takes focus or mouse input.
    /// </summary>
    internal sealed class WindowOutlineForm : Form
    {
        private const int Thickness = 2;
        private static readonly Color Key = Color.Magenta;

        private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;

        public WindowOutlineForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Key;
            TransparencyKey = Key;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED; // layered + transparent = mouse passes through
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.Red, Thickness))
                e.Graphics.DrawRectangle(pen, Thickness / 2, Thickness / 2, Width - Thickness, Height - Thickness);
        }

        /// <summary>Moves the frame around the given screen rectangle (physical pixels) and shows it.</summary>
        public void Outline(Rectangle screenRect)
        {
            Bounds = screenRect;
            Invalidate();
            if (!Visible) Show();
        }
    }
}
