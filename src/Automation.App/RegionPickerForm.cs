using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation.App
{
    /// <summary>
    /// Full-screen translucent overlay covering every monitor. Drag with the left button to choose a rectangle,
    /// Esc to cancel. The result is in physical screen pixels.
    /// </summary>
    public sealed class RegionPickerForm : Form
    {
        private readonly Label _hint = new Label
        {
            AutoSize = true,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Padding = new Padding(6),
            Text = "Drag to select an area, Esc to cancel"
        };

        private Point _start;
        private bool _dragging;
        private Rectangle _current;

        public Rectangle SelectedRegion { get; private set; }

        public RegionPickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.3;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            DoubleBuffered = true;
            Bounds = SystemInformation.VirtualScreen;
            Controls.Add(_hint);

            MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragging = true;
                _start = MousePosition;
                _current = new Rectangle(_start, Size.Empty);
            };
            MouseMove += (s, e) =>
            {
                if (_dragging) _current = Normalize(_start, MousePosition);
                UpdateHint();
                Invalidate();
            };
            MouseUp += (s, e) =>
            {
                if (!_dragging || e.Button != MouseButtons.Left) return;
                _dragging = false;
                var region = Normalize(_start, MousePosition);
                if (region.Width < 2 || region.Height < 2) return; // a click, not a drag
                SelectedRegion = region;
                DialogResult = DialogResult.OK;
            };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = SystemInformation.VirtualScreen;
            Activate();
            UpdateHint();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_dragging || _current.Width <= 0) return;
            var local = new Rectangle(PointToClient(_current.Location), _current.Size);
            using (var fill = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                e.Graphics.FillRectangle(fill, local);
            using (var pen = new Pen(Color.Red, 2))
                e.Graphics.DrawRectangle(pen, local);
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        private void UpdateHint()
        {
            var p = MousePosition;
            _hint.Text = _dragging && _current.Width > 0
                ? "X=" + _current.X + "  Y=" + _current.Y + "  " + _current.Width + " x " + _current.Height + "   (release to confirm, Esc to cancel)"
                : "X=" + p.X + "  Y=" + p.Y + "   (drag to select an area, Esc to cancel)";
            var client = PointToClient(p);
            _hint.Location = new Point(client.X + 20, client.Y + 20);
        }
    }
}
