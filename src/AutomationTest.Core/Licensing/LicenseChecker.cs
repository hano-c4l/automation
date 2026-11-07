using System;
using System.Security.Cryptography;

namespace AutomationTest.Core.Licensing
{
    public enum LicenseStatus
    {
        Valid,
        Missing,
        Malformed,
        BadSignature,
        WrongMachine,
        Expired,
        ClockMovedBack,
        /// <summary>Setting.ini is missing or damaged for a license that expires: the license must be activated again.</summary>
        StateInvalid,
        /// <summary>Activating a license that expires needs the internet to read the real time.</summary>
        NeedsInternet
    }

    public sealed class LicenseResult
    {
        public LicenseStatus Status;
        public string Message;
        /// <summary>The license contents (null unless the signature checked out).</summary>
        public LicenseData License;
        public bool IsValid { get { return Status == LicenseStatus.Valid; } }
    }

    public static class LicenseChecker
    {
        /// <summary>Public key as base64 of the 64 bytes X || Y (ECDSA P-256).</summary>
        public static ECParameters ParsePublicKey(string base64)
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length != 64) throw new FormatException("The public key must be 64 bytes (X and Y of a P-256 point).");
            var x = new byte[32];
            var y = new byte[32];
            Buffer.BlockCopy(bytes, 0, x, 0, 32);
            Buffer.BlockCopy(bytes, 32, y, 0, 32);
            return new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } };
        }

        /// <param name="fileText">Content of license.lic (or pasted text), or null when there is none.</param>
        /// <param name="clock">Asked for the current time, and only when the license expires (a license without an expiry date never needs the network).</param>
        public static LicenseResult Check(string fileText, ECParameters publicKey, string machineId, Func<TimeInfo> clock)
        {
            if (string.IsNullOrWhiteSpace(fileText)) return Fail(LicenseStatus.Missing, "There is no license for this computer.");

            byte[] payload, signature;
            try { LicenseCodec.Unpack(fileText, out payload, out signature); }
            catch (FormatException) { return Fail(LicenseStatus.Malformed, "The license is damaged or is not an AutomationTest license."); }

            bool signedOk;
            try
            {
                using (var ecdsa = ECDsa.Create(publicKey))
                    signedOk = ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256);
            }
            catch (CryptographicException) { signedOk = false; }
            if (!signedOk) return Fail(LicenseStatus.BadSignature, "The license signature is not valid.");

            LicenseData data;
            DateTime? expires;
            try
            {
                data = LicenseData.FromBytes(payload);
                expires = data.ExpiresDate;
            }
            catch (Exception ex) when (ex is FormatException || ex is System.Runtime.Serialization.SerializationException)
            {
                return Fail(LicenseStatus.Malformed, "The license contents cannot be read.");
            }
            if (data.Product != LicenseData.ProductName) return Fail(LicenseStatus.Malformed, "This license is for a different product.");

            if (!string.Equals(MachineIdFormat.Normalize(data.MachineId), MachineIdFormat.Normalize(machineId), StringComparison.Ordinal))
                return new LicenseResult { Status = LicenseStatus.WrongMachine, Message = "This license was issued for a different computer.", License = data };

            if (expires.HasValue)
            {
                TimeInfo time = clock();
                DateTime nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(time.UtcNow, DateTimeKind.Utc), TimeZoneInfo.Local);
                if (nowLocal.Date > expires.Value.Date)
                    return new LicenseResult { Status = LicenseStatus.Expired, Message = "The license expired on " + data.Expires + ".", License = data };

                // Without the internet the computer's own clock is all there is; at least it cannot be earlier than the day the license was made.
                if (!time.FromNetwork && DateTime.TryParseExact(data.Issued, LicenseData.DateFormat, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime issued) && time.UtcNow.Date < issued.Date.AddDays(-1))
                    return new LicenseResult { Status = LicenseStatus.ClockMovedBack, Message = "The computer's clock is earlier than the day this license was issued. Correct the date and time.", License = data };
            }

            return new LicenseResult { Status = LicenseStatus.Valid, Message = "Licensed to " + data.User + (expires.HasValue ? ", valid through " + data.Expires : ", no expiry") + ".", License = data };
        }

        private static LicenseResult Fail(LicenseStatus status, string message)
        {
            return new LicenseResult { Status = status, Message = message };
        }
    }
}
