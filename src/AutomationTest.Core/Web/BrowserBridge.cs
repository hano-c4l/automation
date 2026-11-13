using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationTest.Core.Web
{
    /// <summary>The browser did not answer a command in time (a page may be blocked by a JavaScript dialog).</summary>
    public sealed class BridgeTimeoutException : InvalidOperationException
    {
        public BridgeTimeoutException(string message) : base(message) { }
    }

    /// <summary>
    /// Local WebSocket server the browser extension connects to (127.0.0.1 only). The app sends commands (open a tab, or any
    /// Chrome DevTools Protocol call on a tab) and gets the extension's reply. Only a browser extension may connect: web pages
    /// always send their own http(s) Origin, which is rejected.
    /// </summary>
    public sealed class BrowserBridge : IDisposable
    {
        public const int DefaultPort = 47615;

        private readonly HttpListener _listener = new HttpListener();
        private readonly ConcurrentDictionary<long, TaskCompletionSource<Dictionary<string, object>>> _pending =
            new ConcurrentDictionary<long, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private WebSocket _socket;
        private long _nextId;

        public BrowserBridge(int port = DefaultPort)
        {
            Port = port;
            _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        }

        public int Port { get; }

        /// <summary>True while the extension is connected.</summary>
        public bool Connected
        {
            get { var s = _socket; return s != null && s.State == WebSocketState.Open; }
        }

        /// <summary>Raised for events the extension forwards (for example a CDP event of a tab).</summary>
        public event Action<Dictionary<string, object>> EventReceived;

        public void Start()
        {
            _listener.Start();
            Task.Run(AcceptLoop);
        }

        /// <summary>Waits until the extension has connected.</summary>
        public bool WaitConnected(int timeoutMs, CancellationToken token)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!Connected)
            {
                token.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= until) return false;
                Thread.Sleep(50);
            }
            return true;
        }

        /// <summary>Sends one command and returns the "result" of the reply.</summary>
        /// <param name="cmd">"openUrl", "tabs", or "cdp".</param>
        /// <param name="args">Command arguments (a JSON-serializable dictionary).</param>
        /// <exception cref="InvalidOperationException">Not connected, timed out or the browser reported an error.</exception>
        public object Call(string cmd, Dictionary<string, object> args, int timeoutMs, CancellationToken token)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
                throw new InvalidOperationException("The browser extension is not connected.");
            long id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<Dictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            try
            {
                var message = new Dictionary<string, object> { { "id", id }, { "cmd", cmd }, { "args", args ?? new Dictionary<string, object>() } };
                Send(socket, ToJson(message));
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

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) { return; }

                string origin = context.Request.Headers["Origin"] ?? "";
                bool extension = origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase);
                if (!context.Request.IsWebSocketRequest || !extension)
                {
                    context.Response.StatusCode = 403;
                    context.Response.Close();
                    continue;
                }
                try
                {
                    var ws = (await context.AcceptWebSocketAsync(null).ConfigureAwait(false)).WebSocket;
                    var old = _socket;
                    _socket = ws; // a reconnecting extension replaces the old connection
                    old?.Abort();
                    _ = Task.Run(() => ReceiveLoop(ws));
                }
                catch (Exception)
                {
                    context.Response.Abort();
                }
            }
        }

        private async Task ReceiveLoop(WebSocket ws)
        {
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

                    var message = FromJson(Encoding.UTF8.GetString(ms.ToArray()));
                    if (message == null) continue;
                    if (message.TryGetValue("id", out object idValue) && idValue != null)
                    {
                        long id = Convert.ToInt64(idValue);
                        if (_pending.TryGetValue(id, out var tcs)) tcs.TrySetResult(message);
                    }
                    else if (message.ContainsKey("event"))
                    {
                        EventReceived?.Invoke(message);
                    }
                }
            }
            catch (Exception)
            {
                // connection lost: the extension reconnects by itself
            }
            finally
            {
                if (ReferenceEquals(_socket, ws)) _socket = null;
                foreach (var pair in _pending) pair.Value.TrySetResult(new Dictionary<string, object> { { "error", "The browser connection was lost." } });
            }
        }

        private static string ToJson(Dictionary<string, object> value)
        {
            return MiniJson.Write(value);
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
            _socket?.Abort();
        }
    }
}
