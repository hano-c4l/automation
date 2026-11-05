using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation.App
{
    /// <summary>
    /// Full-screen translucent overlay covering every monitor. Click to choose a point, Esc to cancel.
    /// The returned point is in physical screen pixels (the process is Per-Monitor DPI aware).
    /// </summary>
    public sealed class PointPickerForm : Form
    {
        private readonly Label _hint = new Label
        {
            AutoSize = true,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Padding = new Padding(6),
            Text = "Click to pick a point, Esc to cancel"
        };

        public Point SelectedPoint { get; private set; }

        public PointPickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.25;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            Bounds = SystemInformation.VirtualScreen;
            Controls.Add(_hint);

            MouseMove += (s, e) => UpdateHint();
            MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                SelectedPoint = MousePosition;
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

        private void UpdateHint()
        {
            var p = MousePosition;
            _hint.Text = "X=" + p.X + "  Y=" + p.Y + "   (click to pick, Esc to cancel)";
            var client = PointToClient(p);
            _hint.Location = new Point(client.X + 20, client.Y + 20);
        }
    }
}
