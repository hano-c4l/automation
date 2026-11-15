using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AutomationTest.Core.Email;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.App
{
    /// <summary>The Email part of the editor: the mail icon next to Category, the profile list and the fields of an Email action.</summary>
    public sealed partial class MainForm
    {
        private readonly EmailProfileStore _emailStore = new EmailProfileStore();
        private readonly Button _mailButton = new Button { AutoSize = true, Text = "✉", Font = new Font("Segoe UI Symbol", 10f), Visible = false, Margin = new Padding(0) };
        private readonly GroupBox _emailBox = new GroupBox { Text = "Email", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };

        private readonly ComboBox _emailKind = MakeCombo(typeof(EmailKind));
        private readonly ComboBox _emailProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly HintTextBox _emailTo = new HintTextBox { Width = 200, Placeholder = "To (a, b; c)" };
        private readonly HintTextBox _emailCc = new HintTextBox { Width = 200, Placeholder = "Cc" };
        private readonly HintTextBox _emailBcc = new HintTextBox { Width = 200, Placeholder = "Bcc" };
        private readonly ToggleSwitch _emailToExpr = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly HintTextBox _emailSubject = new HintTextBox { Width = 200, Placeholder = "Subject" };
        private readonly ToggleSwitch _emailSubjectExpr = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly HintTextBox _emailBody = new HintTextBox { Width = 200, Height = 100, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Placeholder = "Message" };
        private readonly ToggleSwitch _emailBodyExpr = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ToggleSwitch _emailHtml = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly HintTextBox _emailAttach = new HintTextBox { Width = 200, Height = 50, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Placeholder = "Files to attach, one per line" };
        private readonly ToggleSwitch _emailAttachExpr = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly HintTextBox _emailFolder = new HintTextBox { Width = 200, Placeholder = "Folder (empty = INBOX)" };
        private readonly HintTextBox _emailFrom = new HintTextBox { Width = 200, Placeholder = "Sender contains" };
        private readonly HintTextBox _emailSince = new HintTextBox { Width = 200, Placeholder = "From day (yyyy-MM-dd)" };
        private readonly HintTextBox _emailBefore = new HintTextBox { Width = 200, Placeholder = "Before day (yyyy-MM-dd)" };
        private readonly ToggleSwitch _emailDatesExpr = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ComboBox _emailStatus = MakeCombo(typeof(EmailStatus));
        private readonly NumericUpDown _emailMax = MakeNumber(1, 10000, 10);
        private readonly ToggleSwitch _emailMarkRead = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly HintTextBox _emailSaveTo = new HintTextBox { Width = 200, Placeholder = "Folder for attachments (optional)" };
        private readonly ComboBox _emailMarkAs = MakeCombo(typeof(EmailMarkAs));
        private readonly ToggleSwitch _emailPermanent = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly HintTextBox _emailId = new HintTextBox { Width = 200, Placeholder = "Variable ID" };
        private readonly TimeBox _emailTime = new TimeBox(60000);
        private readonly TimeBox _emailDelay = new TimeBox(0);

        private void BuildEmailEditor(TableLayoutPanel common)
        {
            var t = NewTable();
            AddRow(t, "ID", _emailId, Help.EmailId); // an ID is always the first field of its group
            AddRow(t, "Type", _emailKind, Help.EmailKind);
            AddRow(t, "Profile", _emailProfile, Help.EmailProfile);
            AddRow(t, "To", _emailTo, Help.EmailTo);
            AddRow(t, "Cc", _emailCc, Help.EmailTo);
            AddRow(t, "Bcc", _emailBcc, Help.EmailTo);
            AddRow(t, "Expr. (to)", _emailToExpr, Help.ExpressionText);
            AddRow(t, "Subject", _emailSubject, Help.EmailSubject);
            AddRow(t, "Expr. (subject)", _emailSubjectExpr, Help.ExpressionText);
            AddRow(t, "Message", _emailBody, Help.EmailBody);
            AddRow(t, "Expr. (message)", _emailBodyExpr, Help.ExpressionText);
            AddRow(t, "HTML", _emailHtml, Help.EmailBody);
            AddRow(t, "Attach", _emailAttach, Help.EmailAttach);
            AddRow(t, "Expr. (files)", _emailAttachExpr, Help.ExpressionText);
            AddRow(t, "Folder", _emailFolder, Help.EmailFolder);
            AddRow(t, "From", _emailFrom, Help.EmailFilter);
            AddRow(t, "Since", _emailSince, Help.EmailDates);
            AddRow(t, "Before", _emailBefore, Help.EmailDates);
            AddRow(t, "Expr. (dates)", _emailDatesExpr, Help.ExpressionText);
            AddRow(t, "Status", _emailStatus, Help.EmailFilter);
            AddRow(t, "Max", _emailMax, Help.EmailMax);
            AddRow(t, "Mark as read", _emailMarkRead, Help.EmailMarkRead);
            AddRow(t, "Save files to", _emailSaveTo, Help.EmailSaveTo);
            AddRow(t, "Mark as", _emailMarkAs, Help.EmailFilter);
            AddRow(t, "For good", _emailPermanent, Help.EmailDelete);
            AddRow(t, "Timeout", _emailTime, Help.EmailTimeout);
            AddRow(t, "Delay", _emailDelay, Help.KeyDelay);
            _emailBox.Controls.Add(t);
            Space(_emailKind, 12);
            Space(_emailTime, 12);

            _mailButton.Click += (s, e) => EditEmailProfiles();
            _emailKind.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _emailProfile.DropDown += (s, e) => RefreshEmailProfiles(null);
            _tips.SetToolTip(_mailButton, "Email profiles: the mail accounts (SMTP / IMAP) that Email actions use");
            RefreshEmailProfiles(null);
        }

        /// <summary>Fills the profile list; <paramref name="select"/> (or the profile that was chosen) stays chosen, even when it is not on this computer.</summary>
        private void RefreshEmailProfiles(string select)
        {
            string keep = select ?? _emailProfile.Text;
            _emailProfile.Items.Clear();
            foreach (string name in _emailStore.Names) _emailProfile.Items.Add(name);
            SetEmailProfile(keep);
        }

        private void SetEmailProfile(string name)
        {
            name = (name ?? "").Trim();
            int found = -1;
            for (int i = 0; i < _emailProfile.Items.Count; i++)
                if (string.Equals((string)_emailProfile.Items[i], name, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
            if (found < 0 && name.Length > 0) found = _emailProfile.Items.Add(name); // a script made on another computer: keep its name
            _emailProfile.SelectedIndex = found;
            if (found < 0 && _emailProfile.Items.Count > 0) _emailProfile.SelectedIndex = 0;
        }

        private void EditEmailProfiles()
        {
            using (var form = new EmailProfilesForm(_emailStore))
            {
                form.ShowDialog(this);
                RefreshEmailProfiles(form.LastName);
            }
        }

        /// <summary>Shows the fields of the chosen type of Email action.</summary>
        private void UpdateEmailVisibility(ActionCategory category)
        {
            bool on = category == ActionCategory.Email;
            _emailBox.Visible = on;
            _mailButton.Visible = on;
            var kind = _emailKind.SelectedItem as EmailKind? ?? EmailKind.Send;
            bool send = on && kind == EmailKind.Send, filter = on && kind != EmailKind.Send;
            SetRowVisible(_emailId, on && kind != EmailKind.Send);
            SetLabel(_emailId, kind == EmailKind.Read ? "ID (table)" : "ID (count)");
            SetRowVisible(_emailKind, on);
            SetRowVisible(_emailProfile, on);
            SetRowVisible(_emailTo, send); SetRowVisible(_emailCc, send); SetRowVisible(_emailBcc, send); SetRowVisible(_emailToExpr, send);
            SetRowVisible(_emailSubject, on);
            SetLabel(_emailSubject, send ? "Subject" : "Subject has");
            SetRowVisible(_emailSubjectExpr, on);
            SetRowVisible(_emailBody, send); SetRowVisible(_emailBodyExpr, send); SetRowVisible(_emailHtml, send);
            SetRowVisible(_emailAttach, send); SetRowVisible(_emailAttachExpr, send);
            SetRowVisible(_emailFolder, filter); SetRowVisible(_emailFrom, filter);
            SetRowVisible(_emailSince, filter); SetRowVisible(_emailBefore, filter); SetRowVisible(_emailDatesExpr, filter);
            SetRowVisible(_emailStatus, filter); SetRowVisible(_emailMax, filter);
            SetRowVisible(_emailMarkRead, on && kind == EmailKind.Read);
            SetRowVisible(_emailSaveTo, on && kind == EmailKind.Read);
            SetRowVisible(_emailMarkAs, on && kind == EmailKind.Mark);
            SetRowVisible(_emailPermanent, on && kind == EmailKind.Delete);
            SetRowVisible(_emailTime, on); SetRowVisible(_emailDelay, on);
        }

        private ActionNode BuildEmailNode()
        {
            var kind = (EmailKind)_emailKind.SelectedItem;
            return ActionNode.NewEmail(new EmailSettings
            {
                Kind = kind,
                Profile = (_emailProfile.SelectedItem as string ?? "").Trim(),
                To = _emailTo.Text.Trim(), Cc = _emailCc.Text.Trim(), Bcc = _emailBcc.Text.Trim(), ToIsExpression = _emailToExpr.Checked,
                Subject = _emailSubject.Text, SubjectIsExpression = _emailSubjectExpr.Checked,
                Body = _emailBody.Text, BodyIsExpression = _emailBodyExpr.Checked, BodyIsHtml = _emailHtml.Checked,
                Attachments = _emailAttach.Text.Trim(), AttachmentsIsExpression = _emailAttachExpr.Checked,
                Folder = _emailFolder.Text.Trim(), From = _emailFrom.Text.Trim(),
                Since = _emailSince.Text.Trim(), Before = _emailBefore.Text.Trim(), DatesAreExpressions = _emailDatesExpr.Checked,
                Status = (EmailStatus)_emailStatus.SelectedItem, Max = (int)_emailMax.Value,
                MarkRead = _emailMarkRead.Checked, SaveTo = _emailSaveTo.Text.Trim(),
                MarkAs = (EmailMarkAs)_emailMarkAs.SelectedItem, Permanent = _emailPermanent.Checked,
                Id = _emailId.Text.Trim(), TimeMs = _emailTime.Milliseconds, DelayMs = _emailDelay.Milliseconds
            });
        }

        private void LoadEmailNode(EmailSettings m)
        {
            _emailKind.SelectedItem = m.Kind;
            SetEmailProfile(m.Profile);
            _emailTo.Text = m.To; _emailCc.Text = m.Cc; _emailBcc.Text = m.Bcc; _emailToExpr.Checked = m.ToIsExpression;
            _emailSubject.Text = m.Subject; _emailSubjectExpr.Checked = m.SubjectIsExpression;
            _emailBody.Text = m.Body; _emailBodyExpr.Checked = m.BodyIsExpression; _emailHtml.Checked = m.BodyIsHtml;
            _emailAttach.Text = m.Attachments; _emailAttachExpr.Checked = m.AttachmentsIsExpression;
            _emailFolder.Text = m.Folder; _emailFrom.Text = m.From;
            _emailSince.Text = m.Since; _emailBefore.Text = m.Before; _emailDatesExpr.Checked = m.DatesAreExpressions;
            _emailStatus.SelectedItem = m.Status; _emailMax.Value = Math.Max(_emailMax.Minimum, Math.Min(_emailMax.Maximum, m.Max));
            _emailMarkRead.Checked = m.MarkRead; _emailSaveTo.Text = m.SaveTo;
            _emailMarkAs.SelectedItem = m.MarkAs; _emailPermanent.Checked = m.Permanent;
            _emailId.Text = m.Id; _emailTime.Milliseconds = m.TimeMs; _emailDelay.Milliseconds = m.DelayMs;
        }

        private static string ValidateEmail(EmailSettings m)
        {
            if (m.Profile.Length == 0) return "Choose the profile to use first (the mail icon next to Category adds profiles).";
            string id = m.Id.Trim();
            if (m.Kind != EmailKind.Send && id.Length > 0 && !Expression.IsValidId(id)) return "\"" + m.Id + "\" is not a valid ID.";
            if (m.Kind == EmailKind.Send)
            {
                if (m.To.Trim().Length == 0 && m.Cc.Trim().Length == 0 && m.Bcc.Trim().Length == 0) return "Enter at least one recipient (To).";
                if (m.ToIsExpression) { Expression.CheckSyntax(m.To); if (m.Cc.Length > 0) Expression.CheckSyntax(m.Cc); if (m.Bcc.Length > 0) Expression.CheckSyntax(m.Bcc); }
                else
                {
                    try
                    {
                        EmailClient.ParseAddresses(m.To, "To"); EmailClient.ParseAddresses(m.Cc, "Cc"); EmailClient.ParseAddresses(m.Bcc, "Bcc");
                    }
                    catch (InvalidOperationException ex) { return ex.Message; }
                }
                if (m.BodyIsExpression && m.Body.Length > 0) Expression.CheckSyntax(m.Body);
                if (m.AttachmentsIsExpression && m.Attachments.Length > 0) Expression.CheckSyntax(m.Attachments);
            }
            else
            {
                if (m.DatesAreExpressions)
                {
                    if (m.Since.Length > 0) Expression.CheckSyntax(m.Since);
                    if (m.Before.Length > 0) Expression.CheckSyntax(m.Before);
                }
                else
                {
                    try { EmailClient.ParseDay(m.Since, "Since"); EmailClient.ParseDay(m.Before, "Before"); }
                    catch (InvalidOperationException ex) { return ex.Message; }
                }
                if (m.Kind == EmailKind.Delete && !ScriptRunner.EmailHasFilter(m))
                    return "Delete needs at least one filter (subject, sender, date or status) so that the whole folder is not deleted.";
            }
            if (m.SubjectIsExpression && m.Subject.Length > 0) Expression.CheckSyntax(m.Subject);
            return null;
        }

        private static string EmailDetails(EmailSettings m)
        {
            string where = m.Profile.Length > 0 ? "[" + m.Profile + "] " : "";
            switch (m.Kind)
            {
                case EmailKind.Send:
                    return where + "to " + Shorten(m.To) + ": " + Shorten(m.Subject);
                default:
                    var parts = new List<string>();
                    if (m.Subject.Length > 0) parts.Add("subject has " + Shorten(m.Subject));
                    if (m.From.Length > 0) parts.Add("from " + Shorten(m.From));
                    if (m.Since.Length > 0) parts.Add("since " + m.Since);
                    if (m.Before.Length > 0) parts.Add("before " + m.Before);
                    if (m.Status != EmailStatus.Any) parts.Add(m.Status.ToString().ToLowerInvariant());
                    string filter = parts.Count == 0 ? "all" : string.Join(", ", parts);
                    string rest = m.Kind == EmailKind.Read ? " -> " + m.Id : m.Kind == EmailKind.Mark ? " as " + m.MarkAs.ToString().ToLowerInvariant() : m.Permanent ? " (for good)" : "";
                    return where + filter + " (max " + m.Max + ")" + rest;
            }
        }
    }
}
