using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>
    /// The runner on actions that need no input device: Variable, Condition, Loop and the planner of Start Selected.
    /// </summary>
    internal static class RunnerSuite
    {
        private static ActionNode Var(string id, string text, VariableType type = VariableType.Number)
        {
            return ActionNode.NewVariable(new VariableSettings { Id = id, Type = type, Text = text });
        }

        private static ActionNode Calc(string id, string expression, VariableType type = VariableType.Number)
        {
            return ActionNode.NewVariable(new VariableSettings { Id = id, Type = type, UseExpression = true, Expression = expression });
        }

        private static ScriptRunner Run(List<ActionNode> actions, IDictionary<string, object> start = null, List<string> messages = null)
        {
            var runner = new ScriptRunner(new MouseController());
            if (messages != null) runner.Message += messages.Add;
            runner.Run(actions, start, CancellationToken.None);
            return runner;
        }

        private static object Get(ScriptRunner runner, string name)
        {
            object value;
            return runner.Variables.TryGet(name, out value) ? value : "(not set)";
        }

        [Suite("runner.variables")]
        public static void Variables(T t)
        {
            var r = Run(new List<ActionNode>
            {
                Var("n", "5"), Var("s", "hello", VariableType.String), Var("b", "true", VariableType.Boolean),
                Calc("sum", "{n} + 1"), Calc("text", "{s} + \" world\"", VariableType.String),
                Var("d", "2026-03-04", VariableType.Date),
            });
            t.Eq("number literal", Get(r, "n"), 5.0);
            t.Eq("string literal", Get(r, "s"), "hello");
            t.Eq("boolean literal", Get(r, "b"), true);
            t.Eq("expression", Get(r, "sum"), 6.0);
            t.Eq("string expression", Get(r, "text"), "hello world");
            t.Eq("date literal", Get(r, "d"), new DateTime(2026, 3, 4));

            // declaring the same ID again assigns it again
            r = Run(new List<ActionNode> { Var("n", "1"), Calc("n", "{n} + 10") });
            t.Eq("assigning again", Get(r, "n"), 11.0);

            // values given at the start (Start Selected)
            r = Run(new List<ActionNode> { Calc("y", "{x} * 2") }, new Dictionary<string, object> { { "x", 4.0 } });
            t.Eq("start values", Get(r, "y"), 8.0);

            t.Throws("a variable with no value stops the run and is named", () => Run(new List<ActionNode> { Calc("y", "{missing} + 1") }), "missing");
            t.Throws("a literal that does not fit the type", () => Run(new List<ActionNode> { Var("n", "abc") }), "");
        }

        [Suite("runner.condition")]
        public static void Condition(T t)
        {
            Func<string, string, ActionNode> make = (first, second) =>
            {
                var c = ActionNode.NewCondition();
                c.Children[0].Case.Expression = first;
                c.Children[0].Children.Add(Var("r", "1"));
                var second_ = ActionNode.NewCase(new CaseSettings { Expression = second });
                second_.Children.Add(Var("r", "2"));
                c.Children.Insert(1, second_);
                c.Children[c.Children.Count - 1].Children.Add(Var("r", "3"));   // else
                return c;
            };

            var start = new Dictionary<string, object> { { "abc", 5.0 } };
            t.Eq("only the first true case runs", Get(Run(new List<ActionNode> { make("{abc} > 3", "{abc} > 2") }, start), "r"), 1.0);
            t.Eq("a later case runs when the first is false", Get(Run(new List<ActionNode> { make("{abc} > 9", "{abc} > 2") }, start), "r"), 2.0);
            t.Eq("else runs when no case is true", Get(Run(new List<ActionNode> { make("{abc} > 9", "{abc} > 8") }, start), "r"), 3.0);

            var disabled = make("true", "true");
            disabled.Children[0].Disabled = true;
            t.Eq("a disabled case is skipped", Get(Run(new List<ActionNode> { disabled }, start), "r"), 2.0);
        }

        [Suite("runner.loops")]
        public static void Loops(T t)
        {
            var times = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 4 });
            times.Children.Add(Calc("n", "{n} + 1"));
            t.Eq("Loop (Times)", Get(Run(new List<ActionNode> { Var("n", "0"), times }), "n"), 4.0);

            var forLoop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.For, ForId = "i", ForFrom = "1", ForTo = "5", ForStep = "2" });
            forLoop.Children.Add(Calc("sum", "{sum} + {i}"));
            t.Eq("Loop (For) 1 to 5 step 2 = 1+3+5", Get(Run(new List<ActionNode> { Var("sum", "0"), forLoop }), "sum"), 9.0);

            var down = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.For, ForId = "i", ForFrom = "3", ForTo = "1", ForStep = "-1" });
            down.Children.Add(Calc("sum", "{sum} * 10 + {i}"));
            t.Eq("Loop (For) counting down", Get(Run(new List<ActionNode> { Var("sum", "0"), down }), "sum"), 321.0);

            // a step with a fraction does not drift: 0, 0.1 ... 1 is 11 passes (adding 0.1 ten times gives 0.9999999999999999, then 1.0999...)
            var fraction = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.For, ForId = "i", ForFrom = "0", ForTo = "1", ForStep = "0.1" });
            fraction.Children.Add(Calc("n", "{n} + 1"));
            t.Eq("Loop (For) 0 to 1 step 0.1 runs 11 times", Get(Run(new List<ActionNode> { Var("n", "0"), fraction }), "n"), 11.0);

            var skipped = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.For, ForId = "i", ForFrom = "5", ForTo = "1", ForStep = "1" });
            skipped.Children.Add(Calc("sum", "{sum} + 1"));
            var messages = new List<string>();
            t.Eq("Loop (For) that cannot run", Get(Run(new List<ActionNode> { Var("sum", "0"), skipped }, null, messages), "sum"), 0.0);
            t.True("it says that the loop was skipped", messages.Any(m => m.IndexOf("skipped", StringComparison.OrdinalIgnoreCase) >= 0));

            var zeroStep = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.For, ForId = "i", ForFrom = "1", ForTo = "5", ForStep = "0" });
            zeroStep.Children.Add(Var("x", "1"));
            t.Throws("Loop (For) with step 0", () => Run(new List<ActionNode> { zeroStep }), "Step cannot be 0");

            // disabled actions and disabled parents are skipped
            var off = Var("v", "9");
            off.Disabled = true;
            t.Eq("a disabled action is skipped", Get(Run(new List<ActionNode> { Var("v", "1"), off }), "v"), 1.0);
            var offLoop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 3 });
            offLoop.Children.Add(Calc("v", "{v} + 1"));
            offLoop.Disabled = true;
            t.Eq("everything in a disabled loop is skipped", Get(Run(new List<ActionNode> { Var("v", "1"), offLoop }), "v"), 1.0);

            // Stop
            var forever = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 0 });
            forever.Children.Add(Calc("n", "{n} + 1"));
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(200);
                t.Throws<OperationCanceledException>("Stop ends an endless loop", () =>
                    new ScriptRunner(new MouseController()).Run(new List<ActionNode> { Var("n", "0"), forever }, null, cts.Token), "");
            }
        }

        [Suite("runner.timing")]
        public static void Timing(T t)
        {
            var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 2 });
            loop.Children.Add(ActionNode.NewMouse(new MouseSettings { TimeMs = 1000, DelayMs = 500 }));
            loop.Children.Add(ActionNode.NewKeyboard(new KeyboardSettings { Action = KeyboardAction.Text, TimeMs = 200, DelayMs = 100 }));
            t.Eq("one pass = Time + Delay of each action", ActionTiming.PerIterationMs(loop), 1800L);

            var off = ActionNode.NewMouse(new MouseSettings { TimeMs = 5000 });
            off.Disabled = true;
            loop.Children.Add(off);
            t.Eq("a disabled action is not counted", ActionTiming.PerIterationMs(loop), 1800L);

            var endless = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 0 });
            endless.Children.Add(ActionNode.NewMouse(new MouseSettings { TimeMs = 10 }));
            var outer = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 3 });
            outer.Children.Add(endless);
            t.True("an endless loop inside makes the time unknown", ActionTiming.PerIterationMs(outer) == null);
        }

        [Suite("runner.selection")]
        public static void Selection(T t)
        {
            var script = new AutomationScript();
            var define = Var("a", "1");
            var use = Calc("b", "{a} + {unknown}");
            var later = Calc("c", "{b} + 1");
            script.Actions.AddRange(new[] { define, use, later });

            var plan = SelectionPlanner.Build(script, new[] { use });
            var names = plan.Needs.Select(n => n.Name.ToLowerInvariant()).OrderBy(n => n).ToList();
            t.Eq("a selected action asks for the variables nobody in the selection sets", string.Join(",", names), "a,unknown");

            plan = SelectionPlanner.Build(script, new[] { define, use });
            names = plan.Needs.Select(n => n.Name.ToLowerInvariant()).OrderBy(n => n).ToList();
            t.Eq("a variable set earlier in the selection is not asked for", string.Join(",", names), "unknown");
            t.Eq("the selected actions run in order", plan.RunNodes.Count, 2);

            // a selected case runs its children, not the condition
            var cond = ActionNode.NewCondition();
            cond.Children[0].Children.Add(Var("r", "1"));
            script.Actions.Add(cond);
            plan = SelectionPlanner.Build(script, new[] { cond.Children[0] });
            t.Eq("a selected Case runs what is inside it", plan.RunNodes.Count, 1);
        }
    }
}
