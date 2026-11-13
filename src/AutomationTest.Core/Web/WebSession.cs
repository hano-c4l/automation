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

        // resolve(sel, frame): the element for a CSS selector, or for "text=Label" the innermost visible-text match, inside the
        // (same-origin) iframes named by frame ("sel >>> sel"). Returns { el, ox, oy } (ox, oy: where the frame's document sits in
        // the page), { error }, or null when something is not there yet.
        private const string ResolveJs = @"function resolve(sel, frame) {
  var doc = document, ox = 0, oy = 0;
  var frames = frame ? frame.split('>>>') : [];
  for (var i = 0; i < frames.length; i++) {
    var f = doc.querySelector(frames[i].trim());
    if (!f) return null;
    var inner = null;
    try { inner = f.contentDocument; } catch (err) { inner = null; }
    if (!inner) return { error: 'The frame ' + frames[i].trim() + ' is from another site, which a script cannot enter.' };
    var fr = f.getBoundingClientRect();
    ox += fr.left + f.clientLeft; oy += fr.top + f.clientTop;
    doc = inner;
  }
  var el = null;
  if (sel.indexOf('text=') === 0) {
    var want = sel.slice(5).trim().toLowerCase();
    var hits = Array.prototype.slice.call(doc.querySelectorAll('body *')).filter(function (e) {
      var t = (e.innerText || e.value || '').trim().toLowerCase();
      return t.length > 0 && t.indexOf(want) >= 0;
    });
    el = hits.filter(function (e) { return !hits.some(function (o) { return o !== e && e.contains(o); }); })[0] || null;
  } else {
    el = doc.querySelector(sel);
  }
  return el ? { el: el, ox: ox, oy: oy } : null;
}";

        // Reports where the element is (in page pixels); the arguments are passed as JSON strings.
        private const string LocateScript = @"(function (sel, frame) {" + ResolveJs + @"
  var hit;
  try { hit = resolve(sel, frame); } catch (err) { return { error: 'Bad selector: ' + err.message }; }
  if (hit && hit.error) return { error: hit.error };
  if (!hit) return { found: false };
  var el = hit.el;
  el.scrollIntoView({ block: 'center', inline: 'center' });
  // The frames moved while scrolling: measure again.
  hit = resolve(sel, frame);
  var r = el.getBoundingClientRect();
  var visible = r.width > 0 && r.height > 0 && getComputedStyle(el).visibility !== 'hidden';
  return { found: true, visible: visible, x: hit.ox + r.left + r.width / 2, y: hit.oy + r.top + r.height / 2 };
})";

        // Focuses the element and selects its content, so typed text replaces it.
        private const string FocusScript = @"(function (sel, frame) {" + ResolveJs + @"
  var hit = resolve(sel, frame);
  if (!hit || hit.error) return false;
  var e = hit.el;
  e.focus();
  if (e.select) e.select(); else e.ownerDocument.execCommand('selectAll');
  return true;
})";

        // The text of the element: the value of a field, otherwise its visible text.
        private const string TextScript = @"(function (sel, frame) {" + ResolveJs + @"
  var hit = resolve(sel, frame);
  if (!hit || hit.error) return null;
  var e = hit.el;
  var tag = e.tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return String(e.value);
  return String(e.innerText != null ? e.innerText : e.textContent).trim();
})";

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

        private readonly BrowserBridge _bridge;
        private readonly bool _ownsBridge;
        private int _tabId = -1;

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

        public bool Connected => _bridge.Connected;

        /// <summary>Runs one Web action.</summary>
        /// <returns>The text read by GetText; null for the other actions.</returns>
        public string Execute(WebSettings w, string text, CancellationToken token)
        {
            switch (w.Kind)
            {
                case WebKind.OpenBrowser: OpenBrowser(w, token); break;
                case WebKind.OpenUrl: OpenUrl(w, token); break;
                case WebKind.WaitElement: FindElement(w, token); break;
                case WebKind.Input: Input(w, text, token); break;
                case WebKind.Click: Click(w, token); break;
                case WebKind.Dialog: Dialog(w, token); break;
                case WebKind.GetText: return GetText(w, token);
                case WebKind.SwitchTab: SwitchTab(w, token); break;
            }
            return null;
        }

        private void OpenBrowser(WebSettings w, CancellationToken token)
        {
            if (_bridge.Connected) return;
            string exe = w.Browser == BrowserKind.Edge ? "msedge.exe" : "chrome.exe";
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                throw new InvalidOperationException("Cannot start " + w.Browser + ": " + ex.Message);
            }
            if (!_bridge.WaitConnected((int)Math.Min(w.TimeMs, int.MaxValue), token))
                throw new InvalidOperationException(w.Browser + " started, but its browser extension did not connect. Install the Automation Test browser extension in that browser first.");
        }

        private void EnsureConnected(CancellationToken token)
        {
            // A browser that has been idle puts the extension to sleep; it wakes within about 30 seconds and then connects.
            if (_bridge.WaitConnected(35000, token)) return;
            throw new InvalidOperationException("The browser is not connected. Add an Open Browser action first, or start the browser that has the Automation Test extension.");
        }

        private object Call(string cmd, Dictionary<string, object> args, CancellationToken token, int timeoutMs = CallTimeoutMs)
        {
            return _bridge.Call(cmd, args, timeoutMs, token);
        }

        private int CurrentTab(CancellationToken token)
        {
            EnsureConnected(token);
            if (_tabId >= 0)
            {
                var exists = (Dictionary<string, object>)Call("tabExists", new Dictionary<string, object> { { "tabId", _tabId } }, token);
                if (exists["exists"] is bool b && b) return _tabId;
            }
            var active = (Dictionary<string, object>)Call("activeTab", null, token);
            _tabId = Convert.ToInt32(active["tabId"]);
            return _tabId;
        }

        private void OpenUrl(WebSettings w, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(w.Url)) throw new InvalidOperationException("Open URL: no address is set.");
            EnsureConnected(token);
            var args = new Dictionary<string, object>
            {
                { "url", NormalizeUrl(w.Url) },
                { "newTab", w.NewTab },
                { "timeoutMs", w.TimeMs }
            };
            if (!w.NewTab && _tabId >= 0)
            {
                var exists = (Dictionary<string, object>)Call("tabExists", new Dictionary<string, object> { { "tabId", _tabId } }, token);
                if (exists["exists"] is bool b && b) args["tabId"] = _tabId;
            }
            var reply = (Dictionary<string, object>)Call("openUrl", args, token, (int)Math.Min(int.MaxValue, w.TimeMs + CallTimeoutMs));
            _tabId = Convert.ToInt32(reply["tabId"]);
        }

        private static string NormalizeUrl(string url)
        {
            url = url.Trim();
            return url.IndexOf("://", StringComparison.Ordinal) > 0 || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
        }

        private object Cdp(int tab, string method, Dictionary<string, object> parameters, CancellationToken token, int timeoutMs = CallTimeoutMs)
        {
            return Call("cdp", new Dictionary<string, object> { { "tabId", tab }, { "method", method }, { "params", parameters ?? new Dictionary<string, object>() } }, token, timeoutMs);
        }

        /// <summary>Waits until the element is on the page and visible; returns where its centre is (in page pixels).</summary>
        private double[] FindElement(WebSettings w, CancellationToken token, bool failIfMissing = true)
        {
            if (string.IsNullOrWhiteSpace(w.Selector)) throw new InvalidOperationException(w.Kind + ": no selector is set.");
            int tab = CurrentTab(token);
            string script = LocateScript + "(" + MiniJson.Write(w.Selector.Trim()) + "," + MiniJson.Write((w.Frame ?? "").Trim()) + ")";
            var watch = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var reply = (Dictionary<string, object>)Cdp(tab, "Runtime.evaluate",
                    new Dictionary<string, object> { { "expression", script }, { "returnByValue", true } }, token);
                if (reply.TryGetValue("exceptionDetails", out object ex) && ex != null)
                    throw new InvalidOperationException("The page could not be searched: " + MiniJson.Write(ex));
                var value = (Dictionary<string, object>)((Dictionary<string, object>)reply["result"])["value"];
                if (value.TryGetValue("error", out object error)) throw new InvalidOperationException(error.ToString());
                if (value["found"] is bool found && found && value["visible"] is bool visible && visible)
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
            int tab = CurrentTab(token);
            Cdp(tab, "Page.bringToFront", null, token);
            Cdp(tab, "Runtime.evaluate", new Dictionary<string, object>
            {
                { "expression", FocusScript + "(" + MiniJson.Write(w.Selector.Trim()) + "," + MiniJson.Write((w.Frame ?? "").Trim()) + ")" }, { "returnByValue", true }
            }, token);
            // insertText goes through the browser's own input path, so frameworks see the change and Vietnamese text is typed as is.
            if (string.IsNullOrEmpty(text)) Cdp(tab, "Input.dispatchKeyEvent", new Dictionary<string, object> { { "type", "rawKeyDown" }, { "key", "Delete" }, { "code", "Delete" }, { "windowsVirtualKeyCode", 46 } }, token);
            else Cdp(tab, "Input.insertText", new Dictionary<string, object> { { "text", text } }, token);
        }

        private void Click(WebSettings w, CancellationToken token)
        {
            var at = FindElement(w, token);
            int tab = CurrentTab(token);
            Cdp(tab, "Page.bringToFront", null, token);
            Thread.Sleep(100); // the viewport of a tab that was in the background needs a moment
            at = FindElement(w, token); // again: the position can change once the tab is in front
            Func<string, int, Dictionary<string, object>> mouse = (type, count) => new Dictionary<string, object>
            {
                { "type", type }, { "x", at[0] }, { "y", at[1] }, { "button", "left" }, { "clickCount", count }
            };
            Cdp(tab, "Input.dispatchMouseEvent", new Dictionary<string, object> { { "type", "mouseMoved" }, { "x", at[0] }, { "y", at[1] } }, token);
            Cdp(tab, "Input.dispatchMouseEvent", mouse("mousePressed", 1), token);
            try
            {
                Cdp(tab, "Input.dispatchMouseEvent", mouse("mouseReleased", 1), token, ClickReleaseTimeoutMs);
            }
            catch (BridgeTimeoutException)
            {
                // The click opened a JavaScript dialog; a Dialog action answers it.
            }
        }

        /// <summary>
        /// Lets the user point at an element in the current tab (it is brought to the front). Returns false when cancelled (Esc, the
        /// token, or the time ran out).
        /// </summary>
        public bool PickElement(CancellationToken token, int timeoutMs, out string selector, out string frame)
        {
            selector = ""; frame = "";
            int tab = CurrentTab(token);
            Call("activateTab", new Dictionary<string, object> { { "tabId", tab } }, token);
            Cdp(tab, "Runtime.evaluate", new Dictionary<string, object> { { "expression", PickScript.Install }, { "returnByValue", true } }, token);
            var watch = Stopwatch.StartNew();
            try
            {
                while (!token.IsCancellationRequested && watch.ElapsedMilliseconds < timeoutMs)
                {
                    var reply = (Dictionary<string, object>)Cdp(tab, "Runtime.evaluate", new Dictionary<string, object> { { "expression", PickScript.Read }, { "returnByValue", true } }, token);
                    var result = (Dictionary<string, object>)reply["result"];
                    if (result.TryGetValue("value", out object value) && value is string json)
                    {
                        var data = (Dictionary<string, object>)MiniJson.Parse(json);
                        if (data.ContainsKey("cancelled")) return false;
                        selector = Convert.ToString(data["selector"]);
                        frame = Convert.ToString(data["frame"]);
                        return true;
                    }
                    Thread.Sleep(150);
                }
                return false;
            }
            finally
            {
                try { Cdp(tab, "Runtime.evaluate", new Dictionary<string, object> { { "expression", PickScript.Cancel } }, CancellationToken.None, 3000); }
                catch (InvalidOperationException) { }
            }
        }

        private string GetText(WebSettings w, CancellationToken token)
        {
            if (FindElement(w, token, w.Required) == null) return ""; // optional element that is not there
            int tab = CurrentTab(token);
            var reply = (Dictionary<string, object>)Cdp(tab, "Runtime.evaluate", new Dictionary<string, object>
            {
                { "expression", TextScript + "(" + MiniJson.Write(w.Selector.Trim()) + "," + MiniJson.Write((w.Frame ?? "").Trim()) + ")" },
                { "returnByValue", true }
            }, token);
            var result = (Dictionary<string, object>)reply["result"];
            return result.TryGetValue("value", out object value) ? value as string : null;
        }

        private void SwitchTab(WebSettings w, CancellationToken token)
        {
            string want = (w.Text ?? "").Trim();
            if (want.Length == 0) throw new InvalidOperationException("Switch Tab: no title or address is set.");
            EnsureConnected(token);
            var watch = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                long best = -1;
                foreach (var item in (object[])Call("tabs", null, token))
                {
                    var tab = (Dictionary<string, object>)item;
                    string title = Convert.ToString(tab["title"]) ?? "";
                    string url = Convert.ToString(tab["url"]) ?? "";
                    if (title.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0 || url.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                        best = Math.Max(best, Convert.ToInt64(tab["id"])); // several matches: the newest tab
                }
                if (best >= 0)
                {
                    Call("activateTab", new Dictionary<string, object> { { "tabId", best } }, token);
                    _tabId = (int)best;
                    return;
                }
                if (watch.ElapsedMilliseconds >= w.TimeMs)
                    throw new InvalidOperationException("Switch Tab: no tab with \"" + want + "\" in its title or address within " + w.TimeMs + " ms.");
                Thread.Sleep(200);
            }
        }

        private void Dialog(WebSettings w, CancellationToken token)
        {
            int tab = CurrentTab(token);
            var watch = Stopwatch.StartNew();
            var parameters = new Dictionary<string, object> { { "accept", w.Accept } };
            if (w.Accept && !string.IsNullOrEmpty(w.Text)) parameters["promptText"] = w.Text;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    Cdp(tab, "Page.handleJavaScriptDialog", parameters, token);
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
