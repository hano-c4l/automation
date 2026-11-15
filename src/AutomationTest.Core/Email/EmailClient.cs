using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Model;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace AutomationTest.Core.Email
{
    /// <summary>How a mail server connection is secured.</summary>
    public enum EmailSecurity
    {
        /// <summary>Not encrypted: only for a server on your own computer or network; a password is never sent this way.</summary>
        None,
        /// <summary>Starts plain and switches to encryption (STARTTLS, usually port 587 for SMTP and 143 for IMAP).</summary>
        StartTls,
        /// <summary>Encrypted from the first byte (SSL/TLS, usually port 465 for SMTP and 993 for IMAP).</summary>
        SslTls
    }

    /// <summary>One mail account: the servers to send and to read with, and who to log in as.</summary>
    public sealed class EmailProfile
    {
        public string Name = "";
        public string SmtpHost = "";
        public int SmtpPort = 587;
        public EmailSecurity SmtpSecurity = EmailSecurity.StartTls;
        public string ImapHost = "";
        public int ImapPort = 993;
        public EmailSecurity ImapSecurity = EmailSecurity.SslTls;
        public string User = "";
        public string Password = "";
        /// <summary>The address messages are sent from (empty = the user name when that is an address).</summary>
        public string FromAddress = "";
        public string FromName = "";

        public bool CanSend { get { return SmtpHost.Trim().Length > 0; } }
        public bool CanRead { get { return ImapHost.Trim().Length > 0; } }

        public EmailProfile Clone() { return (EmailProfile)MemberwiseClone(); }
    }

    /// <summary>Gives the profiles by name (the program keeps them; they hold passwords, so they are not in script files).</summary>
    public interface IEmailProfiles
    {
        /// <summary>The profile with that name (letter case does not matter); null when there is none.</summary>
        EmailProfile Find(string name);
        IList<string> Names { get; }
    }

    /// <summary>What a Read returns for each message (the columns of the table, in this order).</summary>
    public static class EmailColumns
    {
        public const int From = 1, Subject = 2, Date = 3, Unread = 4, Body = 5, Attachments = 6;
        public const int Count = 6;
    }

    /// <summary>
    /// Sends and reads mail with SMTP and IMAP (MailKit). The certificate of an encrypted server is checked. A problem is an
    /// <see cref="InvalidOperationException"/> that says in words what went wrong, never with the password.
    /// </summary>
    public static class EmailClient
    {
        // ------------------------------------------------------------------ what the pure parts do (tested without a server)

        /// <summary>Addresses separated by commas, semicolons or lines.</summary>
        public static List<MailboxAddress> ParseAddresses(string text, string what)
        {
            var list = new List<MailboxAddress>();
            foreach (string part in (text ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string address = part.Trim();
                if (address.Length == 0) continue;
                MailboxAddress parsed;
                if (!MailboxAddress.TryParse(address, out parsed) || string.IsNullOrEmpty(parsed.Address) || parsed.Address.IndexOf('@') < 0)
                    throw new InvalidOperationException("Email: \"" + address + "\" is not an email address (" + what + ").");
                list.Add(parsed);
            }
            return list;
        }

        public static MimeMessage BuildMessage(EmailProfile profile, string to, string cc, string bcc, string subject, string body, bool html, IList<string> attachments)
        {
            var message = new MimeMessage();
            string fromAddress = (profile.FromAddress ?? "").Trim();
            if (fromAddress.Length == 0 && (profile.User ?? "").IndexOf('@') > 0) fromAddress = profile.User.Trim();
            if (fromAddress.Length == 0) throw new InvalidOperationException("Email: the profile \"" + profile.Name + "\" has no From address (and its user name is not an address).");
            var sender = ParseAddresses(fromAddress, "the From address of the profile");
            if (sender.Count == 0) throw new InvalidOperationException("Email: the profile \"" + profile.Name + "\" has no From address.");
            message.From.Add(new MailboxAddress((profile.FromName ?? "").Trim(), sender[0].Address));
            var recipients = ParseAddresses(to, "To");
            var copies = ParseAddresses(cc, "Cc");
            var hidden = ParseAddresses(bcc, "Bcc");
            if (recipients.Count + copies.Count + hidden.Count == 0) throw new InvalidOperationException("Email: enter at least one recipient (To).");
            message.To.AddRange(recipients);
            message.Cc.AddRange(copies);
            message.Bcc.AddRange(hidden);
            message.Subject = subject ?? "";

            var builder = new BodyBuilder();
            if (html) builder.HtmlBody = body ?? ""; else builder.TextBody = body ?? "";
            foreach (string path in attachments ?? new string[0])
            {
                string full;
                try { full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())); }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                { throw new InvalidOperationException("Email: \"" + path.Trim() + "\" is not a valid file path."); }
                if (!File.Exists(full)) throw new InvalidOperationException("Email: the file to attach \"" + full + "\" does not exist.");
                try { builder.Attachments.Add(full); }
                catch (IOException ex) { throw new InvalidOperationException("Email: cannot read \"" + full + "\": " + ex.Message); }
                catch (UnauthorizedAccessException ex) { throw new InvalidOperationException("Email: cannot read \"" + full + "\": " + ex.Message); }
            }
            message.Body = builder.ToMessageBody();
            return message;
        }

        /// <summary>The IMAP search of the filters: subject and sender contain, received on or after Since and before Before, read or unread.</summary>
        public static SearchQuery BuildQuery(string subject, string from, DateTime? since, DateTime? before, EmailStatus status)
        {
            SearchQuery query = SearchQuery.All;
            if (!string.IsNullOrWhiteSpace(subject)) query = query.And(SearchQuery.SubjectContains(subject.Trim()));
            if (!string.IsNullOrWhiteSpace(from)) query = query.And(SearchQuery.FromContains(from.Trim()));
            if (since.HasValue) query = query.And(SearchQuery.DeliveredAfter(since.Value.Date)); // IMAP SINCE: that day and later
            if (before.HasValue) query = query.And(SearchQuery.DeliveredBefore(before.Value.Date));
            if (status == EmailStatus.Unread) query = query.And(SearchQuery.NotSeen);
            else if (status == EmailStatus.Read) query = query.And(SearchQuery.Seen);
            return query;
        }

        /// <summary>A day typed as yyyy-MM-dd (empty = none).</summary>
        public static DateTime? ParseDay(string text, string what)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            DateTime day;
            if (!DateTime.TryParseExact(text.Trim(), new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out day))
                throw new InvalidOperationException("Email: " + what + " \"" + text.Trim() + "\" is not a day (write it as yyyy-MM-dd, for example 2026-11-30).");
            return day;
        }

        /// <summary>The text of an HTML body, roughly (for a message that has no plain text part).</summary>
        public static string HtmlToText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string text = Regex.Replace(html, @"<(script|style)\b.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<br\s*/?>|</p\s*>|</div\s*>|</tr\s*>|</h\d\s*>|</li\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", "");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"[ \t ]+", " ");
            text = Regex.Replace(text, @"\s*\n\s*", "\n");
            return text.Trim();
        }

        /// <summary>One row of the table a Read returns (see <see cref="EmailColumns"/>).</summary>
        public static object[] ToRow(MimeMessage message, bool unread, IList<string> attachmentNames)
        {
            string body = message.TextBody;
            if (string.IsNullOrEmpty(body)) body = HtmlToText(message.HtmlBody);
            body = (body ?? "").Replace("\r\n", "\n").Trim();
            if (body.Length > 200000) body = body.Substring(0, 200000);
            string from = message.From.Mailboxes.Select(m => m.Address).FirstOrDefault() ?? "";
            return new object[]
            {
                from,
                message.Subject ?? "",
                message.Date == DateTimeOffset.MinValue ? (object)"" : message.Date.LocalDateTime,
                unread,
                body,
                string.Join("|", attachmentNames)
            };
        }

        // ------------------------------------------------------------------ connections

        private static SecureSocketOptions Options(EmailSecurity security)
        {
            return security == EmailSecurity.None ? SecureSocketOptions.None : security == EmailSecurity.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        }

        private static InvalidOperationException Failure(string what, EmailProfile profile, Exception ex)
        {
            Exception inner = ex.GetBaseException();
            string text = inner.Message;
            if (!string.IsNullOrEmpty(profile.Password)) text = text.Replace(profile.Password, "****");
            if (inner is AuthenticationException) text = "the server did not accept the user name and password (" + text + ")";
            else if (inner is System.Security.Authentication.AuthenticationException) text = "the secure connection could not be made (" + text + "); check the port and the security setting";
            else if (inner is System.Net.Sockets.SocketException) text = "cannot connect (" + text + ")";
            return new InvalidOperationException("Email " + what + " (" + profile.Name + "): " + text);
        }

        private static void CheckSecurity(EmailProfile profile, EmailSecurity security, string host)
        {
            if (security == EmailSecurity.None && !string.IsNullOrEmpty(profile.User) && !string.IsNullOrEmpty(profile.Password))
                throw new InvalidOperationException("Email (" + profile.Name + "): the password would be sent without encryption to " + host + ". Choose StartTls or SslTls for this server.");
        }

        private static SmtpClient ConnectSmtp(EmailProfile profile, int timeoutMs, CancellationToken token)
        {
            if (!profile.CanSend) throw new InvalidOperationException("Email: the profile \"" + profile.Name + "\" has no SMTP server, so it cannot send.");
            CheckSecurity(profile, profile.SmtpSecurity, profile.SmtpHost);
            var client = new SmtpClient { Timeout = timeoutMs };
            try
            {
                client.Connect(profile.SmtpHost.Trim(), profile.SmtpPort, Options(profile.SmtpSecurity), token);
                if (!string.IsNullOrEmpty(profile.User)) client.Authenticate(profile.User, profile.Password, token);
                return client;
            }
            catch (OperationCanceledException) { client.Dispose(); throw; }
            catch (Exception ex) { client.Dispose(); throw Failure("send", profile, ex); }
        }

        private static ImapClient ConnectImap(EmailProfile profile, int timeoutMs, CancellationToken token)
        {
            if (!profile.CanRead) throw new InvalidOperationException("Email: the profile \"" + profile.Name + "\" has no IMAP server, so it cannot read.");
            CheckSecurity(profile, profile.ImapSecurity, profile.ImapHost);
            var client = new ImapClient { Timeout = timeoutMs };
            try
            {
                client.Connect(profile.ImapHost.Trim(), profile.ImapPort, Options(profile.ImapSecurity), token);
                if (!string.IsNullOrEmpty(profile.User)) client.Authenticate(profile.User, profile.Password, token);
                return client;
            }
            catch (OperationCanceledException) { client.Dispose(); throw; }
            catch (Exception ex) { client.Dispose(); throw Failure("read", profile, ex); }
        }

        // ------------------------------------------------------------------ the actions

        public static void Send(EmailProfile profile, MimeMessage message, int timeoutMs, CancellationToken token)
        {
            using (var client = ConnectSmtp(profile, timeoutMs, token))
            {
                try { client.Send(message, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { throw Failure("send", profile, ex); }
                try { client.Disconnect(true, token); } catch (Exception) { /* the message is sent: a failed goodbye does not matter */ }
            }
        }

        /// <summary>The messages of the filters, the newest first, as rows of <see cref="EmailColumns"/>. Attachments are saved when <paramref name="saveTo"/> is given.</summary>
        public static TableValue Read(EmailProfile profile, string folderName, SearchQuery query, int max, bool markRead, string saveTo, int timeoutMs, CancellationToken token)
        {
            using (var client = ConnectImap(profile, timeoutMs, token))
            {
                try
                {
                    var folder = OpenFolder(client, folderName, markRead, token);
                    var uids = folder.Search(query, token);
                    var chosen = uids.Reverse().Take(Math.Max(1, max)).ToList(); // the newest first
                    var rows = new List<object[]>();
                    var flags = chosen.Count == 0 ? new List<IMessageSummary>() : folder.Fetch(chosen, MessageSummaryItems.UniqueId | MessageSummaryItems.Flags, token).ToList();
                    foreach (var uid in chosen)
                    {
                        token.ThrowIfCancellationRequested();
                        var message = folder.GetMessage(uid, token);
                        var summary = flags.FirstOrDefault(f => f.UniqueId.Equals(uid));
                        bool unread = summary == null || !summary.Flags.HasValue || !summary.Flags.Value.HasFlag(MessageFlags.Seen);
                        var names = Attachments(message, saveTo);
                        rows.Add(ToRow(message, unread, names));
                    }
                    if (markRead && chosen.Count > 0) folder.AddFlags(chosen, MessageFlags.Seen, true, token);
                    try { client.Disconnect(true, token); } catch (Exception) { }
                    var cells = new object[rows.Count, EmailColumns.Count];
                    for (int r = 0; r < rows.Count; r++) for (int c = 0; c < EmailColumns.Count; c++) cells[r, c] = rows[r][c] is bool || rows[r][c] is DateTime ? rows[r][c] : (object)Convert.ToString(rows[r][c]);
                    return new TableValue(cells);
                }
                catch (OperationCanceledException) { throw; }
                catch (InvalidOperationException) { throw; }
                catch (Exception ex) { throw Failure("read", profile, ex); }
            }
        }

        /// <summary>The file names of the attachments; with <paramref name="saveTo"/> they are saved there and the names are the paths.</summary>
        private static List<string> Attachments(MimeMessage message, string saveTo)
        {
            var names = new List<string>();
            foreach (var entity in message.Attachments)
            {
                var part = entity as MimePart;
                string name = part?.FileName ?? (entity as MessagePart)?.ContentDisposition?.FileName ?? "attachment";
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                if (string.IsNullOrWhiteSpace(saveTo) || part == null) { names.Add(name); continue; }
                Directory.CreateDirectory(saveTo);
                string path = Path.Combine(saveTo, name);
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(saveTo, Path.GetFileNameWithoutExtension(name) + " (" + n + ")" + Path.GetExtension(name));
                using (var stream = File.Create(path)) part.Content.DecodeTo(stream);
                names.Add(path);
            }
            return names;
        }

        private static IMailFolder OpenFolder(ImapClient client, string name, bool write, CancellationToken token)
        {
            IMailFolder folder = string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), "INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : client.GetFolder(name.Trim(), token);
            folder.Open(write ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, token);
            return folder;
        }

        /// <summary>Marks the messages as read or unread; returns how many.</summary>
        public static int Mark(EmailProfile profile, string folderName, SearchQuery query, int max, bool asRead, int timeoutMs, CancellationToken token)
        {
            using (var client = ConnectImap(profile, timeoutMs, token))
            {
                try
                {
                    var folder = OpenFolder(client, folderName, true, token);
                    var chosen = folder.Search(query, token).Reverse().Take(Math.Max(1, max)).ToList();
                    if (chosen.Count > 0)
                    {
                        if (asRead) folder.AddFlags(chosen, MessageFlags.Seen, true, token);
                        else folder.RemoveFlags(chosen, MessageFlags.Seen, true, token);
                    }
                    try { client.Disconnect(true, token); } catch (Exception) { }
                    return chosen.Count;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { throw Failure("mark", profile, ex); }
            }
        }

        /// <summary>Deletes the messages (to the Trash folder when there is one, unless <paramref name="permanent"/>); returns how many.</summary>
        public static int Delete(EmailProfile profile, string folderName, SearchQuery query, int max, bool permanent, int timeoutMs, CancellationToken token)
        {
            using (var client = ConnectImap(profile, timeoutMs, token))
            {
                try
                {
                    var folder = OpenFolder(client, folderName, true, token);
                    var chosen = folder.Search(query, token).Reverse().Take(Math.Max(1, max)).ToList();
                    if (chosen.Count > 0)
                    {
                        IMailFolder trash = null;
                        if (!permanent)
                        {
                            try { trash = client.GetFolder(SpecialFolder.Trash); } catch (Exception) { trash = null; }
                        }
                        if (!permanent && trash == null)
                            throw new InvalidOperationException("Email delete (" + profile.Name + "): the account has no Trash folder, so nothing was deleted. Turn on For good to delete permanently.");
                        if (trash != null && trash.FullName != folder.FullName) folder.MoveTo(chosen, trash, token);
                        else
                        {
                            folder.AddFlags(chosen, MessageFlags.Deleted, true, token);
                            folder.Expunge(chosen, token);
                        }
                    }
                    try { client.Disconnect(true, token); } catch (Exception) { }
                    return chosen.Count;
                }
                catch (OperationCanceledException) { throw; }
                catch (InvalidOperationException) { throw; }
                catch (Exception ex) { throw Failure("delete", profile, ex); }
            }
        }

        /// <summary>Tries the profile: logs in to the SMTP server (without sending) and to the IMAP server (and opens the inbox). One line per server.</summary>
        public static string Test(EmailProfile profile, int timeoutMs, CancellationToken token)
        {
            var lines = new List<string>();
            if (!profile.CanSend && !profile.CanRead) return "Nothing to test: enter an SMTP server, an IMAP server, or both.";
            if (profile.CanSend)
            {
                try
                {
                    using (var smtp = ConnectSmtp(profile, timeoutMs, token))
                    {
                        lines.Add("SMTP (send): connected" + (string.IsNullOrEmpty(profile.User) ? "" : " and logged in") + ".");
                        try { smtp.Disconnect(true, token); } catch (Exception) { }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (InvalidOperationException ex) { lines.Add("SMTP (send): FAILED. " + ex.Message); }
            }
            else lines.Add("SMTP (send): not set.");
            if (profile.CanRead)
            {
                try
                {
                    using (var imap = ConnectImap(profile, timeoutMs, token))
                    {
                        imap.Inbox.Open(FolderAccess.ReadOnly, token);
                        lines.Add("IMAP (read): connected" + (string.IsNullOrEmpty(profile.User) ? "" : " and logged in") + "; the inbox has " + imap.Inbox.Count + " message(s), " + imap.Inbox.Unread + " unread.");
                        try { imap.Disconnect(true, token); } catch (Exception) { }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (InvalidOperationException ex) { lines.Add("IMAP (read): FAILED. " + ex.Message); }
                catch (Exception ex) { lines.Add("IMAP (read): FAILED. " + Failure("read", profile, ex).Message); }
            }
            else lines.Add("IMAP (read): not set.");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
