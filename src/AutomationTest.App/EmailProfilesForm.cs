using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutomationTest.Core.Email;

namespace AutomationTest.App
{
    /// <summary>The list of mail profiles with Insert, Edit and Delete.</summary>
    internal sealed class EmailProfilesForm : Form
    {
        private readonly EmailProfileStore _store;
        private readonly ListView _list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Width = 520, Height = 220 };
        private readonly Button _insert = new Button { Text = "Insert", AutoSize = true };
        private readonly Button _edit = new Button { Text = "Edit", AutoSize = true };
        private readonly Button _delete = new Button { Text = "Delete", AutoSize = true };
        private readonly Button _close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };

        /// <summary>The profile that was last inserted or edited (null if none), so the editor can select it.</summary>
        public string LastName { get; private set; }

        public EmailProfilesForm(EmailProfileStore store)
        {
            _store = store;
            Text = "Email profiles";
            Icon = AppIcon.Get() ?? Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            CancelButton = _close;

            _list.Columns.Add("Name", 140);
            _list.Columns.Add("Send (SMTP)", 150);
            _list.Columns.Add("Read (IMAP)", 150);
            _list.Columns.Add("User", 150);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.AddRange(new Control[] { _insert, _edit, _delete, _close });
            var note = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Text = "Profiles stay on this computer, the password is encrypted for your Windows user. A script only holds the profile name.", Padding = new Padding(0, 6, 0, 0) };
            var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
            layout.Controls.Add(_list);
            layout.Controls.Add(buttons);
            layout.Controls.Add(note);
            Controls.Add(layout);

            _insert.Click += (s, e) => Insert();
            _edit.Click += (s, e) => EditSelected();
            _delete.Click += (s, e) => DeleteSelected();
            _list.DoubleClick += (s, e) => EditSelected();
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            Fill(null);
        }

        private void Fill(string select)
        {
            _list.Items.Clear();
            foreach (var p in _store.Profiles)
            {
                var item = new ListViewItem(p.Name) { Tag = p.Name };
                item.SubItems.Add(p.CanSend ? p.SmtpHost + ":" + p.SmtpPort : "-");
                item.SubItems.Add(p.CanRead ? p.ImapHost + ":" + p.ImapPort : "-");
                item.SubItems.Add(p.User);
                _list.Items.Add(item);
                if (string.Equals(p.Name, select, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _edit.Enabled = _delete.Enabled = _list.SelectedItems.Count == 1;
        }

        private void Insert()
        {
            using (var form = new EmailProfileEditForm(_store, null))
                if (form.ShowDialog(this) == DialogResult.OK) { LastName = form.SavedName; Fill(LastName); }
        }

        private void EditSelected()
        {
            if (_list.SelectedItems.Count != 1) return;
            string name = (string)_list.SelectedItems[0].Tag;
            var profile = _store.Find(name);
            if (profile == null) return;
            using (var form = new EmailProfileEditForm(_store, profile))
                if (form.ShowDialog(this) == DialogResult.OK) { LastName = form.SavedName; Fill(LastName); }
        }

        private void DeleteSelected()
        {
            if (_list.SelectedItems.Count != 1) return;
            string name = (string)_list.SelectedItems[0].Tag;
            if (MessageBox.Show(this, "Delete the profile \"" + name + "\"? Scripts that use it will report that it is missing.", "Email profiles", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { _store.Delete(name); }
            catch (InvalidOperationException ex) { MessageBox.Show(this, ex.Message, "Email profiles", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Fill(null);
        }
    }

    /// <summary>One profile: servers, security, account and password, and a Test button that tries them.</summary>
    internal sealed class EmailProfileEditForm : Form
    {
        private readonly EmailProfileStore _store;
        private readonly string _oldName;
        private readonly TextBox _name = new TextBox { Width = 260 };
        private readonly TextBox _smtpHost = new TextBox { Width = 260 };
        private readonly NumericUpDown _smtpPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 587, Width = 70 };
        private readonly ComboBox _smtpSecurity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        private readonly TextBox _imapHost = new TextBox { Width = 260 };
        private readonly NumericUpDown _imapPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 993, Width = 70 };
        private readonly ComboBox _imapSecurity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        private readonly TextBox _user = new TextBox { Width = 260 };
        private readonly TextBox _password = new TextBox { Width = 260, UseSystemPasswordChar = true };
        private readonly TextBox _fromAddress = new TextBox { Width = 260 };
        private readonly TextBox _fromName = new TextBox { Width = 260 };
        private readonly Button _test = new Button { Text = "Test", AutoSize = true };
        private readonly Button _save = new Button { Text = "Save", AutoSize = true };
        private readonly Button _cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        private readonly TextBox _result = new TextBox { Multiline = true, ReadOnly = true, Width = 420, Height = 70, ScrollBars = ScrollBars.Vertical };
        private CancellationTokenSource _testing;

        public string SavedName { get; private set; }

        public EmailProfileEditForm(EmailProfileStore store, EmailProfile profile)
        {
            _store = store;
            _oldName = profile?.Name;
            Text = profile == null ? "New email profile" : "Edit email profile";
            Icon = AppIcon.Get() ?? Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            CancelButton = _cancel;

            foreach (var box in new[] { _smtpSecurity, _imapSecurity })
                box.Items.AddRange(new object[] { EmailSecurity.StartTls, EmailSecurity.SslTls, EmailSecurity.None });
            _smtpSecurity.SelectedItem = EmailSecurity.StartTls;
            _imapSecurity.SelectedItem = EmailSecurity.SslTls;
            _smtpSecurity.SelectedIndexChanged += (s, e) => { if (_smtpSecurity.SelectedItem is EmailSecurity sec) _smtpPort.Value = sec == EmailSecurity.SslTls ? 465 : sec == EmailSecurity.StartTls ? 587 : 25; };
            _imapSecurity.SelectedIndexChanged += (s, e) => { if (_imapSecurity.SelectedItem is EmailSecurity sec) _imapPort.Value = sec == EmailSecurity.SslTls ? 993 : 143; };

            if (profile != null)
            {
                _name.Text = profile.Name;
                _smtpHost.Text = profile.SmtpHost; _smtpPort.Value = Math.Max(1, Math.Min(65535, profile.SmtpPort)); _smtpSecurity.SelectedItem = profile.SmtpSecurity; _smtpPort.Value = Math.Max(1, Math.Min(65535, profile.SmtpPort));
                _imapHost.Text = profile.ImapHost; _imapSecurity.SelectedItem = profile.ImapSecurity; _imapPort.Value = Math.Max(1, Math.Min(65535, profile.ImapPort));
                _user.Text = profile.User; _password.Text = profile.Password;
                _fromAddress.Text = profile.FromAddress; _fromName.Text = profile.FromName;
            }

            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
            Row(table, "Profile name", _name);
            Row(table, "SMTP server (send)", _smtpHost);
            Row(table, "  port / security", Pair(_smtpPort, _smtpSecurity));
            Row(table, "IMAP server (read)", _imapHost);
            Row(table, "  port / security", Pair(_imapPort, _imapSecurity));
            Row(table, "User", _user);
            Row(table, "Password", _password);
            Row(table, "From address", _fromAddress);
            Row(table, "From name", _fromName);
            var note = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Padding = new Padding(0, 6, 0, 6),
                Text = "Leave a server empty if you only send or only read. Gmail and Outlook.com need an app password (not your normal password)." };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            buttons.Controls.AddRange(new Control[] { _test, _save, _cancel });
            var layout = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            layout.Controls.AddRange(new Control[] { table, note, buttons, _result });
            Controls.Add(layout);

            _test.Click += async (s, e) => await RunTest();
            _save.Click += (s, e) => Save();
            FormClosing += (s, e) => { try { _testing?.Cancel(); } catch (ObjectDisposedException) { } };
        }

        private static void Row(TableLayoutPanel table, string label, Control field)
        {
            int row = table.RowCount++;
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) }, 0, row);
            table.Controls.Add(field, 1, row);
        }

        private static Control Pair(Control a, Control b)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            panel.Controls.Add(a);
            panel.Controls.Add(b);
            return panel;
        }

        private EmailProfile Read()
        {
            return new EmailProfile
            {
                Name = _name.Text.Trim(),
                SmtpHost = _smtpHost.Text.Trim(), SmtpPort = (int)_smtpPort.Value, SmtpSecurity = (EmailSecurity)_smtpSecurity.SelectedItem,
                ImapHost = _imapHost.Text.Trim(), ImapPort = (int)_imapPort.Value, ImapSecurity = (EmailSecurity)_imapSecurity.SelectedItem,
                User = _user.Text.Trim(), Password = _password.Text,
                FromAddress = _fromAddress.Text.Trim(), FromName = _fromName.Text.Trim()
            };
        }

        private async Task RunTest()
        {
            var profile = Read();
            if (profile.Name.Length == 0) profile.Name = "(new profile)";
            _test.Enabled = _save.Enabled = false;
            _result.Text = "Testing...";
            _testing = new CancellationTokenSource();
            var token = _testing.Token;
            try
            {
                string text = await Task.Run(() => EmailClient.Test(profile, 20000, token));
                if (!IsDisposed) _result.Text = text;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed) _result.Text = "Test failed: " + ex.Message.Replace(profile.Password.Length > 0 ? profile.Password : "\u0001", "****"); }
            finally { if (!IsDisposed) _test.Enabled = _save.Enabled = true; }
        }

        private void Save()
        {
            try
            {
                var profile = Read();
                if (!profile.CanSend && !profile.CanRead) throw new InvalidOperationException("Enter an SMTP server, an IMAP server, or both.");
                _store.Save(profile, _oldName);
                SavedName = profile.Name;
                DialogResult = DialogResult.OK;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "Email profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
