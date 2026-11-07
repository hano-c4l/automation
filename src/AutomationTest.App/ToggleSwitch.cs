using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutomationTest.App
{
    /// <summary>
    /// An ON / OFF switch: a rounded track with a round knob that sits on the right (green, ON) or on the left (grey, OFF).
    /// It is a CheckBox underneath, so Checked, CheckedChanged, the Space key and the mouse work as usual.
    /// </summary>
    internal sealed class ToggleSwitch : CheckBox
    {
        private static readonly Color OnColor = Color.FromArgb(16, 165, 155);
        private static readonly Color OffColor = Color.FromArgb(216, 216, 218);

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Appearance = Appearance.Normal;
            AutoSize = false;
            Size = new Size(52, 28);
            Text = "";
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var track = new RectangleF(1, 1, Width - 3, Height - 3);
            Color color = Enabled ? (Checked ? OnColor : OffColor) : Color.FromArgb(235, 235, 235);
            using (var path = Pill(track))
            using (var fill = new SolidBrush(color))
                g.FillPath(fill, path);

            // The knob: white with a ring in the track colour, as large as the track allows.
            float d = track.Height - 5;
            float x = Checked ? track.Right - d - 3 : track.Left + 3;
            var knob = new RectangleF(x, track.Top + 2.5f, d, d);
            using (var white = new SolidBrush(Color.White))
                g.FillEllipse(white, knob);
            using (var ring = new Pen(color, 2f))
                g.DrawEllipse(ring, knob.X + 1, knob.Y + 1, knob.Width - 2, knob.Height - 2);

            if (Focused && ShowFocusCues)
                using (var focus = new Pen(Color.FromArgb(120, OnColor), 1.5f))
                using (var outline = Pill(new RectangleF(0, 0, Width - 1, Height - 1)))
                    g.DrawPath(focus, outline);
        }

        private static GraphicsPath Pill(RectangleF r)
        {
            float d = r.Height;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }
    }
}
