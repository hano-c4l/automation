using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutomationTest.App
{
    /// <summary>
    /// Asks for the value of a Password variable while a script runs. What is typed is kept in memory for this run only: it is
    /// never written to the script file, the log or the settings.
    /// </summary>
    internal sealed class PasswordForm : Form
    {
        private readonly TextBox _box = new TextBox { UseSystemPasswordChar = true, Width = 260 };

        /// <summary>What was typed (valid after the dialog returns OK).</summary>
        public string Password { get; private set; }

        public PasswordForm(string id)
        {
            Text = "Password";
            Icon = AppIcon.Get() ?? Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true; // the main window is hidden while a script runs
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14);

            var note = new Label { AutoSize = true, Text = "The script needs the password {" + id + "}. It is used for this run only and is not saved." };
            var ok = new Button { Text = "OK", AutoSize = true };
            var cancel = new Button { Text = "Cancel (stop the run)", AutoSize = true, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => { Password = _box.Text; DialogResult = DialogResult.OK; Close(); };
            AcceptButton = ok;
            CancelButton = cancel;
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
            _box.Margin = new Padding(0, 8, 0, 10);
            layout.Controls.Add(note);
            layout.Controls.Add(_box);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            Shown += (s, e) => { Activate(); _box.Focus(); };
        }
    }
}
