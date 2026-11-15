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
        private readonly ComboBox _auth = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly TextBox _clientId = new TextBox { Width = 260 };
        private readonly TextBox _clientSecret = new TextBox { Width = 260, UseSystemPasswordChar = true };
        private readonly TextBox _tenant = new TextBox { Width = 260 };
        private readonly Button _signIn = new Button { Text = "Sign in...", AutoSize = true };
        private readonly Label _signInState = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        private readonly Label _passwordLabel = new Label { Text = "Password", AutoSize = true, Padding = new Padding(0, 6, 8, 0) };
        private readonly Label _secretLabel = new Label { Text = "Client secret", AutoSize = true, Padding = new Padding(0, 6, 8, 0) };
        private readonly Label _tenantLabel = new Label { Text = "Tenant", AutoSize = true, Padding = new Padding(0, 6, 8, 0) };
        private readonly Label _clientLabel = new Label { Text = "Client ID", AutoSize = true, Padding = new Padding(0, 6, 8, 0) };
        private readonly Label _signInLabel = new Label { Text = "Sign in", AutoSize = true, Padding = new Padding(0, 6, 8, 0) };
        private readonly Label _authNote = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Padding = new Padding(0, 4, 0, 4) };
        private string _refreshToken = "";
        private string _customAuthUrl = "", _customTokenUrl = "", _customScope = "";
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

            _auth.Items.AddRange(new object[] { "Password (or app password)", "Google (sign in, supports MFA)", "Microsoft (sign in, supports MFA)" });
            _auth.SelectedIndex = 0;
            if (profile != null && profile.Auth == EmailAuth.Custom) _auth.Items.Add("Custom (set in Setting.ini)");
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
                _auth.SelectedIndex = profile.Auth == EmailAuth.Google ? 1 : profile.Auth == EmailAuth.Microsoft ? 2 : profile.Auth == EmailAuth.Custom ? 3 : 0;
                _clientId.Text = profile.ClientId; _clientSecret.Text = profile.ClientSecret; _tenant.Text = profile.Tenant;
                _refreshToken = profile.RefreshToken;
                _customAuthUrl = profile.AuthUrl; _customTokenUrl = profile.TokenUrl; _customScope = profile.Scope;
                _fromAddress.Text = profile.FromAddress; _fromName.Text = profile.FromName;
            }

            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
            Row(table, "Profile name", _name);
            Row(table, "SMTP server (send)", _smtpHost);
            Row(table, "  port / security", Pair(_smtpPort, _smtpSecurity));
            Row(table, "IMAP server (read)", _imapHost);
            Row(table, "  port / security", Pair(_imapPort, _imapSecurity));
            Row(table, "Login with", _auth);
            Row(table, "User", _user);
            Row(table, _passwordLabel, _password);
            Row(table, _clientLabel, _clientId);
            Row(table, _secretLabel, _clientSecret);
            Row(table, _tenantLabel, _tenant);
            Row(table, _signInLabel, Pair(_signIn, _signInState));
            Row(table, "From address", _fromAddress);
            Row(table, "From name", _fromName);
            var note = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Padding = new Padding(0, 6, 0, 6),
                Text = "Leave a server empty if you only send or only read. Gmail and Outlook.com need an app password (not your normal password)." };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            buttons.Controls.AddRange(new Control[] { _test, _save, _cancel });
            _signIn.Click += async (s, e) => await RunSignIn();
            _auth.SelectedIndexChanged += (s, e) => { _refreshToken = ""; UpdateAuthRows(); };
            _clientId.TextChanged += (s, e) => ForgetSignIn();
            _tenant.TextChanged += (s, e) => ForgetSignIn();
            _user.TextChanged += (s, e) => ForgetSignIn();
            UpdateAuthRows();
            var layout = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            layout.Controls.AddRange(new Control[] { table, _authNote, note, buttons, _result });
            Controls.Add(layout);

            _test.Click += async (s, e) => await RunTest();
            _save.Click += (s, e) => Save();
            FormClosing += (s, e) => { try { _testing?.Cancel(); _signingIn?.Cancel(); } catch (ObjectDisposedException) { } };
        }

        private static void Row(TableLayoutPanel table, Label label, Control field)
        {
            int row = table.RowCount++;
            label.Anchor = AnchorStyles.Left;
            table.Controls.Add(label, 0, row);
            table.Controls.Add(field, 1, row);
        }

        private EmailAuth AuthMode
        {
            get { return _auth.SelectedIndex == 1 ? EmailAuth.Google : _auth.SelectedIndex == 2 ? EmailAuth.Microsoft : _auth.SelectedIndex == 3 ? EmailAuth.Custom : EmailAuth.Password; }
        }

        /// <summary>Shows the fields of the chosen way to log in.</summary>
        private void UpdateAuthRows()
        {
            var mode = AuthMode;
            bool oauth = mode != EmailAuth.Password;
            _passwordLabel.Visible = _password.Visible = !oauth;
            foreach (Control c in new Control[] { _clientLabel, _clientId, _signIn, _signInState, _signInLabel }) c.Visible = oauth;
            _secretLabel.Visible = _clientSecret.Visible = mode == EmailAuth.Google || mode == EmailAuth.Custom;
            _tenantLabel.Visible = _tenant.Visible = mode == EmailAuth.Microsoft;
            _signInState.Text = _refreshToken.Length > 0 ? "Signed in" : "Not signed in";
            _authNote.Text = mode == EmailAuth.Password
                ? "An account with two-step verification (MFA) cannot answer a code here: use an app password from your provider (Gmail, Yahoo, iCloud), or choose a sign-in option above."
                : mode == EmailAuth.Google
                    ? "User = your Gmail address. Make an OAuth client of type Desktop app in Google Cloud Console (Gmail API) and paste its Client ID and secret. While the app is in Testing, Google asks you to sign in again after 7 days; an app password avoids that."
                    : mode == EmailAuth.Custom ? "Another OAuth2 provider: its addresses and scope are in Setting.ini. Sign in works as for the others."
                    : "User = your mailbox address. Register an app in Microsoft Entra (public client, redirect http://localhost, permissions IMAP.AccessAsUser.All and SMTP.Send) and paste its Application (client) ID. Tenant: leave empty, or give your directory ID. SMTP AUTH must be enabled for the mailbox.";
        }

        private CancellationTokenSource _signingIn;

        /// <summary>Another account, client or directory: the earlier sign-in does not apply.</summary>
        private void ForgetSignIn()
        {
            if (_refreshToken.Length == 0 || _signingIn != null) return;
            _refreshToken = "";
            UpdateAuthRows();
        }

        private async Task RunSignIn()
        {
            if (_signingIn != null) { _signingIn.Cancel(); return; } // the button is Cancel while waiting
            var profile = Read();
            if (profile.Name.Length == 0) profile.Name = "(new profile)";
            if (profile.ClientId.Length == 0) { _result.Text = "Enter the Client ID first."; return; }
            _signingIn = new CancellationTokenSource();
            var token = _signingIn.Token;
            _signIn.Text = "Cancel";
            _test.Enabled = _save.Enabled = false;
            _result.Text = "Waiting for you to sign in in the browser (five minutes)...";
            try
            {
                string refresh = await Task.Run(() => OAuth2.SignIn(profile, url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }), TimeSpan.FromMinutes(5), token));
                _refreshToken = refresh;
                if (!IsDisposed) _result.Text = "Signed in. Press Save to keep it, or Test to try the connection.";
            }
            catch (OperationCanceledException) { if (!IsDisposed) _result.Text = "Sign-in cancelled."; }
            catch (Exception ex) { if (!IsDisposed) _result.Text = ex.Message; }
            finally
            {
                _signingIn.Dispose();
                _signingIn = null;
                if (!IsDisposed) { _signIn.Text = "Sign in..."; _test.Enabled = _save.Enabled = true; UpdateAuthRows(); }
            }
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
                User = _user.Text.Trim(), Password = AuthMode == EmailAuth.Password ? _password.Text : "",
                Auth = AuthMode, ClientId = _clientId.Text.Trim(), ClientSecret = AuthMode == EmailAuth.Google || AuthMode == EmailAuth.Custom ? _clientSecret.Text : "", Tenant = AuthMode == EmailAuth.Microsoft ? _tenant.Text.Trim() : "",
                RefreshToken = AuthMode == EmailAuth.Password ? "" : _refreshToken,
                AuthUrl = _customAuthUrl, TokenUrl = _customTokenUrl, Scope = _customScope,
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
                if (profile.UsesOAuth && profile.RefreshToken.Length > 0) _refreshToken = profile.RefreshToken; // the provider may have sent a new one
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
                if (profile.UsesOAuth && (profile.ClientId.Length == 0 || profile.User.Length == 0)) throw new InvalidOperationException("Enter the user (your address) and the Client ID, then press Sign in.");
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
