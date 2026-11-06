using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Automation.Core.Licensing
{
    /// <summary>What a license says. This exact JSON is what gets signed.</summary>
    [DataContract]
    public sealed class LicenseData
    {
        public const string ProductName = "Automation";
        public const string DateFormat = "yyyy-MM-dd";

        [DataMember(Order = 0)] public string Product { get; set; } = ProductName;
        [DataMember(Order = 1)] public string MachineId { get; set; }
        [DataMember(Order = 2)] public string User { get; set; }
        /// <summary>Last day the license works (inclusive, yyyy-MM-dd). Empty = never expires.</summary>
        [DataMember(Order = 3)] public string Expires { get; set; } = "";
        [DataMember(Order = 4)] public string Features { get; set; } = "";
        [DataMember(Order = 5)] public string Issued { get; set; } = "";

        /// <summary>Null when the license does not expire.</summary>
        public DateTime? ExpiresDate
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Expires)) return null;
                return DateTime.ParseExact(Expires.Trim(), DateFormat, CultureInfo.InvariantCulture);
            }
        }

        public byte[] ToBytes()
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(LicenseData)).WriteObject(stream, this);
                return stream.ToArray();
            }
        }

        public static LicenseData FromBytes(byte[] json)
        {
            using (var stream = new MemoryStream(json))
                return (LicenseData)new DataContractJsonSerializer(typeof(LicenseData)).ReadObject(stream);
        }
    }

    /// <summary>The license file: a header line, the payload and the signature, each base64 on its own line.</summary>
    public static class LicenseCodec
    {
        public const string Header = "AUTOMATION-LICENSE-1";

        public static string Pack(byte[] payload, byte[] signature)
        {
            if (signature.Length != 64) throw new ArgumentException("An ECDSA P-256 signature must be 64 bytes.", nameof(signature));
            return Header + "\r\n" + Convert.ToBase64String(payload) + "\r\n" + Convert.ToBase64String(signature) + "\r\n";
        }

        /// <exception cref="FormatException">The text is not a license file.</exception>
        public static void Unpack(string text, out byte[] payload, out byte[] signature)
        {
            var lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3 || lines[0].Trim() != Header) throw new FormatException("This is not an Automation license file.");
            // Pasted text may have been wrapped by a chat app or an e-mail: join everything after the header and ignore
            // all white space. The signature (64 bytes) is always the last 88 base64 characters.
            var body = new StringBuilder();
            for (int i = 1; i < lines.Length; i++)
                foreach (char c in lines[i]) if (!char.IsWhiteSpace(c)) body.Append(c);
            if (body.Length <= SignatureChars) throw new FormatException("The license file is incomplete.");
            string all = body.ToString();
            payload = Convert.FromBase64String(all.Substring(0, all.Length - SignatureChars));
            signature = Convert.FromBase64String(all.Substring(all.Length - SignatureChars));
        }

        /// <summary>Base64 length of an ECDSA P-256 signature (64 bytes).</summary>
        public const int SignatureChars = 88;
    }

    /// <summary>The Machine ID shown to the user: a short hash of the computer's MachineGuid.</summary>
    public static class MachineIdFormat
    {
        public static string FromMachineGuid(string machineGuid)
        {
            if (string.IsNullOrWhiteSpace(machineGuid)) throw new ArgumentException("The machine GUID is empty.", nameof(machineGuid));
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes("Automation/" + machineGuid.Trim().ToLowerInvariant()));
                var hex = new StringBuilder();
                for (int i = 0; i < 10; i++) hex.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture)); // 20 hex digits
                return Normalize(hex.ToString());
            }
        }

        /// <summary>Upper-case with dashes (XXXXX-XXXXX-XXXXX-XXXXX); accepts the ID typed with or without dashes.</summary>
        public static string Normalize(string id)
        {
            var hex = new StringBuilder();
            foreach (char c in id ?? "") if (char.IsLetterOrDigit(c)) hex.Append(char.ToUpperInvariant(c));
            string s = hex.ToString();
            return s.Length == 20 ? s.Substring(0, 5) + "-" + s.Substring(5, 5) + "-" + s.Substring(10, 5) + "-" + s.Substring(15, 5) : s;
        }
    }
}
