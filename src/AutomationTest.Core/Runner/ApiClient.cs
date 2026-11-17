using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using AutomationTest.Core.Expressions;

namespace AutomationTest.Core.Runner
{
    /// <summary>One call of the API actions.</summary>
    public sealed class ApiRequest
    {
        public string Method = "GET";
        public string Url = "";
        public List<KeyValuePair<string, string>> Headers = new List<KeyValuePair<string, string>>();
        /// <summary>The text to send (null = nothing).</summary>
        public string Body;
        public long TimeoutMs = 30000;
        /// <summary>
        /// The Download toggle: the file to save the answer in (null = keep the answer as text). An answer that is not 2xx is not
        /// saved; it is read as text instead (within the limit) so its error can be seen in {r.Body}.
        /// </summary>
        public string SaveTo;
        public bool Overwrite;
        /// <summary>
        /// The biggest answer that is read into a variable (after unpacking gzip; Settings, API answer limit). A bigger one is not read
        /// any further and not kept: the status and headers still come back, with {r.TooBig} true. So a huge or endless answer cannot
        /// use up the memory and the run goes on. A file save has no limit.
        /// </summary>
        public long MaxAnswerBytes = ApiClient.DefaultMaxAnswerBytes;
    }

    /// <summary>
    /// Calls a web address (HTTP and HTTPS; the certificate of a secure address is always checked). A status that is not 2xx is an
    /// answer, not an error: {r.Ok} and {r.Status} tell it. Network problems and a missing answer are errors.
    /// </summary>
    public static class ApiClient
    {
        public const long DefaultMaxAnswerBytes = 50L * 1024 * 1024;

        private static readonly HttpClient Client = Create();

        /// <summary>The text of the answer, read piece by piece and never more than the limit; null when it is bigger (it is not read on).</summary>
        private static string ReadText(HttpResponseMessage response, ApiRequest request, CancellationToken token)
        {
            long limit = request.MaxAnswerBytes > 0 ? request.MaxAnswerBytes : DefaultMaxAnswerBytes;
            long? announced = response.Content.Headers.ContentLength;
            if (announced.HasValue && announced.Value > limit) return null;
            using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
            using (var kept = new MemoryStream())
            {
                var buffer = new byte[81920];
                while (true)
                {
                    int read = input.ReadAsync(buffer, 0, buffer.Length, token).GetAwaiter().GetResult();
                    if (read <= 0) break;
                    if (kept.Length + read > limit) return null; // the rest is not fetched: the response is closed by the caller
                    kept.Write(buffer, 0, read);
                }
                kept.Position = 0;
                // The character set the server names, else UTF-8; a byte order mark at the start wins (as the framework does).
                Encoding encoding = new UTF8Encoding(false);
                string charset = response.Content.Headers.ContentType != null ? response.Content.Headers.ContentType.CharSet : null;
                if (!string.IsNullOrWhiteSpace(charset))
                {
                    try { encoding = Encoding.GetEncoding(charset.Trim().Trim('"', '\'')); }
                    catch (ArgumentException) { }
                }
                using (var reader = new StreamReader(kept, encoding, true)) return reader.ReadToEnd();
            }
        }

        /// <summary>A size for a message: 50 MB, or a number of bytes when it is not whole megabytes.</summary>
        public static string SizeText(long bytes)
        {
            return bytes % (1024 * 1024) == 0 ? (bytes / (1024 * 1024)) + " MB" : bytes + " bytes";
        }

        private static HttpClient Create()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
            // TLS 1.3 (the enum has no name for it on this framework) where Windows has it, for servers that accept nothing older.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; }
            catch (NotSupportedException) { }
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            return new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan }; // each call has its own time limit
        }

        /// <summary>The address without its query and user name, to say in a message (a query often holds a key).</summary>
        public static string SafeUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out uri)) return (url ?? "").Trim().Split('?')[0];
            return uri.GetLeftPart(UriPartial.Authority).Replace(uri.UserInfo + "@", "") + uri.AbsolutePath;
        }

        public static ApiValue Send(ApiRequest request, CancellationToken token)
        {
            Uri uri;
            if (!Uri.TryCreate((request.Url ?? "").Trim(), UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("API: \"" + SafeUrl(request.Url) + "\" is not a web address (it must start with http:// or https://).");

            using (var message = new HttpRequestMessage(new HttpMethod(request.Method), uri))
            {
                if (request.Body != null && request.Method != "GET" && request.Method != "HEAD")
                {
                    string trimmed = request.Body.TrimStart();
                    var content = new StringContent(request.Body, new UTF8Encoding(false), trimmed.StartsWith("{") || trimmed.StartsWith("[") ? "application/json" : "text/plain");
                    message.Content = content;
                }
                bool agentGiven = false;
                foreach (var header in request.Headers)
                {
                    if (string.Equals(header.Key, "User-Agent", StringComparison.OrdinalIgnoreCase)) agentGiven = true;
                    if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        MediaTypeHeaderValue type;
                        if (!MediaTypeHeaderValue.TryParse(header.Value, out type)) throw new InvalidOperationException("API: \"" + header.Value + "\" is not a content type.");
                        if (message.Content == null) message.Content = new StringContent("");
                        message.Content.Headers.ContentType = type;
                        continue;
                    }
                    if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        if (message.Content == null) message.Content = new StringContent("");
                        if (!message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
                            throw new InvalidOperationException("API: the header \"" + header.Key + "\" cannot be set.");
                    }
                }
                if (!agentGiven) message.Headers.TryAddWithoutValidation("User-Agent", "AutomationTest");

                long limit = request.TimeoutMs > 0 ? request.TimeoutMs : 30000;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter((int)Math.Min(limit, int.MaxValue));
                    try
                    {
                        // Only the headers first: the body is then read with a limit, or written to the file as it comes.
                        using (var response = Client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).GetAwaiter().GetResult())
                        {
                            string body = "";
                            bool tooBig = false;
                            string savedTo = "";
                            if (request.SaveTo != null)
                            {
                                // Download: Timeout is the wait for the answer to begin. A big file then takes as long as it takes;
                                // only Stop ends it (the timer is turned off, the token still follows Stop).
                                timeout.CancelAfter(System.Threading.Timeout.Infinite);
                                if (response.IsSuccessStatusCode) { Save(response, request, timeout.Token); savedTo = request.SaveTo; }
                                else if (request.Method != "HEAD")
                                {
                                    body = ReadText(response, request, timeout.Token);
                                    if (body == null) { body = ""; tooBig = true; }
                                }
                            }
                            else if (request.Method != "HEAD")
                            {
                                body = ReadText(response, request, timeout.Token);
                                if (body == null) { body = ""; tooBig = true; }
                            }

                            var headers = new List<KeyValuePair<string, string>>();
                            foreach (var h in response.Headers) headers.Add(new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)));
                            foreach (var h in response.Content.Headers) headers.Add(new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)));
                            string type = response.Content.Headers.ContentType != null ? response.Content.Headers.ContentType.ToString() : "";
                            return new ApiValue((int)response.StatusCode, response.ReasonPhrase, type, body, headers, tooBig, savedTo);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested(); // Stop
                        throw new InvalidOperationException("API: no answer from " + SafeUrl(request.Url) + " within " + TimeText(limit) + ".");
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new InvalidOperationException("API: cannot reach " + SafeUrl(request.Url) + ": " + ex.GetBaseException().Message);
                    }
                    catch (IOException ex) when (request.SaveTo == null)
                    {
                        throw new InvalidOperationException("API: the answer from " + SafeUrl(request.Url) + " was cut: " + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void Save(HttpResponseMessage response, ApiRequest request, CancellationToken token)
        {
            string path = request.SaveTo;
            if (File.Exists(path) && !request.Overwrite)
                throw new InvalidOperationException("API: \"" + path + "\" exists already (turn on Overwrite to replace it).");
            if (Directory.Exists(path)) throw new InvalidOperationException("API: \"" + path + "\" is a folder; give the file name.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".part";
            try
            {
                using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyToAsync(output, 81920, token).GetAwaiter().GetResult();
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                try { File.Delete(temp); } catch (IOException) { }
                throw new InvalidOperationException("API: cannot save to \"" + path + "\": " + ex.Message);
            }
            catch (OperationCanceledException)
            {
                try { File.Delete(temp); } catch (IOException) { }
                throw;
            }
        }

        private static string TimeText(long ms)
        {
            return ms % 1000 == 0 ? (ms / 1000) + " s" : ms + " ms";
        }

        /// <summary>The lines "Name: value" of the Headers field; a value that starts with = is an expression. The problem is told in <paramref name="problem"/>.</summary>
        public static List<Tuple<string, string, bool>> ParseHeaders(string text, out string problem)
        {
            problem = null;
            var list = new List<Tuple<string, string, bool>>();
            foreach (string raw in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int colon = line.IndexOf(':');
                string name = colon > 0 ? line.Substring(0, colon).Trim() : "";
                if (name.Length == 0 || name.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_'))) { problem = "\"" + line + "\" is not \"Name: value\" (a name has letters, digits, - and _)."; continue; }
                string value = line.Substring(colon + 1).Trim();
                bool expression = value.StartsWith("=");
                if (expression) value = value.Substring(1).Trim();
                if (expression && value.Length == 0) { problem = "The header " + name + " has = but no expression."; continue; }
                list.Add(Tuple.Create(name, value, expression));
            }
            return list;
        }
    }
}
