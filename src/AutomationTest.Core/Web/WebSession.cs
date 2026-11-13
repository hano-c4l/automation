using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AutomationTest.Core.Model;

namespace AutomationTest.Core.Web
{
    /// <summary>
    /// The browser the Web actions drive: one connection to the extension and one current tab, shared by a whole run.
    /// Elements are found with a CSS selector, or "text=Label" for an element whose visible text contains Label.
    /// </summary>
    public sealed class WebSession : IDisposable
    {
        private const int CallTimeoutMs = 15000;
        // A click that opens a JavaScript dialog does not return until the dialog is answered; this is how long the click waits for that.
        private const int ClickReleaseTimeoutMs = 1500;

        private static readonly object SharedLock = new object();
        private static WebSession _shared;

        /// <summary>
        /// The one session of the program (one extension connection, one listening port). Created on first use; if the port is
        /// busy that fails with a message, and the next use tries again.
        /// </summary>
        public static WebSession Shared
        {
            get
            {
                lock (SharedLock)
                {
                    if (_shared == null) _shared = new WebSession();
                    return _shared;
                }
            }
        }

        /// <summary>The session of the program when it has been created (null before the first use); never creates it.</summary>
        public static WebSession SharedIfCreated
        {
            get { lock (SharedLock) return _shared; }
        }

        private readonly BrowserBridge _bridge;
        private readonly bool _ownsBridge;
        // The tab the script is using, per browser (tab ids belong to one browser).
        private readonly Dictionary<BrowserKind, int> _tabs = new Dictionary<BrowserKind, int>();

        public WebSession(BrowserBridge bridge = null)
        {
            _ownsBridge = bridge == null;
            _bridge = bridge ?? new BrowserBridge();
            if (_ownsBridge)
            {
                try { _bridge.Start(); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Cannot listen for the browser extension on port " + _bridge.Port +
                        " (another copy of the program may be running): " + ex.Message);
                }
            }
        }

        /// <summary>True while the extension of that browser is connected.</summary>
        public bool IsConnected(BrowserKind browser) => _bridge.IsConnected(browser);

        /// <summary>Why the last connection of an extension was turned away (for diagnosis).</summary>
        public string LastRejection => _bridge.LastRejection;

        /// <summary>Forgets the tab each browser was using: the next action that needs one uses the tab that is in front then.</summary>
        public void ResetTab()
        {
            lock (_tabs) _tabs.Clear();
        }

        /// <summary>Runs one Web action.</summary>
        /// <returns>What GetText read (text, true/false or a number) or what a Script returned; null for the other actions.</returns>
        public object Execute(WebSettings w, string text, CancellationToken token)
        {
            switch (w.Kind)
            {
                case WebKind.NewTab: NewTab(w, token); break;
                case WebKind.WaitElement: FindElement(w, token); break;
                case WebKind.Input: Input(w, text, token); break;
                case WebKind.Click: Click(w, token); break;
                case WebKind.Dialog: Dialog(w, token); break;
                case WebKind.GetText: return GetText(w, token);
                case WebKind.Script: return RunScript(w, text, token);
                case WebKind.SwitchTab: SwitchTab(w, token); break;
            }
            return null;
        }

        private static string ExeOf(BrowserKind browser)
        {
            return browser == BrowserKind.Edge ? "msedge.exe" : "chrome.exe";
        }

        /// <summary>
        /// True when the browser has a window open. A browser kept alive in the background (Startup Boost, background apps) has
        /// processes but no window, and is treated as not running.
        /// </summary>
        private static bool HasWindow(BrowserKind browser)
        {
            string name = browser == BrowserKind.Edge ? "msedge" : "chrome";
            foreach (var process in Process.GetProcessesByName(name))
            {
                try { if (process.MainWindowHandle != IntPtr.Zero) return true; }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            return false;
        }

        private string ExtensionMissing(BrowserKind browser)
        {
            return "The Automation Test extension of " + browser + " did not connect. Install it in " + browser + " (Load unpacked: the BrowserExtension folder), make sure it is enabled, " +
                "and check that " + browser + " is not connected twice (another profile)." + (string.IsNullOrEmpty(_bridge.LastRejection) ? "" : " Last refusal: " + _bridge.LastRejection + ".");
        }

        private void EnsureConnected(BrowserKind browser, CancellationToken token)
        {
            if (_bridge.IsConnected(browser)) return;
            if (!HasWindow(browser))
                throw new InvalidOperationException(browser + " is not running. Use a New Tab action to start it.");
            // A browser that has been idle puts its extension to sleep; it wakes within about 30 seconds and then connects.
            if (!_bridge.WaitConnected(browser, 35000, token)) throw new InvalidOperationException(ExtensionMissing(browser));
        }

        private object Call(BrowserKind browser, string cmd, Dictionary<string, object> args, CancellationToken token, int timeoutMs = CallTimeoutMs)
        {
            return _bridge.Call(browser, cmd, args, timeoutMs, token);
        }

        private int CurrentTab(BrowserKind browser, CancellationToken token)
        {
            EnsureConnected(browser, token);
            int known;
            bool has;
            lock (_tabs) has = _tabs.TryGetValue(browser, out known);
            if (has)
            {
                var exists = (Dictionary<string, object>)Call(browser, "tabExists", new Dictionary<string, object> { { "tabId", known } }, token);
                if (exists["exists"] is bool b && b) return known;
            }
            var active = (Dictionary<string, object>)Call(browser, "activeTab", null, token);
            int id = Convert.ToInt32(active["tabId"]);
            lock (_tabs) _tabs[browser] = id;
            return id;
        }

        private void NewTab(WebSettings w, CancellationToken token)
        {
            var browser = w.Browser;
            bool launched = false;
            if (!_bridge.IsConnected(browser))
            {
                if (HasWindow(browser))
                {
                    if (!_bridge.WaitConnected(browser, 35000, token)) throw new InvalidOperationException(ExtensionMissing(browser));
                }
                else
                {
                    try { Process.Start(new ProcessStartInfo(ExeOf(browser)) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
                    {
                        throw new InvalidOperationException("Cannot start " + browser + ": " + ex.Message);
                    }
                    launched = true;
                    if (!_bridge.WaitConnected(browser, (int)Math.Min(Math.Max(w.TimeMs, 5000), int.MaxValue), token))
                        throw new InvalidOperationException(browser + " started. " + ExtensionMissing(browser));
                }
            }

            var args = new Dictionary<string, object>
            {
                { "url", string.IsNullOrWhiteSpace(w.Url) ? "" : NormalizeUrl(w.Url) },
                { "newTab", true },
                { "timeoutMs", w.TimeMs }
            };
            if (launched)
            {
                // A browser started just now shows its start page: use that tab instead of opening a second one.
                var active = (Dictionary<string, object>)Call(browser, "activeTab", null, token);
                string url = Convert.ToString(active["url"]) ?? "";
                if (IsStartPage(url))
                {
                    args["newTab"] = false;
                    args["tabId"] = Convert.ToInt32(active["tabId"]);
                }
            }
            var reply = (Dictionary<string, object>)Call(browser, "openUrl", args, token, (int)Math.Min(int.MaxValue, w.TimeMs + CallTimeoutMs));
            lock (_tabs) _tabs[browser] = Convert.ToInt32(reply["tabId"]);
        }

        private static bool IsStartPage(string url)
        {
            url = url.TrimEnd('/');
            return url == "chrome://newtab" || url == "edge://newtab" || url == "about:blank" || url == "";
        }

        private static string NormalizeUrl(string url)
        {
            url = url.Trim();
            return url.IndexOf("://", StringComparison.Ordinal) > 0 || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
        }

        /// <summary>The arguments of the page scripts for an element: selector, frame and which match (1 = first).</summary>
        private static string ElementArgs(WebSettings w)
        {
            return MiniJson.Write(w.Selector.Trim()) + "," + MiniJson.Write((w.Frame ?? "").Trim()) + "," + Math.Max(1, w.Index);
        }

        private object Cdp(BrowserKind browser, int tab, string method, Dictionary<string, object> parameters, CancellationToken token, int timeoutMs = CallTimeoutMs)
        {
            return Call(browser, "cdp", new Dictionary<string, object> { { "tabId", tab }, { "method", method }, { "params", parameters ?? new Dictionary<string, object>() } }, token, timeoutMs);
        }

        /// <summary>Waits until the element is on the page and visible; returns where its centre is (in page pixels).</summary>
        private double[] FindElement(WebSettings w, CancellationToken token, bool failIfMissing = true, bool requireVisible = true)
        {
            if (string.IsNullOrWhiteSpace(w.Selector)) throw new InvalidOperationException(w.Kind + ": no selector is set.");
            int tab = CurrentTab(w.Browser, token);
            string script = WebScripts.Locate + "(" + ElementArgs(w) + ")";
            var watch = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var reply = (Dictionary<string, object>)Cdp(w.Browser, tab, "Runtime.evaluate",
                    new Dictionary<string, object> { { "expression", script }, { "returnByValue", true } }, token);
                if (reply.TryGetValue("exceptionDetails", out object ex) && ex != null)
                    throw new InvalidOperationException("The page could not be searched: " + MiniJson.Write(ex));
                var value = (Dictionary<string, object>)((Dictionary<string, object>)reply["result"])["value"];
                if (value.TryGetValue("error", out object error)) throw new InvalidOperationException(error.ToString());
                if (value["found"] is bool found && found && (!requireVisible || (value["visible"] is bool visible && visible)))
                    return new[] { Convert.ToDouble(value["x"]), Convert.ToDouble(value["y"]) };
                if (watch.ElapsedMilliseconds >= w.TimeMs)
                {
                    if (!failIfMissing) return null;
                    throw new InvalidOperationException(w.Kind + ": \"" + w.Selector.Trim() + "\" was not found on the page within " + w.TimeMs + " ms.");
                }
                Thread.Sleep(150);
            }
        }

        private void Input(WebSettings w, string text, CancellationToken token)
        {
            FindElement(w, token);
            int tab = CurrentTab(w.Browser, token);
            Cdp(w.Browser, tab, "Page.bringToFront", null, token);
            Cdp(w.Browser, tab, "Runtime.evaluate", new Dictionary<string, object>
            {
                { "expression", WebScripts.Focus + "(" + ElementArgs(w) + ")" }, { "returnByValue", true }
            }, token);
            // insertText goes through the browser's own input path, so frameworks see the change and Vietnamese text is typed as is.
            if (string.IsNullOrEmpty(text)) Cdp(w.Browser, tab, "Input.dispatchKeyEvent", new Dictionary<string, object> { { "type", "rawKeyDown" }, { "key", "Delete" }, { "code", "Delete" }, { "windowsVirtualKeyCode", 46 } }, token);
            else Cdp(w.Browser, tab, "Input.insertText", new Dictionary<string, object> { { "text", text } }, token);
        }

        private void Click(WebSettings w, CancellationToken token)
        {
            var at = FindElement(w, token);
            int tab = CurrentTab(w.Browser, token);
            Cdp(w.Browser, tab, "Page.bringToFront", null, token);
            Thread.Sleep(100); // the viewport of a tab that was in the background needs a moment
            at = FindElement(w, token); // again: the position can change once the tab is in front
            Func<string, int, Dictionary<string, object>> mouse = (type, count) => new Dictionary<string, object>
            {
                { "type", type }, { "x", at[0] }, { "y", at[1] }, { "button", "left" }, { "clickCount", count }
            };
            Cdp(w.Browser, tab, "Input.dispatchMouseEvent", new Dictionary<string, object> { { "type", "mouseMoved" }, { "x", at[0] }, { "y", at[1] } }, token);
            Cdp(w.Browser, tab, "Input.dispatchMouseEvent", mouse("mousePressed", 1), token);
            try
            {
                Cdp(w.Browser, tab, "Input.dispatchMouseEvent", mouse("mouseReleased", 1), token, ClickReleaseTimeoutMs);
            }
            catch (BridgeTimeoutException)
            {
                // The click opened a JavaScript dialog; a Dialog action answers it.
            }
        }

        /// <summary>
        /// Lets the user point at an element in the tab that is in front in that browser (and brings its window forward). Returns false
        /// when cancelled (Esc, the token, or the time ran out).
        /// </summary>
        public bool PickElement(BrowserKind browser, CancellationToken token, int timeoutMs, out string selector, out string frame, out int index)
        {
            selector = ""; frame = ""; index = 1;
            // The tab the user is looking at, not the one a script used before; the script's own tab is left alone.
            EnsureConnected(browser, token);
            int tab = Convert.ToInt32(((Dictionary<string, object>)Call(browser, "activeTab", null, token))["tabId"]);
            // Windows only lets the program in front hand the foreground on: allow the browser to take it.
            AutomationTest.Core.Native.NativeMethods.AllowSetForegroundWindow(-1);
            Call(browser, "activateTab", new Dictionary<string, object> { { "tabId", tab } }, token);
            Cdp(browser, tab, "Runtime.evaluate", new Dictionary<string, object> { { "expression", PickScript.Install }, { "returnByValue", true } }, token);
            var watch = Stopwatch.StartNew();
            try
            {
                while (!token.IsCancellationRequested && watch.ElapsedMilliseconds < timeoutMs)
                {
                    var reply = (Dictionary<string, object>)Cdp(browser, tab, "Runtime.evaluate", new Dictionary<string, object> { { "expression", PickScript.Read }, { "returnByValue", true } }, token);
                    var result = (Dictionary<string, object>)reply["result"];
                    if (result.TryGetValue("value", out object value) && value is string json)
                    {
                        var data = (Dictionary<string, object>)MiniJson.Parse(json);
                        if (data.ContainsKey("cancelled")) return false;
                        selector = Convert.ToString(data["selector"]);
                        frame = Convert.ToString(data["frame"]);
                        index = data.TryGetValue("index", out object picked) ? Math.Max(1, Convert.ToInt32(picked)) : 1;
                        return true;
                    }
                    Thread.Sleep(150);
                }
                return false;
            }
            finally
            {
                try { Cdp(browser, tab, "Runtime.evaluate", new Dictionary<string, object> { { "expression", PickScript.Cancel } }, CancellationToken.None, 3000); }
                catch (InvalidOperationException) { }
            }
        }

        private object GetText(WebSettings w, CancellationToken token)
        {
            var browser = w.Browser;
            if (w.Get == WebGetKind.PageUrl || w.Get == WebGetKind.PageTitle)
            {
                int current = CurrentTab(browser, token);
                var info = (Dictionary<string, object>)Call(browser, "tabInfo", new Dictionary<string, object> { { "tabId", current } }, token);
                return Convert.ToString(info[w.Get == WebGetKind.PageUrl ? "url" : "title"]) ?? "";
            }
            if (string.IsNullOrWhiteSpace(w.Selector)) throw new InvalidOperationException("Get Text: no selector is set.");
            int tab = CurrentTab(browser, token);
            if (w.Get == WebGetKind.Count)
            {
                var counted = (Dictionary<string, object>)Cdp(browser, tab, "Runtime.evaluate", new Dictionary<string, object>
                {
                    { "expression", WebScripts.Count + "(" + MiniJson.Write(w.Selector.Trim()) + "," + MiniJson.Write((w.Frame ?? "").Trim()) + ")" },
                    { "returnByValue", true }
                }, token);
                object number = ((Dictionary<string, object>)counted["result"]).TryGetValue("value", out object cv) ? cv : null;
                if (number is Dictionary<string, object> failed) throw new InvalidOperationException(Convert.ToString(failed["error"]));
                return Convert.ToDouble(number ?? 0);
            }
            if (w.Get == WebGetKind.Attribute && string.IsNullOrWhiteSpace(w.Attribute)) throw new InvalidOperationException("Get Text: no attribute name is set.");

            // The element only has to be there: a hidden field or a collapsed block can be read too.
            if (FindElement(w, token, w.Required, false) == null) return w.Get == WebGetKind.Checked ? (object)false : "";
            var reply = (Dictionary<string, object>)Cdp(browser, tab, "Runtime.evaluate", new Dictionary<string, object>
            {
                { "expression", WebScripts.Read + "(" + ElementArgs(w) + "," + MiniJson.Write(w.Get.ToString()) + "," + MiniJson.Write((w.Attribute ?? "").Trim()) + ")" },
                { "returnByValue", true }
            }, token);
            var result = (Dictionary<string, object>)reply["result"];
            result.TryGetValue("value", out object value);
            if (value is bool || value is string) return value;
            return w.Get == WebGetKind.Checked ? (object)false : "";
        }

        /// <summary>Runs JavaScript in the page like the browser console (top-level await and repeated let/const work).</summary>
        private object RunScript(WebSettings w, string code, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Script: there is no script to run.");
            var browser = w.Browser;
            int tab = CurrentTab(browser, token);
            var reply = (Dictionary<string, object>)Cdp(browser, tab, "Runtime.evaluate", new Dictionary<string, object>
            {
                { "expression", code },
                { "returnByValue", true },
                { "awaitPromise", true },
                { "replMode", true },
                { "userGesture", true },
                { "timeout", w.TimeMs }
            }, token, (int)Math.Min(int.MaxValue, w.TimeMs + CallTimeoutMs));
            if (reply.TryGetValue("exceptionDetails", out object details) && details is Dictionary<string, object> d)
            {
                string message = null;
                if (d.TryGetValue("exception", out object ex) && ex is Dictionary<string, object> exception && exception.TryGetValue("description", out object desc)) message = Convert.ToString(desc);
                if (string.IsNullOrEmpty(message) && d.TryGetValue("text", out object text)) message = Convert.ToString(text);
                string at = d.TryGetValue("lineNumber", out object line) ? " (line " + (Convert.ToInt32(line) + 1) + ")" : "";
                throw new InvalidOperationException("Script failed" + at + ": " + (message ?? "unknown error"));
            }
            var result = (Dictionary<string, object>)reply["result"];
            if (!result.TryGetValue("value", out object value) || value == null) return "";
            if (value is string || value is bool) return value;
            if (value is long l) return (double)l;
            if (value is double) return value;
            return MiniJson.Write(value); // an object or an array: as JSON text
        }

        private List<TabInfo> ListTabs(BrowserKind browser, CancellationToken token, out int lastWindowId)
        {
            var reply = (Dictionary<string, object>)Call(browser, "tabs", null, token);
            lastWindowId = reply.TryGetValue("lastWindowId", out object last) && last != null ? Convert.ToInt32(last) : -1;
            var list = new List<TabInfo>();
            foreach (var item in (object[])reply["tabs"])
            {
                var t = (Dictionary<string, object>)item;
                list.Add(new TabInfo
                {
                    Id = Convert.ToInt32(t["id"]),
                    WindowId = Convert.ToInt32(t["windowId"]),
                    Index = Convert.ToInt32(t["index"]),
                    Title = Convert.ToString(t["title"]) ?? "",
                    Url = Convert.ToString(t["url"]) ?? ""
                });
            }
            return list;
        }

        private void SwitchTab(WebSettings w, CancellationToken token)
        {
            bool hasPattern = !string.IsNullOrWhiteSpace(w.Title) || !string.IsNullOrWhiteSpace(w.Url);
            EnsureConnected(w.Browser, token);
            var watch = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                TabInfo chosen;
                try { chosen = TabSelector.Choose(ListTabs(w.Browser, token, out int lastWindow), lastWindow, w.Title, w.Url, w.Index); }
                catch (ArgumentException ex) { throw new InvalidOperationException("Switch Tab: " + ex.Message); }
                if (chosen != null)
                {
                    Call(w.Browser, "activateTab", new Dictionary<string, object> { { "tabId", chosen.Id } }, token);
                    lock (_tabs) _tabs[w.Browser] = chosen.Id;
                    return;
                }
                // Index only: there is no tab at all. With patterns: nothing matches (yet), so wait for it up to the timeout.
                if (!hasPattern || watch.ElapsedMilliseconds >= w.TimeMs)
                {
                    string what = (string.IsNullOrWhiteSpace(w.Title) ? "" : " title /" + w.Title + "/") + (string.IsNullOrWhiteSpace(w.Url) ? "" : " address /" + w.Url + "/");
                    throw new InvalidOperationException("Switch Tab: no " + w.Browser + " tab" + (hasPattern ? " with" + what + " within " + w.TimeMs + " ms." : " found."));
                }
                Thread.Sleep(200);
            }
        }

        private void Dialog(WebSettings w, CancellationToken token)
        {
            int tab = CurrentTab(w.Browser, token);
            var watch = Stopwatch.StartNew();
            var parameters = new Dictionary<string, object> { { "accept", w.Accept } };
            if (w.Accept && !string.IsNullOrEmpty(w.Text)) parameters["promptText"] = w.Text;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    Cdp(w.Browser, tab, "Page.handleJavaScriptDialog", parameters, token);
                    return;
                }
                catch (BridgeTimeoutException) { throw; }
                catch (InvalidOperationException) when (watch.ElapsedMilliseconds < w.TimeMs)
                {
                    Thread.Sleep(150); // no dialog yet
                }
                catch (InvalidOperationException)
                {
                    throw new InvalidOperationException("Dialog: no JavaScript dialog appeared within " + w.TimeMs + " ms.");
                }
            }
        }

        public void Dispose()
        {
            if (_ownsBridge) _bridge.Dispose();
        }
    }
}
