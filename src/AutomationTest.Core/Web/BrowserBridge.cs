using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutomationTest.Core.Model;

namespace AutomationTest.Core.Web
{
    /// <summary>The browser did not answer a command in time (a page may be blocked by a JavaScript dialog).</summary>
    public sealed class BridgeTimeoutException : InvalidOperationException
    {
        public BridgeTimeoutException(string message) : base(message) { }
    }

    /// <summary>
    /// Local WebSocket server the browser extensions connect to (127.0.0.1 only). Chrome and Edge can be connected at the same time,
    /// one connection each (the extension names its browser in the address: ?browser=edge). The program sends commands (open a tab,
    /// or any Chrome DevTools Protocol call on a tab) and gets the extension's reply. Only a browser extension may connect: web pages
    /// always send their own http(s) Origin, which is rejected.
    /// </summary>
    public sealed class BrowserBridge : IDisposable
    {
        public const int DefaultPort = 47615;

        // The extension sends a message every 20 seconds; a connection that has been silent much longer is dead.
        private const int StaleMs = 60000;

        private sealed class Connection
        {
            public WebSocket Socket;
            public long LastReceivedTicks = Environment.TickCount;
        }

        private sealed class Pending
        {
            public BrowserKind Browser;
            public TaskCompletionSource<Dictionary<string, object>> Source;
        }

        private readonly HttpListener _listener = new HttpListener();
        private readonly ConcurrentDictionary<long, Pending> _pending = new ConcurrentDictionary<long, Pending>();
        private readonly ConcurrentDictionary<BrowserKind, Connection> _connections = new ConcurrentDictionary<BrowserKind, Connection>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private long _nextId;

        public BrowserBridge(int port = DefaultPort)
        {
            Port = port;
            _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        }

        public int Port { get; }

        /// <summary>The reason the last connection was turned away (for diagnosis); empty when none was.</summary>
        public string LastRejection { get; private set; } = "";

        /// <summary>True while the extension of that browser is connected.</summary>
        public bool IsConnected(BrowserKind browser)
        {
            return _connections.TryGetValue(browser, out var c) && c.Socket.State == WebSocketState.Open;
        }

        /// <summary>Raised for events the extension forwards (for example a CDP event of a tab).</summary>
        public event Action<BrowserKind, Dictionary<string, object>> EventReceived;

        public void Start()
        {
            _listener.Start();
            Task.Run(AcceptLoop);
        }

        /// <summary>Waits until the extension of that browser has connected.</summary>
        public bool WaitConnected(BrowserKind browser, int timeoutMs, CancellationToken token)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!IsConnected(browser))
            {
                token.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= until) return false;
                Thread.Sleep(50);
            }
            return true;
        }

        /// <summary>Sends one command and returns the "result" of the reply.</summary>
        /// <param name="cmd">The name of an extension command, for example "cdp".</param>
        /// <param name="args">Command arguments (a JSON-serializable dictionary).</param>
        /// <exception cref="InvalidOperationException">Not connected, timed out or the browser reported an error.</exception>
        public object Call(BrowserKind browser, string cmd, Dictionary<string, object> args, int timeoutMs, CancellationToken token)
        {
            if (!_connections.TryGetValue(browser, out var connection) || connection.Socket.State != WebSocketState.Open)
                throw new InvalidOperationException("The " + browser + " extension is not connected.");
            long id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<Dictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = new Pending { Browser = browser, Source = tcs };
            try
            {
                var message = new Dictionary<string, object> { { "id", id }, { "cmd", cmd }, { "args", args ?? new Dictionary<string, object>() } };
                Send(connection.Socket, MiniJson.Write(message));
                var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (!tcs.Task.IsCompleted)
                {
                    token.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow >= until) throw new BridgeTimeoutException("The browser did not answer in time (" + cmd + ").");
                    tcs.Task.Wait(50);
                }
                var reply = tcs.Task.Result;
                if (reply.TryGetValue("error", out object error) && error != null)
                    throw new InvalidOperationException("Browser error: " + error);
                reply.TryGetValue("result", out object result);
                return result;
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private void Send(WebSocket socket, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            _sendLock.Wait();
            try { socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _stop.Token).GetAwaiter().GetResult(); }
            finally { _sendLock.Release(); }
        }

        private void Reject(HttpListenerContext context, string reason)
        {
            LastRejection = reason;
            try
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
            }
            catch (Exception) { }
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) { return; }

                string origin = context.Request.Headers["Origin"] ?? "";
                bool extension = origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
                    || origin.StartsWith("extension://", StringComparison.OrdinalIgnoreCase);
                if (!context.Request.IsWebSocketRequest || !extension)
                {
                    Reject(context, "not an extension (Origin \"" + origin + "\")");
                    continue;
                }
                var browser = string.Equals(context.Request.QueryString["browser"], "edge", StringComparison.OrdinalIgnoreCase) ? BrowserKind.Edge : BrowserKind.Chrome;
                // While the extension of that browser is connected and alive, no other one may take over (two profiles, another extension).
                if (_connections.TryGetValue(browser, out var current) && current.Socket.State == WebSocketState.Open
                    && unchecked(Environment.TickCount - Interlocked.Read(ref current.LastReceivedTicks)) < StaleMs)
                {
                    Reject(context, browser + " is already connected");
                    continue;
                }
                try
                {
                    var ws = (await context.AcceptWebSocketAsync(null).ConfigureAwait(false)).WebSocket;
                    var connection = new Connection { Socket = ws };
                    if (_connections.TryGetValue(browser, out var old)) old.Socket.Abort();
                    _connections[browser] = connection;
                    _ = Task.Run(() => ReceiveLoop(browser, connection));
                }
                catch (Exception ex)
                {
                    LastRejection = "websocket handshake failed: " + ex.Message;
                    context.Response.Abort();
                }
            }
        }

        private async Task ReceiveLoop(BrowserKind browser, Connection connection)
        {
            var ws = connection.Socket;
            var buffer = new byte[64 * 1024];
            try
            {
                while (ws.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                {
                    var ms = new MemoryStream();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buffer, 0, r.Count);
                    } while (!r.EndOfMessage);

                    Interlocked.Exchange(ref connection.LastReceivedTicks, Environment.TickCount);
                    var message = FromJson(Encoding.UTF8.GetString(ms.ToArray()));
                    if (message == null) continue;
                    if (message.TryGetValue("id", out object idValue) && idValue != null)
                    {
                        long id = Convert.ToInt64(idValue);
                        if (_pending.TryGetValue(id, out var pending)) pending.Source.TrySetResult(message);
                    }
                    else if (message.ContainsKey("event"))
                    {
                        EventReceived?.Invoke(browser, message);
                    }
                }
            }
            catch (Exception)
            {
                // connection lost: the extension reconnects by itself
            }
            finally
            {
                if (_connections.TryGetValue(browser, out var now) && ReferenceEquals(now, connection)) _connections.TryRemove(browser, out _);
                foreach (var pair in _pending)
                    if (pair.Value.Browser == browser)
                        pair.Value.Source.TrySetResult(new Dictionary<string, object> { { "error", "The browser connection was lost." } });
            }
        }

        private static Dictionary<string, object> FromJson(string text)
        {
            try { return MiniJson.Parse(text) as Dictionary<string, object>; }
            catch (FormatException) { return null; }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            foreach (var c in _connections.Values) c.Socket.Abort();
        }
    }
}
