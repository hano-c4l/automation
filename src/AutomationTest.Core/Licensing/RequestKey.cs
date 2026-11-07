using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AutomationTest.Core.Licensing
{
    /// <summary>
    /// The key a user sends to the administrator: the Machine ID encrypted with AES (128-bit) under a key derived from a
    /// built-in secret and the UTC date (yyyyMMdd). The block also carries a check code, so a wrong date or a typing mistake
    /// is detected. It changes every day; the Machine ID inside never does. Format: 32 hex digits in groups of four.
    /// </summary>
    public static class RequestKey
    {
        public const string DateFormat = "yyyyMMdd";

        public static string DateText(DateTime utcDate)
        {
            return utcDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        public static string Encode(string machineId, DateTime utcDate)
        {
            byte[] machine = MachineBytes(machineId);
            string date = DateText(utcDate);
            var block = new byte[16];
            Buffer.BlockCopy(machine, 0, block, 0, 10);
            Buffer.BlockCopy(Check(machine, date), 0, block, 10, 6);
            using (var aes = NewAes(date))
            using (var encryptor = aes.CreateEncryptor())
                return Group(encryptor.TransformFinalBlock(block, 0, 16));
        }

        /// <summary>Recovers the Machine ID, or false when the key is malformed or was not made for this date.</summary>
        public static bool TryDecode(string key, DateTime utcDate, out string machineId)
        {
            machineId = null;
            var hex = new StringBuilder();
            foreach (char c in key ?? "") if (!char.IsWhiteSpace(c) && c != '-') hex.Append(c);
            if (hex.Length != 32) return false;
            var cipher = new byte[16];
            for (int i = 0; i < 16; i++)
                if (!byte.TryParse(hex.ToString(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cipher[i])) return false;

            string date = DateText(utcDate);
            byte[] block;
            try
            {
                using (var aes = NewAes(date))
                using (var decryptor = aes.CreateDecryptor())
                    block = decryptor.TransformFinalBlock(cipher, 0, 16);
            }
            catch (CryptographicException) { return false; }

            var machine = new byte[10];
            Buffer.BlockCopy(block, 0, machine, 0, 10);
            byte[] expected = Check(machine, date);
            int diff = 0;
            for (int i = 0; i < 6; i++) diff |= expected[i] ^ block[10 + i];
            if (diff != 0) return false;
            machineId = MachineIdFormat.Normalize(ToHex(machine));
            return true;
        }

        private static Aes NewAes(string date)
        {
            var aes = Aes.Create();
            aes.Mode = CipherMode.ECB; // a single block per key, and the key changes daily
            aes.Padding = PaddingMode.None;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Secrets.Value + "|request-key|" + date));
                var key = new byte[16];
                Buffer.BlockCopy(hash, 0, key, 0, 16);
                aes.Key = key;
            }
            return aes;
        }

        private static byte[] MachineBytes(string machineId)
        {
            string hex = MachineIdFormat.Normalize(machineId).Replace("-", "");
            if (hex.Length != 20) throw new ArgumentException("The Machine ID is not valid.", nameof(machineId));
            var bytes = new byte[10];
            for (int i = 0; i < 10; i++) bytes[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        private static byte[] Check(byte[] machine, string date)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(Secrets.Value + "|request-check|" + date + "|" + ToHex(machine)));
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder();
            foreach (byte b in bytes) sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string Group(byte[] bytes)
        {
            string hex = ToHex(bytes);
            var sb = new StringBuilder();
            for (int i = 0; i < hex.Length; i += 4)
            {
                if (i > 0) sb.Append('-');
                sb.Append(hex, i, 4);
            }
            return sb.ToString();
        }
    }
}
