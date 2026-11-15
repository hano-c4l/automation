using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using AutomationTest.App;
using AutomationTest.Core.Email;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>
    /// The Email actions: the pure parts (addresses, message, search, dates, text of HTML), the model, the profile store in
    /// Setting.ini, and sending to a small SMTP server of the test's own (on this computer only). Reading needs a real IMAP
    /// server and is not covered here.
    /// </summary>
    internal static class EmailSuite
    {
        /// <summary>An SMTP server on 127.0.0.1 that accepts one message without login and keeps what it received.</summary>
        private sealed class FakeSmtp : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _thread;
            public readonly int Port;
            public readonly List<string> Messages = new List<string>();
            public readonly List<string> Recipients = new List<string>();

            public FakeSmtp()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _thread = new Thread(Serve) { IsBackground = true };
                _thread.Start();
            }

            private void Serve()
            {
                try
                {
                    while (true)
                    {
                        using (var client = _listener.AcceptTcpClient())
                        using (var stream = client.GetStream())
                        using (var reader = new StreamReader(stream, Encoding.ASCII))
                        using (var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
                        {
                            writer.WriteLine("220 test ready");
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                string upper = line.ToUpperInvariant();
                                if (upper.StartsWith("EHLO") || upper.StartsWith("HELO")) writer.WriteLine("250 test");
                                else if (upper.StartsWith("MAIL FROM")) writer.WriteLine("250 ok");
                                else if (upper.StartsWith("RCPT TO")) { lock (Recipients) Recipients.Add(line); writer.WriteLine("250 ok"); }
                                else if (upper == "DATA")
                                {
                                    writer.WriteLine("354 go on");
                                    var text = new StringBuilder();
                                    while ((line = reader.ReadLine()) != null && line != ".") text.AppendLine(line);
                                    lock (Messages) Messages.Add(text.ToString());
                                    writer.WriteLine("250 queued");
                                }
                                else if (upper == "QUIT") { writer.WriteLine("221 bye"); break; }
                                else writer.WriteLine("250 ok");
                            }
                        }
                    }
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            }

            public void Dispose() { _listener.Stop(); }
        }

        private sealed class Profiles : IEmailProfiles
        {
            private readonly List<EmailProfile> _list = new List<EmailProfile>();
            public Profiles(params EmailProfile[] profiles) { _list.AddRange(profiles); }
            public EmailProfile Find(string name) { return _list.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); }
            public IList<string> Names { get { return _list.Select(p => p.Name).ToList(); } }
        }

        private static EmailProfile Local(int port)
        {
            return new EmailProfile { Name = "local", SmtpHost = "127.0.0.1", SmtpPort = port, SmtpSecurity = EmailSecurity.None, FromAddress = "me@example.com", FromName = "Me" };
        }

        private static ScriptRunner Run(IEmailProfiles profiles, List<string> messages, params ActionNode[] actions)
        {
            var runner = new ScriptRunner(new MouseController()) { EmailProfiles = profiles };
            if (messages != null) runner.Message += messages.Add;
            runner.Run(new List<ActionNode>(actions), null, CancellationToken.None);
            return runner;
        }

        [Suite("email.pure")]
        public static void Pure(T t)
        {
            t.Eq("two addresses", EmailClient.ParseAddresses("a@x.com, Bob <b@y.org>; c@z.net", "To").Count, 3);
            t.Eq("a name and an address", EmailClient.ParseAddresses("Bob <b@y.org>", "To")[0].Address, "b@y.org");
            t.Throws("a bad address", () => EmailClient.ParseAddresses("not-an-address", "To"), "not an email address");
            t.Eq("empty gives none", EmailClient.ParseAddresses("  ", "To").Count, 0);

            t.Eq("a day", EmailClient.ParseDay("2026-11-30", "Since").Value.Day, 30);
            t.True("an empty day is none", EmailClient.ParseDay("", "Since") == null);
            t.Throws("a bad day", () => EmailClient.ParseDay("30/11/2026", "Since"), "yyyy-MM-dd");

            t.Eq("HTML to text", EmailClient.HtmlToText("<p>Hello <b>you</b></p><p>a &amp; b</p><style>x{}</style>"), "Hello you\na & b");

            var profile = Local(25);
            var message = EmailClient.BuildMessage(profile, "a@x.com", "c@x.com", "d@x.com", "Hi", "Body", false, new string[0]);
            t.Eq("subject", message.Subject, "Hi");
            t.Eq("from", message.From.Mailboxes.First().Address, "me@example.com");
            t.Eq("to, cc, bcc", message.To.Count + "," + message.Cc.Count + "," + message.Bcc.Count, "1,1,1");
            t.Eq("plain body", message.TextBody, "Body");
            t.Eq("html body", EmailClient.BuildMessage(profile, "a@x.com", "", "", "Hi", "<b>x</b>", true, new string[0]).HtmlBody, "<b>x</b>");
            t.Throws("no recipient", () => EmailClient.BuildMessage(profile, "", "", "", "Hi", "B", false, new string[0]), "at least one recipient");
            t.Throws("a missing attachment", () => EmailClient.BuildMessage(profile, "a@x.com", "", "", "Hi", "B", false, new[] { @"C:\no\such\file.txt" }), "does not exist");
            var noFrom = Local(25); noFrom.FromAddress = ""; noFrom.User = "";
            t.Throws("no From address", () => EmailClient.BuildMessage(noFrom, "a@x.com", "", "", "Hi", "B", false, new string[0]), "no From address");
            noFrom.User = "me@host.com";
            t.Eq("the user name is the From address when it is one", EmailClient.BuildMessage(noFrom, "a@x.com", "", "", "Hi", "B", false, new string[0]).From.Mailboxes.First().Address, "me@host.com");

            t.True("a search is built", EmailClient.BuildQuery("x", "y", new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), EmailStatus.Unread) != null);
            var row = EmailClient.ToRow(message, true, new[] { "a.txt", "b.txt" });
            t.Eq("row: from", row[0], "me@example.com");
            t.Eq("row: unread", row[3], true);
            t.Eq("row: attachments", row[5], "a.txt|b.txt");
        }

        [Suite("email.model")]
        public static void Model(T t)
        {
            var settings = new EmailSettings { Kind = EmailKind.Read, Profile = "work", Subject = "Invoice", From = "boss", Since = "2026-01-01", Status = EmailStatus.Unread, Max = 5, MarkRead = true, SaveTo = @"C:\mail", Id = "mail", Permanent = true, MarkAs = EmailMarkAs.Unread };
            var script = new AutomationScript();
            script.Actions.Add(ActionNode.NewEmail(settings));
            string json = ScriptSerializer.ToJson(script);
            t.False("no password field in the script", json.IndexOf("assword", StringComparison.OrdinalIgnoreCase) >= 0);
            var back = ScriptSerializer.FromJson(json).Actions[0];
            t.Eq("category", back.Category, ActionCategory.Email);
            var e = back.Email;
            t.Eq("fields kept", e.Kind + "|" + e.Profile + "|" + e.Subject + "|" + e.From + "|" + e.Since + "|" + e.Status + "|" + e.Max + "|" + e.MarkRead + "|" + e.SaveTo + "|" + e.Id + "|" + e.Permanent + "|" + e.MarkAs,
                "Read|work|Invoice|boss|2026-01-01|Unread|5|True|C:\\mail|mail|True|Unread");
            t.Eq("a clone is equal", ScriptSerializer.ToJson(new AutomationScript { Actions = { back.Clone() } }), json);
            t.Eq("the delay counts as time", ActionTiming.PerIterationMs(Wrap(ActionNode.NewEmail(new EmailSettings { DelayMs = 700 }))), 700L);

            // what Start Selected needs and gives
            var send = ActionNode.NewEmail(new EmailSettings { Kind = EmailKind.Send, To = "{who}", ToIsExpression = true, Body = "\"n=\" + {count}", BodyIsExpression = true });
            var read = ActionNode.NewEmail(new EmailSettings { Kind = EmailKind.Read, Id = "mail" });
            var use = ActionNode.NewVariable(new VariableSettings { Id = "z", Type = VariableType.String, UseExpression = true, Expression = "{mail.Rows}" });
            var plan = new AutomationScript();
            plan.Actions.Add(send); plan.Actions.Add(read); plan.Actions.Add(use);
            var needs = SelectionPlanner.Build(plan, new[] { send, read, use }).Needs.Select(n => n.Name).OrderBy(n => n);
            t.Eq("Send reads variables, Read defines its ID", string.Join(",", needs), "count,who");
        }

        private static ActionNode Wrap(ActionNode inner)
        {
            var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 1 });
            loop.Children.Add(inner);
            return loop;
        }

        [Suite("email.send")]
        public static void Send(T t)
        {
            using (var server = new FakeSmtp())
            {
                var profiles = new Profiles(Local(server.Port));
                var messages = new List<string>();
                Run(profiles, messages, ActionNode.NewEmail(new EmailSettings { Kind = EmailKind.Send, Profile = "LOCAL", To = "a@x.com; b@x.com", Cc = "c@x.com", Subject = "Hello there", Body = "Line one" }));
                t.Eq("the server got one message", server.Messages.Count, 1);
                t.Eq("three recipients", server.Recipients.Count, 3);
                t.True("the subject is in it", server.Messages.Count > 0 && server.Messages[0].Contains("Subject: Hello there"));
                t.True("the body is in it", server.Messages.Count > 0 && server.Messages[0].Contains("Line one"));
                t.True("a message says it was sent", messages.Any(m => m.StartsWith("Email sent")));

                // expressions
                Run(profiles, null, ActionNode.NewVariable(new VariableSettings { Id = "who", Type = VariableType.String, Text = "z@x.com" }),
                    ActionNode.NewEmail(new EmailSettings { Kind = EmailKind.Send, Profile = "local", To = "{who}", ToIsExpression = true, Subject = "\"Re: \" + {who}", SubjectIsExpression = true, Body = "x" }));
                t.True("the expression subject was sent", server.Messages.Count == 2 && server.Messages[1].Contains("Re: z@x.com"));

                // an attachment
                string file = Path.Combine(Path.GetTempPath(), "at_mail_" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(file, "attached text");
                try
                {
                    Run(profiles, null, ActionNode.NewEmail(new EmailSettings { Kind = EmailKind.Send, Profile = "local", To = "a@x.com", Subject = "File", Body = "see", Attachments = file }));
                    t.True("an attachment is sent", server.Messages.Count == 3 && server.Messages[2].Contains("Content-Disposition: attachment"));
                }
                finally { File.Delete(file); }
            }
        }

        [Suite("email.errors")]
        public static void Errors(T t)
        {
            var profiles = new Profiles(Local(1));
            Action<EmailSettings, IEmailProfiles> run = (s, p) => Run(p, null, ActionNode.NewEmail(s));
            t.Throws("no profile chosen", () => run(new EmailSettings { To = "a@x.com" }, profiles), "choose the profile");
            t.Throws("a profile that is not on this computer", () => run(new EmailSettings { Profile = "gone", To = "a@x.com" }, profiles), "no profile named \"gone\"");
            t.Throws("no profiles at all", () => run(new EmailSettings { Profile = "local", To = "a@x.com" }, null), "no profile named");
            t.Throws("a server that is not there", () => run(new EmailSettings { Profile = "local", To = "a@x.com", Subject = "s", TimeMs = 2000 }, profiles), "Email send (local)");
            t.Throws("Delete without a filter", () => run(new EmailSettings { Kind = EmailKind.Delete, Profile = "local" }, profiles), "at least one filter");
            t.Throws("a bad day", () => run(new EmailSettings { Kind = EmailKind.Read, Profile = "local", Since = "yesterday" }, profiles), "yyyy-MM-dd");
            t.Throws("a bad ID", () => run(new EmailSettings { Kind = EmailKind.Read, Profile = "local", Id = "1 x" }, profiles), "not a valid ID");

            // a password is never sent without encryption
            var plain = Local(25); plain.User = "me"; plain.Password = "SECRET-PW";
            string failure = null;
            try { Run(new Profiles(plain), null, ActionNode.NewEmail(new EmailSettings { Profile = "local", To = "a@x.com" })); }
            catch (InvalidOperationException ex) { failure = ex.Message; }
            t.True("a password over an unencrypted connection is refused", failure != null && failure.Contains("without encryption"));
            t.False("the password is not in the message", failure != null && failure.Contains("SECRET-PW"));
            t.True("Test names a problem per server", EmailClient.Test(new EmailProfile { Name = "x", SmtpHost = "127.0.0.1", SmtpPort = 1, SmtpSecurity = EmailSecurity.None }, 2000, CancellationToken.None).Contains("FAILED"));
            t.True("Test with nothing set says so", EmailClient.Test(new EmailProfile { Name = "x" }, 2000, CancellationToken.None).StartsWith("Nothing to test"));
        }

        [Suite("email.store")]
        public static void Store(T t)
        {
            string path = Path.Combine(Path.GetTempPath(), "at_mailini_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                File.WriteAllText(path, "[Files]\r\nScriptFolder=C:\\scripts\r\n");
                var store = new EmailProfileStore(path);
                t.Eq("starts empty", store.Profiles.Count, 0);
                store.Save(new EmailProfile { Name = "Work", SmtpHost = "smtp.x.com", SmtpPort = 465, SmtpSecurity = EmailSecurity.SslTls, ImapHost = "imap.x.com", User = "me@x.com", Password = "S3cret!pw", FromName = "Me" }, null);
                store.Save(new EmailProfile { Name = "Home", SmtpHost = "smtp.h.com" }, null);
                t.Throws("the same name twice", () => store.Save(new EmailProfile { Name = "work", SmtpHost = "q" }, null), "already a profile");

                string text = File.ReadAllText(path);
                t.True("the file keeps its other entries", text.Contains("ScriptFolder=C:\\scripts"));
                t.False("the password is not in the file as text", text.Contains("S3cret!pw"));
                t.True("the password is stored encrypted", text.Contains("Password=dpapi:"));

                var again = new EmailProfileStore(path);
                t.Eq("two profiles are back", string.Join(",", again.Names), "Work,Home");
                var work = again.Find("WORK");
                t.Eq("the password can be read back", work.Password, "S3cret!pw");
                t.Eq("the fields are back", work.SmtpHost + "|" + work.SmtpPort + "|" + work.SmtpSecurity + "|" + work.ImapHost + "|" + work.ImapPort + "|" + work.ImapSecurity + "|" + work.User + "|" + work.FromName,
                    "smtp.x.com|465|SslTls|imap.x.com|993|SslTls|me@x.com|Me");
                t.True("a profile that does not exist is null", again.Find("nobody") == null);

                again.Save(new EmailProfile { Name = "Office", SmtpHost = "smtp.o.com" }, "Work"); // rename
                t.Eq("renamed in place", string.Join(",", new EmailProfileStore(path).Names), "Office,Home");
                again.Delete("home");
                t.Eq("deleted", string.Join(",", new EmailProfileStore(path).Names), "Office");
                t.True("the other entries are still there", File.ReadAllText(path).Contains("ScriptFolder=C:\\scripts"));
            }
            finally { File.Delete(path); }
        }
    }
}
