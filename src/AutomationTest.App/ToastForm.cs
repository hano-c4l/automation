using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutomationTest.App
{
    /// <summary>How a run ended, for the notice (Settings, Message = ON).</summary>
    internal enum RunOutcome { Finished, Failed }

    /// <summary>
    /// A small notice in the bottom right corner of the screen that says a run is over. It does not take the keyboard focus, closes
    /// itself after a few seconds, and closes at once when it is clicked.
    /// </summary>
    internal sealed class ToastForm : Form
    {
        private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x80, WsExTopmost = 0x8;

        private readonly Timer _timer = new Timer { Interval = 8000 };

        /// <summary>The title and the text of the notice (a long error message is cut).</summary>
        public static void Describe(RunOutcome outcome, string detail, out string title, out string text)
        {
            title = outcome == RunOutcome.Finished ? "Automation Test: finished" : "Automation Test: error";
            text = (detail ?? "").Trim();
            if (text.Length == 0) text = outcome == RunOutcome.Finished ? "The run is done." : "The run stopped with an error.";
            if (text.Length > 220) text = text.Substring(0, 217) + "...";
        }

        /// <summary>Shows the notice; it goes away by itself.</summary>
        public static void ShowNotice(RunOutcome outcome, string detail)
        {
            string title, text;
            Describe(outcome, detail, out title, out text);
            var toast = new ToastForm(outcome, title, text);
            toast.Show();
        }

        private ToastForm(RunOutcome outcome, string title, string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(360, 96);
            BackColor = Color.FromArgb(40, 44, 52);
            Padding = new Padding(14, 8, 10, 8);

            var accent = outcome == RunOutcome.Finished ? Color.FromArgb(76, 175, 80) : Color.FromArgb(229, 57, 53);
            var bar = new Panel { Dock = DockStyle.Left, Width = 6, BackColor = accent };
            var heading = new Label { Text = title, Dock = DockStyle.Top, Height = 22, ForeColor = Color.White, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
            var body = new Label { Text = text, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(220, 220, 220) };
            Controls.Add(body);
            Controls.Add(heading);
            Controls.Add(bar);

            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);

            EventHandler close = (s, e) => Close();
            foreach (Control c in new Control[] { this, heading, body, bar }) c.Click += close;
            _timer.Tick += close;
            Shown += (s, e) => _timer.Start();
            FormClosed += (s, e) => _timer.Dispose();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost;
                return cp;
            }
        }
    }
}
