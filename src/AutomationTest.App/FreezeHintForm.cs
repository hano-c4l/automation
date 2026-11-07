using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutomationTest.App
{
    /// <summary>
    /// A small always-on-top bar shown while a Pick waits for the moment to take the picture. It never takes the focus (neither
    /// when it appears nor when its buttons are clicked), so the program being picked keeps its look until the picture is taken.
    /// </summary>
    internal sealed class FreezeHintForm : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

        public event Action FreezeClicked;
        public event Action CancelClicked;

        public FreezeHintForm(string text, bool hotKeyAvailable)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(30, 41, 59);
            Padding = new Padding(12, 8, 12, 8);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var label = new Label { AutoSize = true, ForeColor = Color.White, Text = text, MaximumSize = new Size(Font.Height * 40, 0), Margin = new Padding(0, 4, 12, 0) };
            var freeze = new Button { Text = hotKeyAvailable ? "Freeze now (F8)" : "Freeze now", AutoSize = true, FlatStyle = FlatStyle.System, TabStop = false };
            var cancel = new Button { Text = "Cancel (Esc)", AutoSize = true, FlatStyle = FlatStyle.System, TabStop = false };
            freeze.Click += (s, e) => FreezeClicked?.Invoke();
            cancel.Click += (s, e) => CancelClicked?.Invoke();

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            row.Controls.Add(label);
            row.Controls.Add(freeze);
            row.Controls.Add(cancel);
            Controls.Add(row);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        /// <summary>Shows the bar at the top centre of the primary screen.</summary>
        public void ShowAtTop()
        {
            Show();
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + 12);
        }
    }
}
