using System;
using System.Security.Cryptography;
using System.Text;

namespace AutomationTest.Core.Licensing
{
    /// <summary>
    /// The "last time the program was used" mark kept in Setting.ini for licenses that expire. It is encrypted (AES-CBC with a
    /// random IV) and signed (HMAC-SHA256), with keys that depend on this computer and on the license, so it cannot be read,
    /// edited or copied from another computer or license.
    /// </summary>
    public static class LicenseState
    {
        /// <summary>How far behind the stored time the clock may be before it counts as set back (time sync, daylight changes).</summary>
        public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(2);

        public static string Protect(DateTime utc, string machineId, string licenseText)
        {
            byte[] plain = BitConverter.GetBytes(utc.Ticks);
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Key("state-enc", machineId, licenseText);
                aes.GenerateIV();
                byte[] cipher;
                using (var encryptor = aes.CreateEncryptor())
                    cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                var body = new byte[16 + cipher.Length];
                Buffer.BlockCopy(aes.IV, 0, body, 0, 16);
                Buffer.BlockCopy(cipher, 0, body, 16, cipher.Length);
                byte[] mac = Mac(body, machineId, licenseText);
                var all = new byte[body.Length + 32];
                Buffer.BlockCopy(body, 0, all, 0, body.Length);
                Buffer.BlockCopy(mac, 0, all, body.Length, 32);
                return Convert.ToBase64String(all);
            }
        }

        /// <summary>False when the text is missing, damaged, edited, or made for another computer or license.</summary>
        public static bool TryUnprotect(string text, string machineId, string licenseText, out DateTime utc)
        {
            utc = default(DateTime);
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return false;
                byte[] all = Convert.FromBase64String(text.Trim());
                if (all.Length < 16 + 16 + 32) return false;
                var body = new byte[all.Length - 32];
                Buffer.BlockCopy(all, 0, body, 0, body.Length);
                byte[] expected = Mac(body, machineId, licenseText);
                int diff = 0;
                for (int i = 0; i < 32; i++) diff |= expected[i] ^ all[body.Length + i];
                if (diff != 0) return false;

                var iv = new byte[16];
                Buffer.BlockCopy(body, 0, iv, 0, 16);
                using (var aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = Key("state-enc", machineId, licenseText);
                    aes.IV = iv;
                    using (var decryptor = aes.CreateDecryptor())
                    {
                        byte[] plain = decryptor.TransformFinalBlock(body, 16, body.Length - 16);
                        if (plain.Length != 8) return false;
                        long ticks = BitConverter.ToInt64(plain, 0);
                        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
                        utc = new DateTime(ticks, DateTimeKind.Utc);
                        return true;
                    }
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is CryptographicException || ex is ArgumentException)
            {
                return false;
            }
        }

        private static byte[] Key(string purpose, string machineId, string licenseText)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(Secrets.Value + "|" + purpose + "|" + MachineIdFormat.Normalize(machineId) + "|" + Fingerprint(licenseText)));
        }

        private static byte[] Mac(byte[] body, string machineId, string licenseText)
        {
            using (var hmac = new HMACSHA256(Key("state-mac", machineId, licenseText))) return hmac.ComputeHash(body);
        }

        /// <summary>Ties the state to one license: the signature bytes (all white space ignored, so a re-saved copy matches).</summary>
        private static string Fingerprint(string licenseText)
        {
            try
            {
                LicenseCodec.Unpack(licenseText, out byte[] _, out byte[] signature);
                return Convert.ToBase64String(signature);
            }
            catch (FormatException) { return ""; }
        }
    }
}
