using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Automation.Core.Input;
using Automation.Core.Model;
using Automation.Core.Native;
using Automation.Core.Windows;

namespace Automation.Core.Runner
{
    /// <summary>Runs a script synchronously on the calling thread until it finishes or the token is cancelled.</summary>
    public sealed class ScriptRunner
    {
        private const int SettleAfterActivateMs = 120;

        private readonly MouseController _mouse;
        private readonly HashSet<MouseButton> _held = new HashSet<MouseButton>();
        private readonly Dictionary<string, IntPtr> _windowCache = new Dictionary<string, IntPtr>();

        public ScriptRunner(MouseController mouse)
        {
            _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        }

        /// <summary>Raised (from the runner thread) with the node that is about to run.</summary>
        public event Action<ActionNode> NodeStarted;

        /// <summary>Raised (from the runner thread) for notable events such as a skipped loop.</summary>
        public event Action<string> Message;

        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public void Run(AutomationScript script, CancellationToken token)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            _windowCache.Clear();
            // 1 ms timer resolution so short delays and the per-iteration yield are not rounded up to ~15 ms.
            NativeMethods.timeBeginPeriod(1);
            try
            {
                ExecuteList(script.Actions, new RunContext(), token);
            }
            finally
            {
                ReleaseHeldButtons();
                NativeMethods.timeEndPeriod(1);
            }
        }

        /// <summary>
        /// What child actions inherit from the node that contains them. Today only the window; later an Image node
        /// will push the place where the image was found so its children can be relative to it.
        /// </summary>
        private sealed class RunContext
        {
            public WindowTarget Window = null;
        }

        private void ExecuteList(List<ActionNode> nodes, RunContext context, CancellationToken token)
        {
            foreach (var node in nodes)
            {
                token.ThrowIfCancellationRequested();
                NodeStarted?.Invoke(node);
                Execute(node, context, token);
            }
        }

        private void Execute(ActionNode node, RunContext context, CancellationToken token)
        {
            switch (node.Category)
            {
                case ActionCategory.Mouse:
                    ExecuteMouse(node, context, token);
                    break;
                case ActionCategory.Control:
                    ExecuteLoop(node, context, token);
                    break;
            }
        }

        // ---------------------------------------------------------------- loop

        /// <summary>
        /// Duration and Until are checked between iterations, so the last iteration can overshoot the end by
        /// up to the length of one pass over the body.
        /// </summary>
        private void ExecuteLoop(ActionNode loop, RunContext context, CancellationToken token)
        {
            if (loop.Children.Count == 0) return;

            var settings = loop.Loop;
            switch (settings.Mode)
            {
                case LoopType.Count:
                    for (long i = 0; settings.Count == 0 || i < settings.Count; i++)
                        Iterate(loop, context, token);
                    break;

                case LoopType.Duration:
                    var watch = Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < settings.DurationMs)
                        Iterate(loop, context, token);
                    break;

                case LoopType.Until:
                    DateTime? until = settings.GetUntil();
                    if (until == null || until.Value <= DateTime.Now)
                    {
                        Message?.Invoke("Loop skipped: the end time is missing or already in the past.");
                        break;
                    }
                    while (DateTime.Now < until.Value)
                        Iterate(loop, context, token);
                    break;
            }
        }

        private void Iterate(ActionNode loop, RunContext context, CancellationToken token)
        {
            ExecuteList(loop.Children, context, token);
            Wait(0, token); // a loop whose body has no delay still yields and checks for a stop on every pass
        }

        // ---------------------------------------------------------------- mouse

        private void ExecuteMouse(ActionNode node, RunContext context, CancellationToken token)
        {
            var m = node.Mouse;
            int x = m.X, y = m.Y;
            WindowTarget target = !m.Process.IsEmpty ? m.Process : context.Window;
            if (target != null && !target.IsEmpty)
            {
                IntPtr hwnd = FindWindow(target);
                Activate(hwnd, token);
                var area = WindowLocator.GetClientArea(hwnd);
                WindowLocator.ToScreen(area, m.Anchor, m.X, m.Y, out x, out y);
                // Never act on whatever program happens to cover the point (activation is best effort).
                var under = WindowLocator.WindowAt(x, y);
                if (!under.HasValue || !WindowLocator.SameProcess(under.Value.Handle, hwnd))
                    throw new InvalidOperationException("The target window is covered by another window at (" + x + ", " + y + ").");
            }

            _mouse.MoveTo(x, y, ToInt(m.TimeMs), token);
            switch (m.Action)
            {
                case MouseAction.Click:
                    _mouse.Click(m.Button);
                    break;
                case MouseAction.MouseDown:
                    _mouse.Down(m.Button);
                    _held.Add(m.Button);
                    break;
                case MouseAction.MouseUp:
                    _mouse.Up(m.Button);
                    _held.Remove(m.Button);
                    break;
                case MouseAction.ScrollUp:
                    _mouse.Scroll(Math.Max(1, m.ScrollNotches));
                    break;
                case MouseAction.ScrollDown:
                    _mouse.Scroll(-Math.Max(1, m.ScrollNotches));
                    break;
            }
            Wait(m.DelayMs, token);
        }

        /// <summary>Returns the cached window for the target while it is still valid, otherwise searches again.</summary>
        private IntPtr FindWindow(WindowTarget target)
        {
            string key = (target.ProcessName ?? "") + "|" + (target.TitlePattern ?? "");
            if (_windowCache.TryGetValue(key, out IntPtr cached) && WindowLocator.Matches(cached, target))
                return cached;

            IntPtr hwnd = WindowLocator.Find(target);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Window not found: " + Describe(target));
            _windowCache[key] = hwnd;
            return hwnd;
        }

        /// <summary>
        /// Restores and activates the window unless it is already in front, so clicks land on it and not on
        /// whatever is covering it. Activation is best effort (Windows may refuse it).
        /// </summary>
        private static void Activate(IntPtr hwnd, CancellationToken token)
        {
            if (WindowLocator.IsActive(hwnd) && !WindowLocator.IsMinimized(hwnd)) return;
            WindowLocator.BringToFront(hwnd);
            Wait(SettleAfterActivateMs, token);
            if (WindowLocator.IsMinimized(hwnd))
                throw new InvalidOperationException("The target window is minimized and could not be restored.");
        }

        private static string Describe(WindowTarget target)
        {
            return "process \"" + target.ProcessName + "\", title \"" + target.TitlePattern + "\"";
        }

        /// <summary>Releases any button left pressed by a MouseDown action when the run stops early.</summary>
        private void ReleaseHeldButtons()
        {
            foreach (var button in _held)
            {
                try { _mouse.Up(button); }
                catch (InvalidOperationException) { }
            }
            _held.Clear();
        }

        private static int ToInt(long value)
        {
            return (int)Math.Max(0, Math.Min(int.MaxValue, value));
        }

        private static void Wait(long ms, CancellationToken token)
        {
            if (ms < 20)
            {
                // Timer granularity can be ~15 ms (Windows ignores timeBeginPeriod for a minimized app), so short
                // waits are spun with a yield instead of slept; this keeps fast loops fast and still stoppable.
                var watch = Stopwatch.StartNew();
                do
                {
                    token.ThrowIfCancellationRequested();
                    Thread.Sleep(0);
                } while (watch.ElapsedMilliseconds < ms);
                return;
            }
            // WaitOne takes an int; split very long waits into chunks.
            while (ms > 0 && !token.IsCancellationRequested)
            {
                int chunk = (int)Math.Min(ms, int.MaxValue - 1);
                token.WaitHandle.WaitOne(chunk);
                ms -= chunk;
            }
            token.ThrowIfCancellationRequested();
        }
    }
}
