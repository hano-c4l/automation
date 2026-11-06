using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Automation.Core.Licensing
{
    /// <summary>The current time and whether it came from the network (true) or only from this computer's clock (false).</summary>
    public struct TimeInfo
    {
        public DateTime UtcNow;
        public bool FromNetwork;
    }

    /// <summary>
    /// Asks the internet for the time, like Windows does when it syncs its clock, so setting the computer's clock back does
    /// not help. Tries the Date header of HTTPS sites first (the connection is authenticated), then NTP. After one success the
    /// time is carried forward with a stopwatch, so changing the system clock afterwards has no effect during this run.
    /// Nothing is stored on disk or in the registry.
    /// </summary>
    public static class TrustedClock
    {
        private static readonly string[] HttpsUrls = { "https://www.microsoft.com", "https://www.google.com", "https://www.cloudflare.com" };
        private static readonly string[] NtpHosts = { "time.windows.com", "time.google.com", "pool.ntp.org" };
        private static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(1);
        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(10); // offline: do not make every action wait for the lookup
        private static readonly object Gate = new object();

        private static DateTime _networkUtc;
        private static long _atTicks;                 // Stopwatch timestamp when _networkUtc was read
        private static bool _hasNetwork;
        private static long _failedAtTicks = long.MinValue;

        /// <summary>Test hook: replaces the network lookups; returns null when there is no answer.</summary>
        public static Func<DateTime?> Fetcher { get; set; }

        /// <summary>Lets the next call try the network again even if a recent attempt failed (the user may have just connected).</summary>
        public static void AllowRetry()
        {
            lock (Gate) { _failedAtTicks = long.MinValue; }
        }

        /// <summary>Forgets the carried-forward time (for tests).</summary>
        public static void Reset()
        {
            lock (Gate) { _hasNetwork = false; _failedAtTicks = long.MinValue; }
        }

        public static TimeInfo Now(int timeoutMs = 3000)
        {
            lock (Gate)
            {
                long now = Stopwatch.GetTimestamp();
                if (_hasNetwork && Elapsed(_atTicks, now) < RefreshAfter)
                    return new TimeInfo { UtcNow = _networkUtc + Elapsed(_atTicks, now), FromNetwork = true };

                bool recentlyFailed = _failedAtTicks != long.MinValue && Elapsed(_failedAtTicks, now) < RetryAfterFailure;
                if (!recentlyFailed)
                {
                    DateTime? fetched = Fetcher != null ? Fetcher() : FetchFromInternet(timeoutMs);
                    if (fetched.HasValue)
                    {
                        _networkUtc = fetched.Value;
                        _atTicks = Stopwatch.GetTimestamp();
                        _hasNetwork = true;
                        return new TimeInfo { UtcNow = _networkUtc, FromNetwork = true };
                    }
                    _failedAtTicks = Stopwatch.GetTimestamp();
                }
                if (_hasNetwork) // refresh failed but an earlier answer is still the best information
                    return new TimeInfo { UtcNow = _networkUtc + Elapsed(_atTicks, Stopwatch.GetTimestamp()), FromNetwork = true };
                return new TimeInfo { UtcNow = DateTime.UtcNow, FromNetwork = false };
            }
        }

        private static TimeSpan Elapsed(long from, long to)
        {
            return TimeSpan.FromSeconds((to - from) / (double)Stopwatch.Frequency);
        }

        private static DateTime? FetchFromInternet(int timeoutMs)
        {
            var tasks = new System.Collections.Generic.List<Task<DateTime?>>();
            foreach (string url in HttpsUrls) { string u = url; tasks.Add(Task.Run(() => FromHttps(u, timeoutMs))); }
            foreach (string host in NtpHosts) { string h = host; tasks.Add(Task.Run(() => FromNtp(h, timeoutMs))); }
            // HTTPS answers are preferred; wait for them first, then take whatever NTP produced.
            var deadline = Stopwatch.StartNew();
            for (int i = 0; i < HttpsUrls.Length; i++)
            {
                var t = tasks[i];
                int left = timeoutMs + 500 - (int)deadline.ElapsedMilliseconds;
                if (left > 0 && t.Wait(left) && t.Result.HasValue) return t.Result;
            }
            for (int i = HttpsUrls.Length; i < tasks.Count; i++)
            {
                int left = Math.Max(1, timeoutMs + 500 - (int)deadline.ElapsedMilliseconds);
                if (tasks[i].Wait(left) && tasks[i].Result.HasValue) return tasks[i].Result;
            }
            return null;
        }

        private static DateTime? FromHttps(string url, int timeoutMs)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "HEAD";
                request.Timeout = timeoutMs;
                request.AllowAutoRedirect = false;
                request.UserAgent = "Mozilla/5.0";
                HttpWebResponse response;
                try { response = (HttpWebResponse)request.GetResponse(); }
                catch (WebException ex) when (ex.Response != null) { response = (HttpWebResponse)ex.Response; } // 4xx / 405 still carry the Date header
                using (response)
                {
                    string date = response.Headers["Date"];
                    if (date != null && DateTime.TryParseExact(date, "r", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime utc))
                        return utc;
                }
            }
            catch (Exception ex) when (ex is WebException || ex is InvalidOperationException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
            }
            return null;
        }

        private static DateTime? FromNtp(string host, int timeoutMs)
        {
            try
            {
                var packet = new byte[48];
                packet[0] = 0x1B; // version 3, client mode
                using (var udp = new UdpClient())
                {
                    udp.Client.SendTimeout = timeoutMs;
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Connect(host, 123);
                    udp.Send(packet, packet.Length);
                    IPEndPoint remote = null;
                    byte[] reply = udp.Receive(ref remote);
                    if (reply.Length < 48) return null;
                    ulong seconds = ((ulong)reply[40] << 24) | ((ulong)reply[41] << 16) | ((ulong)reply[42] << 8) | reply[43];
                    ulong fraction = ((ulong)reply[44] << 24) | ((ulong)reply[45] << 16) | ((ulong)reply[46] << 8) | reply[47];
                    if (seconds == 0) return null;
                    var epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    return epoch.AddSeconds(seconds).AddMilliseconds(fraction * 1000.0 / 4294967296.0);
                }
            }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                return null;
            }
        }
    }
}
