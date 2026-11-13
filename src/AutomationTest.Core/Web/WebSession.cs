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

        // find(sel): the element for a CSS selector, or for "text=Label" the innermost visible-text match.
        private const string FindJs = @"function find(sel) {
  if (sel.indexOf('text=') === 0) {
    var want = sel.slice(5).trim().toLowerCase();
    var hits = Array.prototype.slice.call(document.querySelectorAll('body *')).filter(function (e) {
      var t = (e.innerText || e.value || '').trim().toLowerCase();
      return t.length > 0 && t.indexOf(want) >= 0;
    });
    return hits.filter(function (e) { return !hits.some(function (o) { return o !== e && e.contains(o); }); })[0] || null;
  }
  return document.querySelector(sel);
}";

        // Reports where the element is; the selector is passed as a JSON string.
        private const string LocateScript = @"(function (sel) {" + FindJs + @"
  var el;
  try { el = find(sel); } catch (err) { return { error: 'Bad selector: ' + err.message }; }
  if (!el) return { found: false };
  el.scrollIntoView({ block: 'center', inline: 'center' });
  var r = el.getBoundingClientRect();
  var visible = r.width > 0 && r.height > 0 && getComputedStyle(el).visibility !== 'hidden';
  return { found: true, visible: visible, x: r.left + r.width / 2, y: r.top + r.height / 2 };
})";

        // Focuses the element and selects its content, so typed text replaces it.
        private const string FocusScript = @"(function (sel) {" + FindJs + @"
  var e = find(sel);
  if (!e) return false;
  e.focus();
  if (e.select) e.select(); else document.execCommand('selectAll');
  return true;
})";

        private static readonly Lazy<WebSession> SharedSession = new Lazy<WebSession>(() => new WebSession());

        /// <summary>The one session of the program (one extension connection, one listening port).</summary>
        public static WebSession Shared => SharedSession.Value;

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
        public void Execute(WebSettings w, string text, CancellationToken token)
        {
            switch (w.Kind)
            {
                case WebKind.OpenBrowser: OpenBrowser(w, token); break;
                case WebKind.OpenUrl: OpenUrl(w, token); break;
                case WebKind.WaitElement: FindElement(w, token); break;
                case WebKind.Input: Input(w, text, token); break;
                case WebKind.Click: Click(w, token); break;
                case WebKind.Dialog: Dialog(w, token); break;
            }
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
            if (_bridge.WaitConnected(1500, token)) return;
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
        private double[] FindElement(WebSettings w, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(w.Selector)) throw new InvalidOperationException(w.Kind + ": no selector is set.");
            int tab = CurrentTab(token);
            string script = LocateScript + "(" + MiniJson.Write(w.Selector.Trim()) + ")";
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
                    throw new InvalidOperationException(w.Kind + ": \"" + w.Selector.Trim() + "\" was not found on the page within " + w.TimeMs + " ms.");
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
                { "expression", FocusScript + "(" + MiniJson.Write(w.Selector.Trim()) + ")" }, { "returnByValue", true }
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
