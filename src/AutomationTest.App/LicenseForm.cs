using System;
using System.Drawing;
using System.Windows.Forms;
using AutomationTest.Core.Licensing;

namespace AutomationTest.App
{
    /// <summary>
    /// Shows the request key and the license state, and imports a license from a file or from pasted text. In gate mode
    /// (no valid license) it is the only thing the program shows: closing it without a valid license exits the program.
    /// </summary>
    internal sealed class LicenseForm : Form
    {
        private readonly Label _status = new Label { AutoSize = true };
        private readonly TextBox _requestKey = new TextBox { ReadOnly = true, Font = new Font(FontFamily.GenericMonospace, 10f) };
        private readonly Button _copy = new Button { Text = "Copy", AutoSize = true };
        private readonly TextBox _paste = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 8.5f) };
        private readonly Button _usePasted = new Button { Text = "Use pasted license", AutoSize = true };
        private readonly Button _import = new Button { Text = "Import file...", AutoSize = true };
        private readonly Button _close = new Button { AutoSize = true };

        public LicenseForm(bool gate, LicenseResult current)
        {
            Text = gate ? "Automation Test - License required" : "Automation Test - License";
            Icon = AppIcon.Get() ?? Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);
            _close.Text = gate ? "Exit" : "Close";
            CancelButton = _close;

            // Sizes follow the font so the dialog looks right at any display scaling.
            int unit = Font.Height;
            int contentWidth = unit * 30;

            // An unlimited license is final: nothing to request, paste or import, only the message.
            if (current != null && current.IsValid && current.License != null && !current.License.ExpiresDate.HasValue)
            {
                _status.MaximumSize = new Size(contentWidth, 0);
                _status.Text = "License registered: unlimited. " + current.Message;
                _status.ForeColor = Color.DarkGreen;
                var compact = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
                _status.Margin = new Padding(0, 0, 0, 16);
                _close.Anchor = AnchorStyles.Right;
                compact.Controls.Add(_status);
                compact.Controls.Add(_close);
                Controls.Add(compact);
                _close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                return;
            }
            _status.MaximumSize = new Size(contentWidth, 0);
            _requestKey.Width = contentWidth - 70;
            _paste.Size = new Size(contentWidth, unit * 6);

            var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
            _status.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(_status);
            layout.Controls.Add(new Label { Text = "Request key (send this to the administrator to get a license):", AutoSize = true, MaximumSize = new Size(contentWidth, 0) });

            var keyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 12) };
            keyRow.Controls.Add(_requestKey);
            keyRow.Controls.Add(_copy);
            layout.Controls.Add(keyRow);

            layout.Controls.Add(new Label { Text = "Paste the license text here:", AutoSize = true });
            // Shown under the paste box (see below); a license with an expiry date is activated online.
            _paste.Margin = new Padding(0, 4, 0, 6);
            layout.Controls.Add(_paste);
            layout.Controls.Add(new Label
            {
                Text = "A license with an expiry date is activated online: please turn on the internet before activating it.",
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 0, 0, 6)
            });

            // One row of buttons: Use pasted license on the left, Import file and Close on the right.
            var buttons = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, Width = contentWidth, Margin = new Padding(0, 6, 0, 0) };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            right.Controls.Add(_import);
            right.Controls.Add(_close);
            _usePasted.Anchor = AnchorStyles.Left;
            _usePasted.Margin = new Padding(0, 3, 3, 3);
            buttons.Controls.Add(_usePasted, 0, 0);
            buttons.Controls.Add(right, 1, 0);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            try
            {
                _requestKey.Text = LicenseManager.RequestKeyNow();
            }
            catch (InvalidOperationException ex)
            {
                _requestKey.Text = "";
                _copy.Enabled = _import.Enabled = _usePasted.Enabled = _paste.Enabled = false;
                current = new LicenseResult { Status = LicenseStatus.Malformed, Message = ex.Message };
            }
            ShowResult(current);
            Shown += (s, e) => { if (_paste.Enabled) ActiveControl = _paste; }; // so the request key is not shown selected

            _copy.Click += (s, e) => { if (_requestKey.Text.Length > 0) Clipboard.SetText(_requestKey.Text); };
            _import.Click += (s, e) => ImportFile();
            _usePasted.Click += (s, e) => Finish(LicenseManager.ImportText(_paste.Text));
            _close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        }

        private void ShowResult(LicenseResult result)
        {
            _status.Text = result.Message;
            _status.ForeColor = result.IsValid ? Color.DarkGreen : Color.Firebrick;
        }

        private void ImportFile()
        {
            using (var dialog = new OpenFileDialog { Filter = "License (*.lic)|*.lic|All files (*.*)|*.*", Title = "Choose the license file" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Finish(LicenseManager.ImportFile(dialog.FileName));
            }
        }

        private void Finish(LicenseResult result)
        {
            ShowResult(result);
            if (!result.IsValid) return;
            MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
