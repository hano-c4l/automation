using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Imaging;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Native;
using AutomationTest.Core.Windows;

namespace AutomationTest.Core.Runner
{
    /// <summary>Runs a script synchronously on the calling thread until it finishes or the token is cancelled.</summary>
    public sealed class ScriptRunner
    {
        private const int SettleAfterActivateMs = 120;

        private readonly MouseController _mouse;
        private readonly HashSet<MouseButton> _held = new HashSet<MouseButton>();
        private readonly Dictionary<string, IntPtr> _windowCache = new Dictionary<string, IntPtr>();

        private readonly VariableStore _vars = new VariableStore();
        private readonly KeyboardController _keyboard;
        private readonly HashSet<int> _heldKeys = new HashSet<int>();

        public ScriptRunner(MouseController mouse, KeyboardController keyboard = null)
        {
            _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
            _keyboard = keyboard ?? new KeyboardController();
        }

        /// <summary>Raised (from the runner thread) with the node that is about to run.</summary>
        public event Action<ActionNode> NodeStarted;

        /// <summary>Raised (from the runner thread) for notable events such as a skipped loop.</summary>
        public event Action<string> Message;

        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public void Run(AutomationScript script, CancellationToken token)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            Run(script.Actions, null, token);
        }

        /// <summary>Runs just these actions (for trying part of a script), starting with the given variable values.</summary>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public void Run(List<ActionNode> actions, IDictionary<string, object> variables, CancellationToken token)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            _windowCache.Clear();
            _vars.Clear();
            if (variables != null)
                foreach (var pair in variables) _vars.Set(pair.Key, pair.Value);
            // 1 ms timer resolution so short delays and the per-iteration yield are not rounded up to ~15 ms.
            NativeMethods.timeBeginPeriod(1);
            try
            {
                ExecuteList(actions, token);
            }
            finally
            {
                ReleaseHeldButtons();
                ReleaseHeldKeys();
                NativeMethods.timeEndPeriod(1);
            }
        }

        private void ExecuteList(List<ActionNode> nodes, CancellationToken token)
        {
            foreach (var node in nodes)
            {
                token.ThrowIfCancellationRequested();
                NodeStarted?.Invoke(node);
                Execute(node, token);
            }
        }

        private void Execute(ActionNode node, CancellationToken token)
        {
            switch (node.Category)
            {
                case ActionCategory.Mouse:
                    ExecuteMouse(node, token);
                    break;
                case ActionCategory.Keyboard:
                    ExecuteKeyboard(node, token);
                    break;
                case ActionCategory.Command:
                    ExecuteCommand(node);
                    break;
                case ActionCategory.Control:
                    switch (node.Control)
                    {
                        case ControlKind.Wait: ExecuteWait(node, token); break;
                        case ControlKind.Image: ExecuteImage(node, token); break;
                        case ControlKind.Variable: ExecuteVariable(node); break;
                        case ControlKind.Condition: ExecuteCondition(node, token); break;
                        case ControlKind.Case:
                        case ControlKind.Else:
                            throw new InvalidOperationException("A Case or Else can only be inside a Condition.");
                        default: ExecuteLoop(node, token); break;
                    }
                    break;
            }
        }

        // ---------------------------------------------------------------- keyboard and command

        private void ExecuteKeyboard(ActionNode node, CancellationToken token)
        {
            var k = node.Keyboard;
            int time = ToInt(k.TimeMs);
            try
            {
                switch (k.Action)
                {
                    case KeyboardAction.KeyPress:
                        _keyboard.Press(KeyCodes.ParseKey(k.Value), time, token);
                        break;
                    case KeyboardAction.KeyDown:
                        int down = KeyCodes.ParseKey(k.Value);
                        _keyboard.Down(down);
                        _heldKeys.Add(down);
                        break;
                    case KeyboardAction.KeyUp:
                        int up = KeyCodes.ParseKey(k.Value);
                        _keyboard.Up(up);
                        _heldKeys.Remove(up);
                        break;
                    case KeyboardAction.ComboKey:
                        _keyboard.Combo(KeyCodes.ParseCombo(k.Value), time, token);
                        break;
                    case KeyboardAction.Text:
                        _keyboard.TypeText(k.ValueIsExpression ? EvalString(k.Value) : k.Value, time, token);
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(ex.Message);
            }
            Wait(k.DelayMs, token);
        }

        private static void ExecuteCommand(ActionNode node)
        {
            var c = node.Command;
            if (string.IsNullOrWhiteSpace(c.Value))
                throw new InvalidOperationException("Open: no path is set.");
            try
            {
                Process.Start(new ProcessStartInfo(c.Value.Trim()) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                throw new InvalidOperationException("Cannot open \"" + c.Value.Trim() + "\": " + ex.Message);
            }
        }

        /// <summary>Releases keys left pressed by a KeyDown action when the run stops early.</summary>
        private void ReleaseHeldKeys()
        {
            foreach (int key in _heldKeys)
            {
                try { _keyboard.Up(key); }
                catch (InvalidOperationException) { }
            }
            _heldKeys.Clear();
        }

        // ---------------------------------------------------------------- image

        /// <summary>
        /// Looks for the picture. With an ID the result is stored in that variable (Found, and when found Position and
        /// Size). When found the children run; when not found they are skipped and the run continues.
        /// </summary>
        private void ExecuteImage(ActionNode node, CancellationToken token)
        {
            var im = node.Image;
            if (!im.HasImage) throw new InvalidOperationException("Image: no picture is set.");
            bool hasId = !string.IsNullOrWhiteSpace(im.Id);
            if (hasId && !Expression.IsValidId(im.Id.Trim()))
                throw new InvalidOperationException("Image: \"" + im.Id + "\" is not a valid ID.");

            LocateResult found = ImageLocator.Locate(im, im.Process, token);
            if (hasId) _vars.Set(im.Id.Trim(), ToImageValue(im, found));
            if (!found.Found)
            {
                string best = found.Score >= 0 ? " (best match " + (int)Math.Round(found.Score * 100) + "%, needs " + im.Accuracy + "%)" : "";
                Message?.Invoke("Image not found" + best + ".");
                return;
            }
            ExecuteList(node.Children, token);
        }

        /// <summary>
        /// With a Process, Position is the picture's anchor corner (centre for Center) measured from the same anchor point
        /// of the window that the range uses, so a Mouse action with that Process and Anchor lands on it. Without a
        /// Process it is the picture's top-left corner on the desktop.
        /// </summary>
        private static ImageValue ToImageValue(ImageSettings im, LocateResult found)
        {
            if (!found.Found) return new ImageValue(false, default(PointValue), default(SizeValue));

            PointValue position;
            if (found.Window.HasValue)
            {
                WindowLocator.ToScreen(found.Rect, im.Anchor, 0, 0, out int px, out int py);
                WindowLocator.ToScreen(found.Window.Value, im.Anchor, 0, 0, out int wx, out int wy);
                position = new PointValue(px - wx, py - wy);
            }
            else position = new PointValue(found.Rect.Left, found.Rect.Top);
            return new ImageValue(true, position, new SizeValue(found.Rect.Width, found.Rect.Height));
        }

        // ---------------------------------------------------------------- variable and condition

        private void ExecuteVariable(ActionNode node)
        {
            var v = node.Variable;
            string id = (v.Id ?? "").Trim();
            if (!Expression.IsValidId(id))
                throw new InvalidOperationException("Variable: \"" + v.Id + "\" is not a valid ID (letters, digits and _, not starting with a digit).");
            try
            {
                object value = v.UseExpression ? Expression.Evaluate(v.Expression, _vars) : v.GetLiteral();
                _vars.Set(id, Coercion.To(v.Type, value));
            }
            catch (ExpressionException ex)
            {
                throw new InvalidOperationException("Variable {" + id + "}: " + ex.Message);
            }
        }

        /// <summary>Runs the first Case whose expression is true; if none is, runs the Else.</summary>
        private void ExecuteCondition(ActionNode node, CancellationToken token)
        {
            foreach (var branch in node.Children)
            {
                token.ThrowIfCancellationRequested();
                if (branch.IsKind(ControlKind.Case))
                {
                    bool matched;
                    try { matched = Expression.EvaluateBool(branch.Case.Expression, _vars); }
                    catch (ExpressionException ex) { throw new InvalidOperationException("Condition " + (string.IsNullOrWhiteSpace(branch.Name) ? "" : branch.Name + " ") + "(" + branch.Case.Expression.Trim() + "): " + ex.Message); }
                    if (matched)
                    {
                        ExecuteList(branch.Children, token);
                        return;
                    }
                }
                else if (branch.IsKind(ControlKind.Else))
                {
                    ExecuteList(branch.Children, token);
                    return;
                }
            }
        }

        private string EvalString(string text)
        {
            try { return Expression.EvaluateString(text, _vars); }
            catch (ExpressionException ex) { throw new InvalidOperationException(ex.Message); }
        }

        // ---------------------------------------------------------------- wait

        /// <summary>
        /// Waits until a window matching the process appears and is in front, then runs the children. Each attempt
        /// lasts TimeMs; after Retry more attempts the run stops with an error.
        /// </summary>
        private void ExecuteWait(ActionNode node, CancellationToken token)
        {
            var w = node.Wait;
            if (w.Process.IsEmpty)
                throw new InvalidOperationException("Wait: choose a process or a title first.");

            int attempts = 1 + Math.Max(0, w.Retry);
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (attempts > 1) Message?.Invoke("Waiting for " + Describe(w.Process) + " (attempt " + attempt + " / " + attempts + ")");
                var watch = Stopwatch.StartNew();
                do
                {
                    token.ThrowIfCancellationRequested();
                    IntPtr hwnd = WindowLocator.Find(w.Process);
                    if (hwnd != IntPtr.Zero && TryActivate(hwnd, token))
                    {
                        ExecuteList(node.Children, token);
                        return;
                    }
                    Wait(200, token);
                } while (watch.ElapsedMilliseconds < w.TimeMs);
            }
            throw new InvalidOperationException("Timed out waiting for " + Describe(w.Process) + ".");
        }

        // ---------------------------------------------------------------- loop

        /// <summary>
        /// Duration and Until are checked between iterations, so the last iteration can overshoot the end by
        /// up to the length of one pass over the body.
        /// </summary>
        private void ExecuteLoop(ActionNode loop, CancellationToken token)
        {
            if (loop.Children.Count == 0) return;

            var settings = loop.Loop;
            switch (settings.Mode)
            {
                case LoopType.Times:
                    for (long i = 0; settings.Count == 0 || i < settings.Count; i++)
                        Iterate(loop, token);
                    break;

                case LoopType.Duration:
                    var watch = Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < settings.DurationMs)
                        Iterate(loop, token);
                    break;

                case LoopType.Until:
                    DateTime? until = settings.GetUntil();
                    if (until == null || until.Value <= DateTime.Now)
                    {
                        Message?.Invoke("Loop skipped: the end time is missing or already in the past.");
                        break;
                    }
                    while (DateTime.Now < until.Value)
                        Iterate(loop, token);
                    break;
            }
        }

        private void Iterate(ActionNode loop, CancellationToken token)
        {
            ExecuteList(loop.Children, token);
            Wait(0, token); // a loop whose body has no delay still yields and checks for a stop on every pass
        }

        // ---------------------------------------------------------------- mouse

        private void ExecuteMouse(ActionNode node, CancellationToken token)
        {
            var m = node.Mouse;
            int x = m.X, y = m.Y;
            if (m.UseVariable)
            {
                try
                {
                    var point = Expression.EvaluatePoint(m.PositionExpression, _vars);
                    x = (int)Math.Round(point.X);
                    y = (int)Math.Round(point.Y);
                }
                catch (ExpressionException ex) { throw new InvalidOperationException("Mouse position: " + ex.Message); }
            }

            if (!m.Process.IsEmpty)
            {
                IntPtr hwnd = FindWindow(m.Process);
                Activate(hwnd, token);
                var area = WindowLocator.GetClientArea(hwnd);
                WindowLocator.ToScreen(area, m.Anchor, x, y, out x, out y);
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

        /// <summary>Like Activate, but reports whether the window is now in front instead of failing.</summary>
        private static bool TryActivate(IntPtr hwnd, CancellationToken token)
        {
            if (!(WindowLocator.IsActive(hwnd) && !WindowLocator.IsMinimized(hwnd)))
            {
                WindowLocator.BringToFront(hwnd);
                Wait(SettleAfterActivateMs, token);
            }
            return WindowLocator.IsActive(hwnd) && !WindowLocator.IsMinimized(hwnd);
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
