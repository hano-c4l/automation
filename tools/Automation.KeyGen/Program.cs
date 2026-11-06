using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Automation.Core.Licensing;
using Microsoft.Win32;

namespace Automation.KeyGen
{
    /// <summary>
    /// Owner-only tool: creates the signing key pair and issues license files. NEVER ship this tool or the private key
    /// file with the program, and never commit the private key.
    /// </summary>
    internal static class Program
    {
        private const string Usage = @"Automation KeyGen

  keygen genkey --out <private.key>
      Creates a new signing key. Prints the public key to paste into LicenseManager.PublicKey in Automation.App.
      Keep the private key file safe and backed up, outside the repository.

  keygen machineid
      Prints the Machine ID of this computer.

  keygen issue --key <private.key> (--request <request key> --sent <YYYYMMDD> | --machine <id> | --this-machine)
               [--user <name>] [--expires <YYYYMMDD>] [--features <text>] --out <license.lic>
      Creates a license file (replacing an existing one). Without --expires the license never expires.
      --request is the key the user's program shows; --sent is the UTC date it was made.";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) { Console.WriteLine(Usage); return 1; }
                var options = ParseOptions(args);
                switch (args[0])
                {
                    case "genkey": return GenerateKey(Require(options, "out"));
                    case "machineid": Console.WriteLine(ThisMachineId()); return 0;
                    case "issue": return Issue(options);
                    default: Console.WriteLine(Usage); return 1;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is FormatException || ex is CryptographicException)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 2;
            }
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) throw new ArgumentException("Unexpected argument: " + args[i]);
                string name = args[i].Substring(2);
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
                map[name] = hasValue ? args[++i] : "";
            }
            return map;
        }

        private static string Require(Dictionary<string, string> options, string name)
        {
            if (!options.TryGetValue(name, out string value) || value.Length == 0) throw new ArgumentException("Missing --" + name + ".");
            return value;
        }

        private static int GenerateKey(string path)
        {
            if (File.Exists(path)) throw new ArgumentException("\"" + path + "\" already exists; it will not be overwritten.");
            using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var p = ecdsa.ExportParameters(true);
                var all = new byte[96]; // D || X || Y
                Buffer.BlockCopy(p.D, 0, all, 0, 32);
                Buffer.BlockCopy(p.Q.X, 0, all, 32, 32);
                Buffer.BlockCopy(p.Q.Y, 0, all, 64, 32);
                File.WriteAllText(path, Convert.ToBase64String(all));
                var pub = new byte[64];
                Buffer.BlockCopy(all, 32, pub, 0, 64);
                Console.WriteLine("Private key written to " + Path.GetFullPath(path) + " (keep it secret and back it up).");
                Console.WriteLine("Public key (put in LicenseKeys.PublicKey):");
                Console.WriteLine(Convert.ToBase64String(pub));
            }
            return 0;
        }

        private static int Issue(Dictionary<string, string> options)
        {
            string machine;
            if (options.ContainsKey("request"))
            {
                // The key only decodes with the exact date it was made on; the administrator decides which UTC date that is.
                string key = Require(options, "request");
                DateTime sent = ParseDate(Require(options, "sent"));
                if (!RequestKey.TryDecode(key, sent, out machine))
                    throw new ArgumentException("The request key does not match the send date " + RequestKey.DateText(sent) + " (wrong date, or the key is wrong or was altered). No license was created.");
            }
            else
            {
                machine = options.ContainsKey("this-machine") ? ThisMachineId() : MachineIdFormat.Normalize(Require(options, "machine"));
            }
            var data = new LicenseData
            {
                MachineId = machine,
                User = options.TryGetValue("user", out string user) && user.Length > 0 ? user : "Licensed user",
                Features = options.TryGetValue("features", out string features) ? features : "",
                Issued = DateTime.UtcNow.ToString(LicenseData.DateFormat, CultureInfo.InvariantCulture)
            };
            if (options.TryGetValue("expires", out string expires) && expires.Length > 0)
                data.Expires = ParseDate(expires).ToString(LicenseData.DateFormat, CultureInfo.InvariantCulture);

            var all = Convert.FromBase64String(File.ReadAllText(Require(options, "key")).Trim());
            if (all.Length != 96) throw new FormatException("The private key file is not valid.");
            var d = new byte[32]; var x = new byte[32]; var y = new byte[32];
            Buffer.BlockCopy(all, 0, d, 0, 32);
            Buffer.BlockCopy(all, 32, x, 0, 32);
            Buffer.BlockCopy(all, 64, y, 0, 32);
            var parameters = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = d, Q = new ECPoint { X = x, Y = y } };

            byte[] payload = data.ToBytes();
            byte[] signature;
            using (var ecdsa = ECDsa.Create(parameters)) signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);

            string output = Require(options, "out");
            File.WriteAllText(output, LicenseCodec.Pack(payload, signature));
            Console.WriteLine("License for " + data.User + " (machine " + data.MachineId + ", " + (data.Expires.Length == 0 ? "no expiry" : "valid through " + data.Expires) + ") written to " + Path.GetFullPath(output));
            return 0;
        }

        /// <summary>Accepts yyyyMMdd or yyyy-MM-dd.</summary>
        private static DateTime ParseDate(string text)
        {
            if (DateTime.TryParseExact(text.Trim(), new[] { "yyyyMMdd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)) return date;
            throw new ArgumentException("\"" + text + "\" is not a date; use YYYYMMDD.");
        }

        private static string ThisMachineId()
        {
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            {
                string guid = key?.GetValue("MachineGuid") as string;
                if (string.IsNullOrWhiteSpace(guid)) throw new ArgumentException("This computer has no MachineGuid in the registry.");
                return MachineIdFormat.FromMachineGuid(guid);
            }
        }
    }
}
