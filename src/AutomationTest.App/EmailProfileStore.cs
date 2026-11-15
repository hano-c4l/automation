using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using AutomationTest.Core.Email;

namespace AutomationTest.App
{
    /// <summary>
    /// The mail profiles of this computer, in Setting.ini next to the program. The password is encrypted with Windows
    /// data protection for the current Windows user (nobody else, and no other computer, can read it); a script file only holds
    /// the name of a profile.
    /// </summary>
    internal sealed class EmailProfileStore : IEmailProfiles
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutomationTest.EmailProfiles");

        private readonly string _path;
        private readonly List<EmailProfile> _profiles = new List<EmailProfile>();
        private readonly object _lock = new object(); // a run (another thread) may save a new refresh token while the window edits profiles

        public EmailProfileStore() : this(IniFile.DefaultPath) { }

        public EmailProfileStore(string path)
        {
            _path = path;
            Load();
        }

        public IList<EmailProfile> Profiles { get { return _profiles; } }

        public IList<string> Names { get { lock (_lock) return _profiles.Select(p => p.Name).ToList(); } }

        public EmailProfile Find(string name)
        {
            lock (_lock)
            {
                var found = _profiles.FirstOrDefault(p => string.Equals(p.Name, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                return found?.Clone();
            }
        }

        /// <summary>A provider sent a new refresh token: keep it (the old one may stop working).</summary>
        public void UpdateRefreshToken(string name, string refreshToken)
        {
            lock (_lock)
            {
                var found = _profiles.FirstOrDefault(p => string.Equals(p.Name, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                if (found == null) return;
                found.RefreshToken = refreshToken ?? "";
                try { Write(); } catch (InvalidOperationException) { } // the run goes on with the token in memory
            }
        }

        /// <summary>Adds the profile, or replaces the one named <paramref name="oldName"/> (null = add).</summary>
        public void Save(EmailProfile profile, string oldName)
        {
            lock (_lock) SaveCore(profile, oldName);
        }

        private void SaveCore(EmailProfile profile, string oldName)
        {
            if (string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidOperationException("Give the profile a name.");
            profile.Name = profile.Name.Trim();
            var same = _profiles.FirstOrDefault(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            var old = oldName == null ? null : _profiles.FirstOrDefault(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (same != null && !ReferenceEquals(same, old)) throw new InvalidOperationException("There is already a profile named \"" + profile.Name + "\".");
            if (old != null) _profiles[_profiles.IndexOf(old)] = profile;
            else _profiles.Add(profile);
            Write();
        }

        public void Delete(string name)
        {
            lock (_lock)
            {
                _profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                Write();
            }
        }

        // ------------------------------------------------------------------ Setting.ini
        // [Email] Count=N, then [Email.1] ... [Email.N] with one profile each. Password=dpapi:... is encrypted (two-way).

        private const string Prefix = "dpapi:";

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            return Prefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));
        }

        private static string Unprotect(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith(Prefix, StringComparison.Ordinal)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text.Substring(Prefix.Length)), Entropy, DataProtectionScope.CurrentUser)); }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException) { return ""; } // another Windows user or computer: enter it again
        }

        private static EmailSecurity ParseSecurity(string text, EmailSecurity fallback)
        {
            EmailSecurity parsed;
            return Enum.TryParse(text, true, out parsed) ? parsed : fallback;
        }

        private static EmailAuth ParseAuth(string text)
        {
            EmailAuth parsed;
            return Enum.TryParse(text, true, out parsed) ? parsed : EmailAuth.Password;
        }

        private void Load()
        {
            _profiles.Clear();
            var ini = IniFile.Load(_path);
            int count = ini.GetInt("Email", "Count") ?? 0;
            for (int n = 1; n <= count && n <= 1000; n++)
            {
                string section = "Email." + n;
                string name = (ini.Get(section, "Name") ?? "").Trim();
                if (name.Length == 0 || _profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                _profiles.Add(new EmailProfile
                {
                    Name = name,
                    SmtpHost = ini.Get(section, "SmtpHost") ?? "",
                    SmtpPort = ini.GetInt(section, "SmtpPort") ?? 587,
                    SmtpSecurity = ParseSecurity(ini.Get(section, "SmtpSecurity"), EmailSecurity.StartTls),
                    ImapHost = ini.Get(section, "ImapHost") ?? "",
                    ImapPort = ini.GetInt(section, "ImapPort") ?? 993,
                    ImapSecurity = ParseSecurity(ini.Get(section, "ImapSecurity"), EmailSecurity.SslTls),
                    User = ini.Get(section, "User") ?? "",
                    Password = Unprotect(ini.Get(section, "Password")),
                    FromAddress = ini.Get(section, "FromAddress") ?? "",
                    FromName = ini.Get(section, "FromName") ?? "",
                    Auth = ParseAuth(ini.Get(section, "Auth")),
                    ClientId = ini.Get(section, "ClientId") ?? "",
                    ClientSecret = Unprotect(ini.Get(section, "ClientSecret")),
                    Tenant = ini.Get(section, "Tenant") ?? "",
                    AuthUrl = ini.Get(section, "AuthUrl") ?? "",
                    TokenUrl = ini.Get(section, "TokenUrl") ?? "",
                    Scope = ini.Get(section, "Scope") ?? "",
                    RefreshToken = Unprotect(ini.Get(section, "RefreshToken"))
                });
            }
        }

        private void Write()
        {
            var ini = IniFile.Load(_path); // keeps the other entries of the file
            ini.RemoveSections("Email");
            ini.Set("Email", "Count", _profiles.Count);
            for (int n = 1; n <= _profiles.Count; n++)
            {
                var p = _profiles[n - 1];
                string section = "Email." + n;
                ini.Set(section, "Name", p.Name);
                ini.Set(section, "SmtpHost", p.SmtpHost);
                ini.Set(section, "SmtpPort", p.SmtpPort);
                ini.Set(section, "SmtpSecurity", p.SmtpSecurity.ToString());
                ini.Set(section, "ImapHost", p.ImapHost);
                ini.Set(section, "ImapPort", p.ImapPort);
                ini.Set(section, "ImapSecurity", p.ImapSecurity.ToString());
                ini.Set(section, "User", p.User);
                ini.Set(section, "Password", Protect(p.Password));
                ini.Set(section, "FromAddress", p.FromAddress);
                ini.Set(section, "FromName", p.FromName);
                ini.Set(section, "Auth", p.Auth.ToString());
                ini.Set(section, "ClientId", p.ClientId);
                ini.Set(section, "ClientSecret", Protect(p.ClientSecret));
                ini.Set(section, "Tenant", p.Tenant);
                ini.Set(section, "AuthUrl", p.AuthUrl);
                ini.Set(section, "TokenUrl", p.TokenUrl);
                ini.Set(section, "Scope", p.Scope);
                ini.Set(section, "RefreshToken", Protect(p.RefreshToken));
            }
            try { ini.Save(_path); }
            catch (IOException ex) { throw new InvalidOperationException("Cannot save the email profiles to \"" + _path + "\": " + ex.Message); }
        }
    }
}
