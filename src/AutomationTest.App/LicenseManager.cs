using System;
using System.IO;
using System.Windows.Forms;
using AutomationTest.Core.Licensing;
using Microsoft.Win32;

namespace AutomationTest.App
{
    /// <summary>
    /// Everything about the license that needs this computer: its Machine ID, the request key shown to the user, license.lic and
    /// Setting.ini next to the exe. Nothing is written to the registry; the only registry access is reading MachineGuid (no
    /// administrator rights needed).
    /// A license that expires also needs the internet to be activated, and keeps an encrypted "last used" time in Setting.ini
    /// so that setting the computer's clock back is noticed while offline.
    /// </summary>
    internal static class LicenseManager
    {
        /// <summary>
        /// Public half of the signing key (X || Y, base64). The private half lives only with the product owner (KeyGen).
        /// Replace this with the output of "keygen genkey" when the key is renewed.
        /// </summary>
        public const string PublicKey = "+xmuNYTvwWrSoolbdN8IpaonySJrNOXCEU0UEFDDjzEs/yznKvpaW8BcwSfa7oFBGQEIZhjklMaHvqVkGlUgcg==";

        private const string AppSection = "App";
        private const string StateKey = "State";
        private static string _machineId;

        /// <summary>Test hook: replaces the clock (network time when possible).</summary>
        public static Func<TimeInfo> Clock { get; set; } = () => TrustedClock.Now();

        /// <summary>Test hook: where license.lic and Setting.ini live (null = next to the exe).</summary>
        public static string Folder { get; set; }

        private static string BaseFolder
        {
            get { return Folder ?? Path.GetDirectoryName(Application.ExecutablePath); }
        }

        public static string LicensePath { get { return Path.Combine(BaseFolder, "license.lic"); } }

        public static string SettingsPath { get { return Path.Combine(BaseFolder, IniFile.FileName); } }

        /// <exception cref="InvalidOperationException">The computer has no MachineGuid.</exception>
        public static string MachineId
        {
            get
            {
                if (_machineId != null) return _machineId;
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    string guid = key?.GetValue("MachineGuid") as string;
                    if (string.IsNullOrWhiteSpace(guid)) throw new InvalidOperationException("This computer has no MachineGuid, so a license cannot be tied to it.");
                    return _machineId = MachineIdFormat.FromMachineGuid(guid);
                }
            }
            set { _machineId = value; }
        }

        /// <summary>The Machine ID encrypted together with today's UTC date: what the user sends to the administrator.</summary>
        public static string RequestKeyNow()
        {
            return RequestKey.Encode(MachineId, DateTime.UtcNow);
        }

        /// <summary>
        /// Checks license.lic. For a license that expires it also checks Setting.ini and records the current time there, so call it
        /// at start-up and after each user action that should count as "the program was used".
        /// </summary>
        public static LicenseResult Check()
        {
            string text = null;
            try { if (File.Exists(LicensePath)) text = File.ReadAllText(LicensePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            var result = Evaluate(text);
            if (!result.IsValid || !result.License.ExpiresDate.HasValue) return result; // no expiry: nothing more to check

            var ini = IniFile.Load(SettingsPath);
            if (!LicenseState.TryUnprotect(ini.Get(AppSection, StateKey), MachineId, text, out DateTime stored))
                return With(result, LicenseStatus.StateInvalid, IniFile.FileName + " is missing or damaged, so this license can no longer be trusted. Activate the license again.");

            TimeInfo now = Clock();
            // The expiry date counts against the later of "now" and the last time the program was used (offline, the clock may be set back).
            DateTime latest = now.FromNetwork || now.UtcNow >= stored ? now.UtcNow : stored;
            DateTime latestLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(latest, DateTimeKind.Utc), TimeZoneInfo.Local);
            if (latestLocal.Date > result.License.ExpiresDate.Value.Date)
                return With(result, LicenseStatus.Expired, "The license expired on " + result.License.Expires + ".");
            // The internet is the truth: a clock that was merely wrong earlier is put right. Offline, going back in time is not allowed.
            if (!now.FromNetwork && now.UtcNow < stored - LicenseState.Tolerance)
                return With(result, LicenseStatus.ClockMovedBack, "The computer's clock is earlier than the last time this program was used. Correct the date and time.");

            if (now.FromNetwork || now.UtcNow > stored)
            {
                ini.Set(AppSection, StateKey, LicenseState.Protect(now.UtcNow, MachineId, text));
                TrySave(ini);
            }
            return result;
        }

        /// <summary>Checks license text without touching any file.</summary>
        public static LicenseResult Evaluate(string text)
        {
            return LicenseChecker.Check(text, LicenseChecker.ParsePublicKey(PublicKey), MachineId, Clock);
        }

        /// <summary>Reads the chosen file and imports it.</summary>
        public static LicenseResult ImportFile(string sourcePath)
        {
            string text;
            try { text = File.ReadAllText(sourcePath); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new LicenseResult { Status = LicenseStatus.Malformed, Message = "Cannot read the file: " + ex.Message };
            }
            return ImportText(text);
        }

        /// <summary>
        /// Activates a license (from a file or pasted): it must be valid for this computer; one that expires also needs the internet.
        /// It is saved next to the exe as license.lic (replacing any older one) and, if it expires, Setting.ini gets a fresh state.
        /// </summary>
        public static LicenseResult ImportText(string text)
        {
            TrustedClock.AllowRetry(); // the user may have just connected
            TimeInfo? asked = null;
            var result = LicenseChecker.Check(text, LicenseChecker.ParsePublicKey(PublicKey), MachineId, () => { var t = Clock(); asked = t; return t; });
            if (!result.IsValid) return result;

            bool expires = result.License.ExpiresDate.HasValue;
            if (expires && !(asked.HasValue && asked.Value.FromNetwork))
                return With(result, LicenseStatus.NeedsInternet, "A license with an expiry date can only be activated while connected to the internet (the real time is needed). Connect and try again.");

            string canonical;
            try
            {
                LicenseCodec.Unpack(text, out byte[] payload, out byte[] signature);
                canonical = LicenseCodec.Pack(payload, signature); // the three-line form, even when pasted with wrapped lines
                File.WriteAllText(LicensePath, canonical);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return With(result, LicenseStatus.Malformed, "The license is valid but cannot be saved next to the program (" + ex.Message + "). Run the program as administrator once, or move it to a folder you can write to.");
            }

            var ini = IniFile.Load(SettingsPath);
            if (expires) ini.Set(AppSection, StateKey, LicenseState.Protect(asked.Value.UtcNow, MachineId, canonical));
            else ini.Remove(AppSection, StateKey);
            try { ini.Save(SettingsPath); }
            catch (IOException ex)
            {
                try { File.Delete(LicensePath); } catch (IOException) { }
                return With(result, LicenseStatus.Malformed, "Cannot write " + IniFile.FileName + " next to the program (" + ex.Message + ").");
            }
            return result;
        }

        private static void TrySave(IniFile ini)
        {
            try { ini.Save(SettingsPath); }
            catch (IOException) { } // the old state stays, which is the safe side
        }

        private static LicenseResult With(LicenseResult from, LicenseStatus status, string message)
        {
            return new LicenseResult { Status = status, Message = message, License = from.License };
        }
    }
}
