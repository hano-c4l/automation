using System;
using System.Collections.Generic;
using System.Threading;
using Automation.Core.Input;
using Automation.Core.Model;

namespace Automation.Core.Runner
{
    /// <summary>Runs a script synchronously on the calling thread until it finishes or the token is cancelled.</summary>
    public sealed class ScriptRunner
    {
        private readonly MouseController _mouse;
        private readonly HashSet<MouseButton> _held = new HashSet<MouseButton>();

        public ScriptRunner(MouseController mouse)
        {
            _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        }

        /// <summary>Raised with the zero-based index of the step that is about to run (from the runner thread).</summary>
        public event Action<int> StepStarted;

        /// <summary>Raised with the one-based number of the run that is about to start (from the runner thread).</summary>
        public event Action<int> RunStarted;

        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public void Run(AutomationScript script, CancellationToken token)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            if (script.Steps.Count == 0) return;

            try
            {
                Wait(script.StartDelayMs, token);
                for (int run = 1; script.RepeatCount == 0 || run <= script.RepeatCount; run++)
                {
                    RunStarted?.Invoke(run);
                    for (int i = 0; i < script.Steps.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        StepStarted?.Invoke(i);
                        ExecuteStep(script.Steps[i], token);
                    }
                    bool last = script.RepeatCount != 0 && run == script.RepeatCount;
                    if (!last) Wait(script.RepeatDelayMs, token);
                }
            }
            finally
            {
                ReleaseHeldButtons();
            }
        }

        private void ExecuteStep(Step step, CancellationToken token)
        {
            int repeat = Math.Max(1, step.Repeat);
            for (int n = 0; n < repeat; n++)
            {
                token.ThrowIfCancellationRequested();
                _mouse.MoveTo(step.X, step.Y, step.MoveDurationMs, token);
                switch (step.Action)
                {
                    case StepAction.Click:
                        _mouse.Click(step.Button);
                        break;
                    case StepAction.MouseDown:
                        _mouse.Down(step.Button);
                        _held.Add(step.Button);
                        break;
                    case StepAction.MouseUp:
                        _mouse.Up(step.Button);
                        _held.Remove(step.Button);
                        break;
                }
                Wait(step.DelayAfterMs, token);
            }
        }

        /// <summary>Releases any button left pressed by a MouseDown step when the run stops early.</summary>
        private void ReleaseHeldButtons()
        {
            foreach (var button in _held)
            {
                try { _mouse.Up(button); }
                catch (InvalidOperationException) { }
            }
            _held.Clear();
        }

        private static void Wait(int ms, CancellationToken token)
        {
            if (ms > 0) token.WaitHandle.WaitOne(ms);
            token.ThrowIfCancellationRequested();
        }
    }
}
