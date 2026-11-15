using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutomationTest.Core.Expressions;

namespace AutomationTest.Core.Email
{
    /// <summary>How a profile logs in.</summary>
    public enum EmailAuth
    {
        /// <summary>User name and password (use an app password when the account has two-step verification).</summary>
        Password,
        /// <summary>Sign in with Google in the browser (OAuth2): two-step verification happens there, once.</summary>
        Google,
        /// <summary>Sign in with Microsoft (Microsoft 365, Outlook.com) in the browser (OAuth2).</summary>
        Microsoft,
        /// <summary>Another OAuth2 provider: the addresses and scope are given in the profile.</summary>
        Custom
    }

    /// <summary>
    /// OAuth2 sign-in for mail accounts that use multi-factor authentication. The person signs in once in the browser (the
    /// provider asks for the second factor there); the program gets a refresh token and later swaps it for short-lived access
    /// tokens, which it gives to the mail server (XOAUTH2). The program never sees the password. Authorization code flow with
    /// PKCE and a loopback address, so no client secret is needed except where the provider insists on one (Google).
    /// </summary>
    public static class OAuth2
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, KeyValuePair<string, DateTime>> Cache = new Dictionary<string, KeyValuePair<string, DateTime>>(); // refresh token -> access token, expiry (UTC)

        /// <summary>Test hook: how long before the end a cached access token is renewed.</summary>
        internal static TimeSpan Margin = TimeSpan.FromMinutes(2);

        public sealed class Endpoints
        {
            public string AuthUrl, TokenUrl, Scope;
            /// <summary>Extra parameters of the sign-in address.</summary>
            public string Extra = "";
        }

        public static Endpoints EndpointsOf(EmailProfile p)
        {
            switch (p.Auth)
            {
                case EmailAuth.Google:
                    return new Endpoints
                    {
                        AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth",
                        TokenUrl = "https://oauth2.googleapis.com/token",
                        Scope = "https://mail.google.com/",
                        Extra = "&access_type=offline&prompt=consent" // without these Google gives no refresh token
                    };
                case EmailAuth.Microsoft:
                    string tenant = string.IsNullOrWhiteSpace(p.Tenant) ? "common" : p.Tenant.Trim();
                    return new Endpoints
                    {
                        AuthUrl = "https://login.microsoftonline.com/" + Uri.EscapeDataString(tenant) + "/oauth2/v2.0/authorize",
                        TokenUrl = "https://login.microsoftonline.com/" + Uri.EscapeDataString(tenant) + "/oauth2/v2.0/token",
                        Scope = "offline_access https://outlook.office.com/IMAP.AccessAsUser.All https://outlook.office.com/SMTP.Send"
                    };
                case EmailAuth.Custom:
                    return new Endpoints { AuthUrl = (p.AuthUrl ?? "").Trim(), TokenUrl = (p.TokenUrl ?? "").Trim(), Scope = (p.Scope ?? "").Trim() };
                default:
                    throw new InvalidOperationException("Email: this profile does not use OAuth2.");
            }
        }

        // ------------------------------------------------------------------ PKCE and the sign-in address

        public static string NewVerifier()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        /// <summary>The PKCE challenge of a verifier (S256, RFC 7636).</summary>
        public static string Challenge(string verifier)
        {
            using (var sha = SHA256.Create()) return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static string BuildAuthUrl(EmailProfile p, string redirect, string state, string challenge)
        {
            var e = EndpointsOf(p);
            if (string.IsNullOrEmpty(e.AuthUrl)) throw new InvalidOperationException("Email: the profile has no sign-in address.");
            return e.AuthUrl + (e.AuthUrl.Contains("?") ? "&" : "?")
                + "response_type=code"
                + "&client_id=" + Uri.EscapeDataString(p.ClientId ?? "")
                + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                + "&scope=" + Uri.EscapeDataString(e.Scope ?? "")
                + "&state=" + Uri.EscapeDataString(state)
                + "&code_challenge=" + Uri.EscapeDataString(challenge)
                + "&code_challenge_method=S256"
                + (p.Auth == EmailAuth.Google && !string.IsNullOrEmpty(p.User) ? "&login_hint=" + Uri.EscapeDataString(p.User) : "")
                + e.Extra;
        }

        // ------------------------------------------------------------------ sign in (the browser part)

        /// <summary>
        /// Signs in: <paramref name="openBrowser"/> gets the address to show the person; the provider sends the browser back to
        /// this computer, and the code is exchanged for tokens. Returns the refresh token. Gives up after <paramref name="timeout"/>.
        /// </summary>
        public static string SignIn(EmailProfile p, Action<string> openBrowser, TimeSpan timeout, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(p.ClientId)) throw new InvalidOperationException("Email: enter the Client ID first.");
            if (p.Auth == EmailAuth.Password) throw new InvalidOperationException("Email: choose Google, Microsoft or Custom to sign in.");
            EndpointsOf(p);

            HttpListener listener = null;
            int port = 0;
            var random = new Random();
            for (int attempt = 0; listener == null; attempt++)
            {
                port = random.Next(20000, 60000);
                var candidate = new HttpListener();
                candidate.Prefixes.Add("http://localhost:" + port + "/");
                try { candidate.Start(); listener = candidate; }
                catch (HttpListenerException) when (attempt < 20) { candidate.Close(); }
                catch (HttpListenerException ex) { throw new InvalidOperationException("Email sign-in: cannot listen on this computer: " + ex.Message); }
            }
            string redirect = "http://localhost:" + port + "/";
            string verifier = NewVerifier();
            string state = NewVerifier();
            try
            {
                openBrowser(BuildAuthUrl(p, redirect, state, Challenge(verifier)));
                string code = WaitForCode(listener, state, timeout, token);
                var tokens = Post(p, new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" }, { "code", code }, { "redirect_uri", redirect }, { "code_verifier", verifier }
                }, token);
                if (string.IsNullOrEmpty(tokens.Refresh))
                    throw new InvalidOperationException("Email sign-in: the provider gave no refresh token" + (p.Auth == EmailAuth.Google ? " (remove the program's access in your Google account and sign in again)." : "."));
                Remember(tokens.Refresh, tokens);
                return tokens.Refresh;
            }
            finally { listener.Close(); }
        }

        private static string WaitForCode(HttpListener listener, string state, TimeSpan timeout, CancellationToken token)
        {
            var until = DateTime.UtcNow + timeout;
            Task<HttpListenerContext> pending = null; // one request is waited for at a time: a new wait would lose requests
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var remaining = until - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) throw new InvalidOperationException("Email sign-in: no answer from the browser in " + (int)timeout.TotalMinutes + " minute(s); try again.");
                if (pending == null) pending = listener.GetContextAsync();
                try
                {
                    if (!pending.Wait((int)Math.Min(remaining.TotalMilliseconds, 500), token)) continue;
                }
                catch (AggregateException ex) { throw new InvalidOperationException("Email sign-in: the listener stopped: " + ex.GetBaseException().Message); }
                var context = pending.Result;
                pending = null;
                var query = context.Request.QueryString;
                string error = query["error"], code = query["code"];
                if (code == null && error == null) { Reply(context, 404, "Not found"); continue; } // a favicon or something else
                if (query["state"] != state) { Reply(context, 400, "This sign-in does not belong to the program that is waiting. Close this tab."); continue; }
                if (error != null)
                {
                    Reply(context, 200, "Sign-in was not completed (" + WebUtility.HtmlEncode(error) + "). You can close this tab.");
                    throw new InvalidOperationException("Email sign-in: the provider said \"" + error + "\"" + (query["error_description"] != null ? ": " + query["error_description"] : "") + ".");
                }
                Reply(context, 200, "Signed in. You can close this tab and go back to Automation Test.");
                return code;
            }
        }

        private static void Reply(HttpListenerContext context, int status, string text)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes("<html><body style=\"font-family:sans-serif\"><p>" + WebUtility.HtmlEncode(text) + "</p></body></html>");
                context.Response.StatusCode = status;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.OutputStream.Write(body, 0, body.Length);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException || ex is InvalidOperationException) { }
        }

        // ------------------------------------------------------------------ tokens

        private sealed class Tokens
        {
            public string Access, Refresh;
            public int ExpiresIn = 3600;
        }

        private static Tokens Post(EmailProfile p, Dictionary<string, string> form, CancellationToken token)
        {
            var e = EndpointsOf(p);
            if (string.IsNullOrEmpty(e.TokenUrl)) throw new InvalidOperationException("Email: the profile has no token address.");
            form["client_id"] = p.ClientId ?? "";
            if (!string.IsNullOrEmpty(p.ClientSecret)) form["client_secret"] = p.ClientSecret;
            string text;
            int status;
            try
            {
                using (var content = new FormUrlEncodedContent(form))
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    using (var response = Http.PostAsync(e.TokenUrl, content, timeout.Token).GetAwaiter().GetResult())
                    {
                        status = (int)response.StatusCode;
                        text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); throw new InvalidOperationException("Email sign-in: no answer from the provider's token server."); }
            catch (HttpRequestException ex) { throw new InvalidOperationException("Email sign-in: cannot reach the provider's token server: " + ex.GetBaseException().Message); }

            JsonValue json;
            if (!JsonValue.TryParse(text, out json)) throw new InvalidOperationException("Email sign-in: the token server answered " + status + " without a readable answer.");
            string error = Member(json, "error");
            if (status < 200 || status > 299 || error.Length > 0)
            {
                string detail = Member(json, "error_description");
                throw new InvalidOperationException("Email sign-in: the provider refused (" + (error.Length > 0 ? error : status.ToString()) + ")" + (detail.Length > 0 ? ": " + Scrub(p, detail) : "")
                    + (error == "invalid_grant" ? ". Sign in again in the profile." : "."));
            }
            string access = Member(json, "access_token");
            if (access.Length == 0) throw new InvalidOperationException("Email sign-in: the provider gave no access token.");
            int seconds;
            return new Tokens { Access = access, Refresh = Member(json, "refresh_token"), ExpiresIn = int.TryParse(Member(json, "expires_in"), out seconds) && seconds > 0 ? seconds : 3600 };
        }

        private static string Member(JsonValue json, string name)
        {
            try { var value = json.GetMember(name); return value is string s ? s : value is double d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""; }
            catch (ExpressionException) { return ""; }
        }

        private static string Scrub(EmailProfile p, string text)
        {
            foreach (string secret in new[] { p.ClientSecret, p.RefreshToken })
                if (!string.IsNullOrEmpty(secret)) text = text.Replace(secret, "****");
            return text;
        }

        private static void Remember(string refresh, Tokens tokens)
        {
            lock (CacheLock) Cache[refresh] = new KeyValuePair<string, DateTime>(tokens.Access, DateTime.UtcNow.AddSeconds(tokens.ExpiresIn));
        }

        /// <summary>The access token currently remembered for a profile (for hiding it in messages); null if none.</summary>
        public static string CachedAccessToken(EmailProfile p)
        {
            lock (CacheLock)
                return !string.IsNullOrEmpty(p.RefreshToken) && Cache.TryGetValue(p.RefreshToken, out var entry) ? entry.Key : null;
        }

        /// <summary>
        /// A valid access token for the profile: the remembered one, or a new one got with the refresh token. When the provider
        /// sends a new refresh token it is kept in the profile and given to <paramref name="store"/> to save.
        /// </summary>
        public static string GetAccessToken(EmailProfile p, IEmailProfiles store, CancellationToken token)
        {
            if (string.IsNullOrEmpty(p.RefreshToken))
                throw new InvalidOperationException("Email (" + p.Name + "): not signed in. Open the profile (mail icon next to Category), press Sign in and try again.");
            lock (CacheLock)
                if (Cache.TryGetValue(p.RefreshToken, out var entry) && entry.Value - Margin > DateTime.UtcNow) return entry.Key;

            Tokens tokens;
            try
            {
                tokens = Post(p, new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", p.RefreshToken }, { "scope", EndpointsOf(p).Scope ?? "" } }, token);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("(" + p.Name + ")"))
            {
                throw new InvalidOperationException(ex.Message.Replace("Email sign-in:", "Email (" + p.Name + "):"));
            }
            string refresh = p.RefreshToken;
            if (!string.IsNullOrEmpty(tokens.Refresh) && tokens.Refresh != p.RefreshToken)
            {
                refresh = tokens.Refresh;
                p.RefreshToken = refresh; // the next call of this run uses the new one
                store?.UpdateRefreshToken(p.Name, refresh);
            }
            Remember(refresh, tokens);
            return tokens.Access;
        }
    }
}
