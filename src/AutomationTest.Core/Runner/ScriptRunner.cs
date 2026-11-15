using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AutomationTest.Core.Excel;
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

        /// <summary>Where the run goes next after an action: on (None), out of the nearest Loop (Break), to its next pass (Continue) or to the end (Return).</summary>
        private enum Flow { None, Break, Continue, Return }

        /// <summary>
        /// Random Time: every Time and Delay of an action (not Timeouts, Retry, Wait, the Shutdown countdown or the length of a Loop)
        /// is made 10 to 20 percent longer, chosen again each time the action runs. Never shorter.
        /// </summary>
        public bool RandomTime { get; set; }

        internal Random RandomSource = new Random();

        /// <summary>Gives the text in the clipboard (set by the program, which knows how to reach the clipboard; null when it cannot).</summary>
        public Func<string> ClipboardReader { get; set; }

        /// <summary>
        /// Asks the user for the Password variable with that ID (set by the program). Returns the value, or null when the user cancels,
        /// which stops the run. It is asked once for each ID in a run.
        /// </summary>
        public Func<string, CancellationToken, string> PasswordPrompt { get; set; }

        /// <summary>A secret value has become known (a password): the program hides it in the log, the status and the notice.</summary>
        public event Action<string> SecretKnown;

        private readonly Dictionary<string, string> _passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The time with the random extra added when Random Time is on (0 stays 0).</summary>
        internal long Jitter(long ms)
        {
            if (!RandomTime || ms <= 0) return ms;
            return ms + (long)Math.Round(ms * (0.10 + RandomSource.NextDouble() * 0.10));
        }

        private Flow _flow;
        private int _loopDepth; // how many Loops the running action is inside of

        /// <summary>The variables of the run (read by tests).</summary>
        internal VariableStore Variables => _vars;
        private readonly KeyboardController _keyboard;
        private readonly HashSet<int> _heldKeys = new HashSet<int>();

        public ScriptRunner(MouseController mouse, KeyboardController keyboard = null)
        {
            _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
            _keyboard = keyboard ?? new KeyboardController();
        }

        /// <summary>The Excel the Excel actions drive; the program-wide session when not set.</summary>
        public ExcelSession ExcelSession { get; set; }

        /// <summary>The browser the Web actions drive; the program-wide session when not set.</summary>
        public AutomationTest.Core.Web.WebSession WebSession { get; set; }

        /// <summary>Performs the Shutdown action; replaceable so tests never shut the computer down.</summary>
        public ISystemShutdown Shutdown { get; set; } = new WindowsShutdown();

        /// <summary>Raised (from the runner thread) with the node that is about to run.</summary>
        public event Action<ActionNode> NodeStarted;

        /// <summary>Raised (from the runner thread) for notable events such as a skipped loop.</summary>
        public event Action<string> Message;

        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public void Run(AutomationScript script, CancellationToken token)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            BrowserInheritance.Propagate(script.Actions);
            Run(script.Actions, null, token);
        }

        /// <summary>
        /// Runs just these actions (for trying part of a script), starting with the given variable values. With
        /// <paramref name="partial"/> (Start Selected) the actions may be the inside of a Loop, so a Break or Continue that is reached
        /// simply ends the selection instead of being an error.
        /// </summary>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public void Run(List<ActionNode> actions, IDictionary<string, object> variables, CancellationToken token, bool partial = false)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            _windowCache.Clear();
            _vars.Clear();
            _flow = Flow.None;
            _loopDepth = partial ? 1 : 0;
            _passwords.Clear();
            (WebSession ?? AutomationTest.Core.Web.WebSession.SharedIfCreated)?.ResetTab(); // every run starts on the tab that is in front
            (ExcelSession ?? AutomationTest.Core.Excel.ExcelSession.SharedIfCreated)?.Reset(); // and on the workbook and sheet that are active
            if (variables != null)
                foreach (var pair in variables) _vars.Set(pair.Key, pair.Value);
            // 1 ms timer resolution so short delays and the per-iteration yield are not rounded up to ~15 ms.
            NativeMethods.timeBeginPeriod(1);
            try
            {
                ExecuteList(actions, token);
                if (partial && (_flow == Flow.Break || _flow == Flow.Continue))
                    Message?.Invoke((_flow == Flow.Break ? "Break" : "Continue") + " reached: the selection ends here (it would act on the Loop around it).");
            }
            catch (ShutdownIssuedException)
            {
                // The computer is shutting down: the run ends here so nothing else is clicked or typed and a Loop cannot ask again.
            }
            finally
            {
                ReleaseHeldButtons();
                ReleaseHeldKeys();
                NativeMethods.timeEndPeriod(1);
                (ExcelSession ?? AutomationTest.Core.Excel.ExcelSession.SharedIfCreated)?.EndRun(); // Excel started hidden by this run is quit
            }
        }

        private void ExecuteList(List<ActionNode> nodes, CancellationToken token)
        {
            foreach (var node in nodes)
            {
                token.ThrowIfCancellationRequested();
                if (node.Disabled) continue; // a disabled action is skipped with everything inside it
                NodeStarted?.Invoke(node);
                Execute(node, token);
                if (_flow != Flow.None) return; // Break, Continue or Return: the rest of this list is not run
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
                case ActionCategory.Utilities:
                case ActionCategory.Program:
                    ExecuteCommand(node, token);
                    break;
                case ActionCategory.Web:
                    ExecuteWeb(node, token);
                    break;
                case ActionCategory.Excel:
                    ExecuteExcel(node, token);
                    break;
                case ActionCategory.Control:
                    switch (node.Control)
                    {
                        case ControlKind.Wait: ExecuteWait(node, token); break;
                        case ControlKind.Image: ExecuteImage(node, token); break;
                        case ControlKind.Variable: ExecuteVariable(node, token); break;
                        case ControlKind.Condition: ExecuteCondition(node, token); break;
                        case ControlKind.Case:
                        case ControlKind.Else:
                            throw new InvalidOperationException("A Case or Else can only be inside a Condition.");
                        case ControlKind.Catch:
                            throw new InvalidOperationException("A Catch can only be the last line of a Try.");
                        case ControlKind.Try: ExecuteTry(node, token); break;
                        case ControlKind.Section: ExecuteList(node.Children, token); break;
                        case ControlKind.Break:
                        case ControlKind.Continue:
                            if (_loopDepth == 0) throw new InvalidOperationException((node.Control == ControlKind.Break ? "Break" : "Continue") + " can only be used inside a Loop.");
                            _flow = node.Control == ControlKind.Break ? Flow.Break : Flow.Continue;
                            break;
                        case ControlKind.Return: _flow = Flow.Return; break;
                        default: ExecuteLoop(node, token); break;
                    }
                    break;
            }
        }

        // ---------------------------------------------------------------- keyboard and command

        private void ExecuteKeyboard(ActionNode node, CancellationToken token)
        {
            var k = node.Keyboard;
            int time = ToInt(Jitter(k.TimeMs));
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
            Wait(Jitter(k.DelayMs), token);
        }

        private void ExecuteWeb(ActionNode node, CancellationToken token)
        {
            var w = node.Web;
            string text = null;
            if (WebTextKind(w)) text = w.TextIsExpression ? EvalString(w.Text) : w.Text;
            else if (w.Kind == WebKind.Script) text = w.TextIsExpression ? EvalString(w.Script) : w.Script;
            bool keeps = w.Kind == WebKind.GetText || (w.Kind == WebKind.Script && !string.IsNullOrWhiteSpace(w.Id));
            if (keeps && !Expression.IsValidId((w.Id ?? "").Trim()))
                throw new InvalidOperationException((w.Kind == WebKind.Script ? "Script" : "Element Get") + ": \"" + w.Id + "\" is not a valid ID.");
            object result = (WebSession ?? AutomationTest.Core.Web.WebSession.Shared).Execute(w, text, token);
            if (keeps) _vars.Set(w.Id.Trim(), result ?? "");
            // Tab New and Tab Switch hold the actions that work in that tab (and browser).
            if (node.IsContainer) ExecuteList(node.Children, token);
            if (_flow != Flow.None) return;
            Wait(Jitter(w.DelayMs), token);
        }

        /// <summary>The Web actions whose Text field is used (and can be an expression).</summary>
        public static bool WebTextKind(WebSettings w)
        {
            return w.Kind == WebKind.Input || w.Kind == WebKind.Dialog || w.Kind == WebKind.PressKey || w.Kind == WebKind.SelectOption
                || w.Kind == WebKind.Upload || w.Kind == WebKind.Screenshot
                || (w.Kind == WebKind.WaitElement && w.Until != WebWaitKind.ElementAppears && w.Until != WebWaitKind.ElementDisappears);
        }

        private object EvalObject(string text)
        {
            try { return Expression.Evaluate(text, _vars); }
            catch (ExpressionException ex) { throw new InvalidOperationException(ex.Message); }
        }

        /// <summary>The Excel actions whose Value field can be an expression.</summary>
        public static bool ExcelValueKind(ExcelKind kind)
        {
            return kind == ExcelKind.SetCell || kind == ExcelKind.Find || kind == ExcelKind.Evaluate || kind == ExcelKind.RenameSheet || kind == ExcelKind.Copy || kind == ExcelKind.RunMacro
                || kind == ExcelKind.SheetCopy || kind == ExcelKind.CutCells || kind == ExcelKind.ReplaceText;
        }

        private void ExecuteExcel(ActionNode node, CancellationToken token)
        {
            var x = node.Excel;
            var args = new ExcelArgs { Cell = x.CellIsExpression ? EvalString(x.Cell) : x.Cell };
            if (x.Kind == ExcelKind.SetRange) args.Value = EvalObject(x.Value); // a table can only come from a variable
            else if (ExcelValueKind(x.Kind))
                args.Value = x.ValueIsExpression ? EvalObject(x.Value) : x.Value;

            bool keeps = x.Kind == ExcelKind.GetCell || x.Kind == ExcelKind.GetRange || x.Kind == ExcelKind.GetInfo || x.Kind == ExcelKind.Find || x.Kind == ExcelKind.Evaluate || x.Kind == ExcelKind.RunMacro;
            string id = (x.Id ?? "").Trim();
            if (keeps && id.Length > 0 && !Expression.IsValidId(id)) throw new InvalidOperationException("\"" + x.Id + "\" is not a valid ID.");

            object result = (ExcelSession ?? AutomationTest.Core.Excel.ExcelSession.Shared).Execute(x, args, token);
            if (keeps && id.Length > 0) _vars.Set(id, result ?? "");

            if (x.Kind == ExcelKind.Find)
            {
                var found = result as CellValue;
                if (found != null && found.Found) ExecuteList(node.Children, token);
                else Message?.Invoke("Not found in Excel: " + Format.Display(args.Value));
            }
            else if (node.IsContainer) ExecuteList(node.Children, token); // Workbook and Sheet Select hold the actions that work there
            if (_flow != Flow.None) return;
            Wait(Jitter(x.DelayMs), token);
        }

        /// <summary>Counts from a number to another; the counter is a variable that the actions inside can read (a row number, for example).</summary>
        private void ExecuteFor(ActionNode loop, CancellationToken token)
        {
            var s = loop.Loop;
            string id = (s.ForId ?? "").Trim();
            if (!Expression.IsValidId(id)) throw new InvalidOperationException("Loop (For): \"" + s.ForId + "\" is not a valid ID for the counter.");
            double from = ForNumber(s.ForFrom, "From"), to = ForNumber(s.ForTo, "To"), step = ForNumber(s.ForStep, "Step");
            if (step == 0) throw new InvalidOperationException("Loop (For): Step cannot be 0.");
            if (step > 0 ? from > to : from < to)
            {
                Message?.Invoke("Loop skipped: it counts from " + Format.Display(from) + " to " + Format.Display(to) + " with a step of " + Format.Display(step) + ".");
                return;
            }
            for (double i = from; step > 0 ? i <= to : i >= to; i += step)
            {
                _vars.Set(id, i);
                if (!Iterate(loop, token)) break;
            }
        }

        private double ForNumber(string text, string what)
        {
            object value = EvalObject(text);
            if (value is double d) return d;
            throw new InvalidOperationException("Loop (For): " + what + " must be a number, but \"" + text.Trim() + "\" gives " + Format.TypeName(value) + ".");
        }

        private sealed class ShutdownIssuedException : Exception { }

        /// <summary>The Utilities kinds that read or list something into a variable (Id).</summary>
        public static bool CommandKeeps(CommandKind kind)
        {
            return kind == CommandKind.FsExists || kind == CommandKind.FsRead || kind == CommandKind.FsList;
        }

        /// <summary>The Utilities kinds that work on files and folders.</summary>
        public static bool CommandIsFileSystem(CommandKind kind)
        {
            return kind >= CommandKind.FsCopy && kind <= CommandKind.FsList;
        }

        private void ExecuteCommand(ActionNode node, CancellationToken token)
        {
            var c = node.Command;
            switch (c.Kind)
            {
                case CommandKind.Shutdown:
                    Wait(c.TimeMs, token); // stoppable: the Stop key cancels the countdown
                    token.ThrowIfCancellationRequested();
                    Shutdown.Shutdown(c.Force);
                    throw new ShutdownIssuedException();

                case CommandKind.Active:
                    KeepAwake.Poke(_mouse);
                    break;

                case CommandKind.Open:
                    string toOpen = c.ValueIsExpression ? EvalString(c.Value) : c.Value;
                    if (string.IsNullOrWhiteSpace(toOpen))
                        throw new InvalidOperationException("Program Open: no path is set.");
                    try
                    {
                        Process.Start(new ProcessStartInfo(toOpen.Trim()) { UseShellExecute = true });
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
                    {
                        throw new InvalidOperationException("Cannot open \"" + toOpen.Trim() + "\": " + ex.Message);
                    }
                    break;

                case CommandKind.WindowActivate:
                case CommandKind.WindowMinimize:
                case CommandKind.WindowMaximize:
                case CommandKind.WindowRestore:
                case CommandKind.WindowMove:
                case CommandKind.WindowResize:
                    int arranged = ProgramActions.Arrange(c.Kind, c.Match, c.ValueIsExpression ? EvalString(c.Value) : c.Value, c.All, c.X, c.Y, c.Width, c.Height);
                    if (arranged == 0) throw new InvalidOperationException("No window found for " + c.Match + " \"" + (c.ValueIsExpression ? EvalString(c.Value) : c.Value).Trim() + "\" (is the program running, and is its window visible?).");
                    break;

                case CommandKind.Close:
                    int closed = ProgramActions.Close(c.Match, c.ValueIsExpression ? EvalString(c.Value) : c.Value, c.All, c.Force, c.TimeMs, token);
                    if (closed == 0) Message?.Invoke("Program Close: no program matches \"" + c.Value.Trim() + "\" (" + c.Match + ").");
                    break;

                default:
                    if (!CommandIsFileSystem(c.Kind)) throw new InvalidOperationException("Utilities: the action " + c.Kind + " is not known to this version of the program.");
                    ExecuteFileSystem(c);
                    break;
            }
            Wait(Jitter(c.DelayMs), token);
        }

        private void ExecuteFileSystem(CommandSettings c)
        {
            string id = (c.Id ?? "").Trim();
            if (CommandKeeps(c.Kind) && !Expression.IsValidId(id)) throw new InvalidOperationException("File/Folder: \"" + c.Id + "\" is not a valid ID.");
            string path = c.ValueIsExpression ? EvalString(c.Value) : c.Value;
            string target = c.TargetIsExpression ? EvalString(c.Target) : c.Target;
            switch (c.Kind)
            {
                case CommandKind.FsCopy: FileActions.Copy(path, target, c.Overwrite); break;
                case CommandKind.FsMove: FileActions.Move(path, target, c.Overwrite); break;
                case CommandKind.FsRename: FileActions.Rename(path, target); break;
                case CommandKind.FsDelete:
                    if (!FileActions.Delete(path, c.Recurse, c.Force)) Message?.Invoke("File/Folder Delete: nothing at \"" + path.Trim() + "\".");
                    break;
                case CommandKind.FsCreate: FileActions.CreateFolder(path); break;
                case CommandKind.FsExists: _vars.Set(id, FileActions.Exists(path)); break;
                case CommandKind.FsRead: _vars.Set(id, FileActions.Read(path)); break;
                case CommandKind.FsWrite: FileActions.Write(path, target, c.Append); break;
                case CommandKind.FsList: _vars.Set(id, FileActions.List(path, c.Recurse)); break;
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

        private void ExecuteVariable(ActionNode node, CancellationToken token)
        {
            var v = node.Variable;
            string id = (v.Id ?? "").Trim();
            if (!Expression.IsValidId(id))
                throw new InvalidOperationException("Variable: \"" + v.Id + "\" is not a valid ID (letters, digits and _, not starting with a digit).");
            if (v.Type == VariableType.Image)
            {
                var im = node.Image;
                if (im == null || !im.HasImage) throw new InvalidOperationException("Variable {" + id + "}: no picture is set (Capture or load one).");
                LocateResult found = ImageLocator.Locate(im, im.Process, token);
                _vars.Set(id, ToImageValue(im, found));
                if (!found.Found)
                {
                    string best = found.Score >= 0 ? " (best match " + (int)Math.Round(found.Score * 100) + "%, needs " + im.Accuracy + "%)" : "";
                    Message?.Invoke("Image {" + id + "} not found" + best + ".");
                }
                return;
            }
            if (v.Type == VariableType.OCR)
            {
                var area = node.Image ?? new ImageSettings();
                RawImage picture = ImageLocator.CaptureRange(area, area.Process);
                _vars.Set(id, WindowsOcr.Recognize(picture, area.OcrLanguage));
                return;
            }
            if (v.Type == VariableType.Clipboard)
            {
                if (ClipboardReader == null) throw new InvalidOperationException("Variable {" + id + "}: the clipboard cannot be read here.");
                _vars.Set(id, ClipboardReader() ?? ""); // a copy of the text now; the variable does not follow the clipboard
                return;
            }
            if (v.Type == VariableType.Password)
            {
                string secret;
                if (!_passwords.TryGetValue(id, out secret))
                {
                    if (PasswordPrompt == null) throw new InvalidOperationException("Variable {" + id + "}: a Password can only be asked for by the program.");
                    secret = PasswordPrompt(id, token);
                    if (secret == null) throw new OperationCanceledException("The password for {" + id + "} was not given.");
                    _passwords[id] = secret;
                    if (secret.Length > 0) SecretKnown?.Invoke(secret);
                }
                _vars.Set(id, secret);
                return;
            }
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
                if (branch.Disabled) continue;
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
                        // A program may show a splash screen (Excel: "Opening - Excel") that matches the same title for a moment
                        // before its real window appears. Go on only when the same window is still the match after WaitMs;
                        // otherwise keep waiting for the one that stays.
                        Wait(w.WaitMs, token);
                        if (WindowLocator.Find(w.Process) == hwnd && WindowLocator.IsActive(hwnd) && !WindowLocator.IsMinimized(hwnd))
                        {
                            ExecuteList(node.Children, token);
                            return;
                        }
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
            if (!loop.Children.Exists(c => !c.Disabled)) return; // nothing enabled inside: nothing to repeat

            var settings = loop.Loop;
            _loopDepth++;
            try { RunLoop(loop, settings, token); }
            finally { _loopDepth--; }
        }

        private void RunLoop(ActionNode loop, LoopSettings settings, CancellationToken token)
        {
            switch (settings.Mode)
            {
                case LoopType.Times:
                    for (long i = 0; settings.Count == 0 || i < settings.Count; i++)
                        if (!Iterate(loop, token)) break;
                    break;

                case LoopType.Duration:
                    var watch = Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < settings.DurationMs)
                        if (!Iterate(loop, token)) break;
                    break;

                case LoopType.Until:
                    DateTime? until = settings.GetUntil();
                    if (until == null || until.Value <= DateTime.Now)
                    {
                        Message?.Invoke("Loop skipped: the end time is missing or already in the past.");
                        break;
                    }
                    while (DateTime.Now < until.Value)
                        if (!Iterate(loop, token)) break;
                    break;

                case LoopType.For:
                    ExecuteFor(loop, token);
                    break;
            }
        }

        /// <summary>One pass over the body of a Loop. False when the Loop is over: a Break, or a Return on its way out.</summary>
        private bool Iterate(ActionNode loop, CancellationToken token)
        {
            ExecuteList(loop.Children, token);
            if (_flow == Flow.Break) { _flow = Flow.None; return false; }
            if (_flow == Flow.Return) return false;
            _flow = Flow.None; // a Continue is done: the next pass starts
            Wait(0, token); // a loop whose body has no delay still yields and checks for a stop on every pass
            return true;
        }

        // ---------------------------------------------------------------- try

        /// <summary>
        /// Runs the actions of the Try; when one fails, the rest of them are skipped and the Catch line runs (its ID, if any, gets the
        /// message). A stop and a shutdown are not errors and go through. A disabled Catch means the error is not handled.
        /// </summary>
        private void ExecuteTry(ActionNode node, CancellationToken token)
        {
            ActionNode handler = node.Children.LastOrDefault(c => c.IsKind(ControlKind.Catch));
            var body = node.Children.Where(c => !c.IsKind(ControlKind.Catch)).ToList();
            string id = (handler?.Catch?.Id ?? "").Trim();
            if (id.Length > 0 && !Expression.IsValidId(id))
                throw new InvalidOperationException("Catch: \"" + handler.Catch.Id + "\" is not a valid ID.");
            try
            {
                ExecuteList(body, token);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is ShutdownIssuedException) && handler != null && !handler.Disabled)
            {
                _flow = Flow.None;
                Message?.Invoke("Caught: " + ex.Message);
                if (id.Length > 0) _vars.Set(id, ex.Message);
                NodeStarted?.Invoke(handler);
                ExecuteList(handler.Children, token);
            }
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

            _mouse.MoveTo(x, y, ToInt(Jitter(m.TimeMs)), token);
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
            Wait(Jitter(m.DelayMs), token);
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
