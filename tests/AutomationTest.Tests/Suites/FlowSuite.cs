using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>Try / Catch, Break, Continue, Return and Section in the runner (they need no input device).</summary>
    internal static class FlowSuite
    {
        private static ActionNode Var(string id, string text)
        {
            return ActionNode.NewVariable(new VariableSettings { Id = id, Type = VariableType.Number, Text = text });
        }

        private static ActionNode Calc(string id, string expression)
        {
            return ActionNode.NewVariable(new VariableSettings { Id = id, Type = VariableType.Number, UseExpression = true, Expression = expression });
        }

        /// <summary>An action that fails: it reads a variable that does not exist.</summary>
        private static ActionNode Fail()
        {
            return Calc("never", "{missing} + 1");
        }

        private static ActionNode Cond(string expression, params ActionNode[] whenTrue)
        {
            var c = ActionNode.NewCondition();
            c.Children[0].Case.Expression = expression;
            c.Children[0].Children.AddRange(whenTrue);
            return c;
        }

        private static ActionNode Loop(long times, params ActionNode[] body)
        {
            var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = times });
            loop.Children.AddRange(body);
            return loop;
        }

        private static ActionNode Try(ActionNode[] body, string errorId = "", params ActionNode[] handler)
        {
            var t = ActionNode.NewTry();
            var catchNode = t.Children[0];
            catchNode.Catch.Id = errorId;
            catchNode.Children.AddRange(handler);
            t.Children.InsertRange(0, body);
            return t;
        }

        private static ScriptRunner Run(params ActionNode[] actions)
        {
            var runner = new ScriptRunner(new MouseController());
            runner.Run(new List<ActionNode>(actions), null, CancellationToken.None);
            return runner;
        }

        private static object Get(ScriptRunner runner, string name)
        {
            object value;
            return runner.Variables.TryGet(name, out value) ? value : "(not set)";
        }

        [Suite("flow.try")]
        public static void TryCatch(T t)
        {
            // a failing action: the rest of the Try is skipped, the Catch runs, the run goes on
            var r = Run(Var("a", "0"), Var("after", "0"),
                Try(new[] { Calc("a", "1"), Fail(), Calc("a", "2") }, "err", Calc("a", "{a} + 10")),
                Calc("after", "1"));
            t.Eq("the action before the failure ran, the one after did not", Get(r, "a"), 11.0);
            t.Eq("the run goes on after the Try", Get(r, "after"), 1.0);
            t.True("the message of the error is in the variable", ((string)Get(r, "err")).Contains("missing"));

            // no error: the Catch does not run
            r = Run(Var("a", "0"), Try(new[] { Calc("a", "5") }, "err", Calc("a", "99")));
            t.Eq("no error, no catch", Get(r, "a"), 5.0);
            t.Eq("no error, no error variable", Get(r, "err"), "(not set)");

            // a Catch without an ID still catches
            r = Run(Var("a", "0"), Try(new[] { Fail() }, "", Calc("a", "7")));
            t.Eq("a catch without an ID", Get(r, "a"), 7.0);

            // a disabled Catch does not handle the error
            var unhandled = Try(new[] { Fail() }, "", Calc("a", "7"));
            unhandled.Children.Last().Disabled = true;
            t.Throws("a disabled Catch lets the error through", () => Run(Var("a", "0"), unhandled), "missing");

            // a disabled Try does nothing at all
            var off = Try(new[] { Fail() }, "", Calc("a", "7"));
            off.Disabled = true;
            t.Eq("a disabled Try is skipped", Get(Run(Var("a", "0"), off), "a"), 0.0);

            // an error inside the Catch is not caught by the same Try, but by one around it
            t.Throws("an error in the Catch stops the run", () => Run(Try(new[] { Fail() }, "", Fail())), "missing");
            r = Run(Var("a", "0"), Try(new[] { Try(new[] { Fail() }, "", Fail()) }, "", Calc("a", "3")));
            t.Eq("an outer Try catches the error of an inner Catch", Get(r, "a"), 3.0);

            // an inner Try handles its own error; the outer one sees nothing
            r = Run(Var("a", "0"), Try(new[] { Try(new[] { Fail() }, "", Calc("a", "1")), Calc("a", "{a} + 1") }, "", Calc("a", "100")));
            t.Eq("an inner Try handles its own error", Get(r, "a"), 2.0);

            // the Stop key is not an error
            var forever = Loop(0, Calc("n", "{n} + 1"));
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(200);
                t.Throws<OperationCanceledException>("Stop goes through a Try", () =>
                    new ScriptRunner(new MouseController()).Run(new List<ActionNode> { Var("n", "0"), Try(new[] { forever }, "", Calc("n", "-1")) }, null, cts.Token), "");
            }

            // a Catch on its own and a Catch of another kind of line are mistakes the runner names
            t.Throws("a Catch outside a Try", () => Run(ActionNode.NewCatch(null)), "last line of a Try");
        }

        [Suite("flow.jumps")]
        public static void Jumps(T t)
        {
            // Break
            var r = Run(Var("n", "0"), Loop(10, Calc("n", "{n} + 1"), Cond("{n} == 3", ActionNode.NewJump(ControlKind.Break))));
            t.Eq("Break leaves the loop", Get(r, "n"), 3.0);

            // Continue skips the rest of the pass
            r = Run(Var("n", "0"), Var("sum", "0"), Loop(5, Calc("n", "{n} + 1"), Cond("{n} == 2", ActionNode.NewJump(ControlKind.Continue)), Calc("sum", "{sum} + {n}")));
            t.Eq("Continue skips only the rest of the pass", Get(r, "sum"), 13.0);   // 1 + 3 + 4 + 5

            // Return ends the whole script, also from inside loops, a Try and a Section
            var section = ActionNode.NewSection();
            section.Children.Add(ActionNode.NewJump(ControlKind.Return));
            section.Children.Add(Calc("n", "{n} + 100"));
            r = Run(Var("n", "0"), Loop(3, Calc("n", "{n} + 1"), Try(new[] { section }, "", Calc("n", "-5"))), Calc("n", "{n} + 1000"));
            t.Eq("Return ends everything", Get(r, "n"), 1.0);

            // Break inside a Try and a Section inside a Loop still leaves that Loop, and is not an error
            var inside = ActionNode.NewSection();
            inside.Children.Add(Cond("{n} == 2", ActionNode.NewJump(ControlKind.Break)));
            r = Run(Var("n", "0"), Loop(10, Calc("n", "{n} + 1"), Try(new[] { inside }, "err", Calc("n", "-50"))));
            t.Eq("Break through a Try and a Section", Get(r, "n"), 2.0);
            t.Eq("a Break is not an error", Get(r, "err"), "(not set)");

            // only the nearest Loop is left
            var inner = Loop(10, Calc("n", "{n} + 1"), ActionNode.NewJump(ControlKind.Break));
            r = Run(Var("n", "0"), Loop(3, inner));
            t.Eq("Break leaves only the inner loop", Get(r, "n"), 3.0);

            // Continue in a For loop and Break in a Duration-free loop
            var forLoop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.For, ForId = "i", ForFrom = "1", ForTo = "5", ForStep = "1" });
            forLoop.Children.Add(Cond("{i} % 2 == 0", ActionNode.NewJump(ControlKind.Continue)));
            forLoop.Children.Add(Calc("sum", "{sum} + {i}"));
            t.Eq("Continue in a For loop (odd numbers only)", Get(Run(Var("sum", "0"), forLoop), "sum"), 9.0);

            // a jump with no Loop is an error that says so
            t.Throws("Break outside a Loop", () => Run(ActionNode.NewJump(ControlKind.Break)), "inside a Loop");
            t.Throws("Continue outside a Loop", () => Run(ActionNode.NewJump(ControlKind.Continue)), "inside a Loop");
            t.Throws("Break in a Section outside a Loop", () => { var s = ActionNode.NewSection(); s.Children.Add(ActionNode.NewJump(ControlKind.Break)); Run(s); }, "inside a Loop");

            // a Return alone just ends the script
            t.Eq("Return at the top", Get(Run(Var("a", "1"), ActionNode.NewJump(ControlKind.Return), Calc("a", "2")), "a"), 1.0);

            // a jump that is disabled does nothing
            var offBreak = ActionNode.NewJump(ControlKind.Break);
            offBreak.Disabled = true;
            t.Eq("a disabled Break", Get(Run(Var("n", "0"), Loop(3, Calc("n", "{n} + 1"), offBreak)), "n"), 3.0);

            // the next run starts clean after a Return
            var runner = new ScriptRunner(new MouseController());
            runner.Run(new List<ActionNode> { ActionNode.NewJump(ControlKind.Return) }, null, CancellationToken.None);
            runner.Run(new List<ActionNode> { Var("z", "4") }, null, CancellationToken.None);
            t.Eq("a Return does not leak into the next run", Get(runner, "z"), 4.0);
        }

        [Suite("flow.partial-run")]
        public static void PartialRun(T t)
        {
            // Start Selected on the inside of a Loop: a Break or Continue ends the selection, it is not an error
            var messages = new List<string>();
            var runner = new ScriptRunner(new MouseController());
            runner.Message += messages.Add;
            runner.Run(new List<ActionNode> { Var("a", "1"), ActionNode.NewJump(ControlKind.Break), Var("a", "2") }, null, CancellationToken.None, partial: true);
            t.Eq("the actions before the Break ran", Get(runner, "a"), 1.0);
            t.True("and a note says the selection ended", messages.Any(m => m.Contains("selection ends")));
            runner.Run(new List<ActionNode> { Var("a", "1"), Cond("true", ActionNode.NewJump(ControlKind.Continue)), Var("a", "2") }, null, CancellationToken.None, partial: true);
            t.Eq("a Continue inside a Condition ends it as well", Get(runner, "a"), 1.0);
            // the same actions in a whole run (not partial) are still an error
            t.Throws("a whole run keeps the error", () => Run(Var("a", "1"), ActionNode.NewJump(ControlKind.Break)), "inside a Loop");
        }

        [Suite("flow.section")]
        public static void Sections(T t)
        {
            var section = ActionNode.NewSection();
            section.Name = "setup";
            section.Children.Add(Var("a", "1"));
            section.Children.Add(Calc("b", "{a} + 1"));
            var r = Run(section);
            t.Eq("a Section runs what is inside it once (1)", Get(r, "a"), 1.0);
            t.Eq("a Section runs what is inside it once (2)", Get(r, "b"), 2.0);

            var off = ActionNode.NewSection();
            off.Children.Add(Var("c", "9"));
            off.Disabled = true;
            t.Eq("a disabled Section skips everything inside", Get(Run(off), "c"), "(not set)");

            // time: the sum of what is inside, like a pass of a Loop
            var timed = ActionNode.NewSection();
            timed.Children.Add(ActionNode.NewMouse(new MouseSettings { TimeMs = 1000, DelayMs = 500 }));
            var disabledChild = ActionNode.NewMouse(new MouseSettings { TimeMs = 9000 });
            disabledChild.Disabled = true;
            timed.Children.Add(disabledChild);
            t.Eq("a Section's time is the sum of its actions", ActionTiming.PerIterationMs(timed), 1500L);
            var loop = Loop(2, timed);
            t.Eq("a Section inside a Loop counts in the Loop's pass", ActionTiming.PerIterationMs(loop), 1500L);

            // a Try counts its own actions, not its Catch
            var tr = Try(new[] { ActionNode.NewMouse(new MouseSettings { TimeMs = 400, DelayMs = 0 }) }, "", ActionNode.NewMouse(new MouseSettings { TimeMs = 99999, DelayMs = 0 }));
            t.Eq("a Try counts its body only", ActionTiming.PerIterationMs(Loop(1, tr)), 400L);
        }

        [Suite("flow.start-selected")]
        public static void StartSelected(T t)
        {
            var script = new AutomationScript();
            var tr = Try(new[] { Calc("a", "{x} + 1") }, "err", Calc("a", "{y}"));
            script.Actions.Add(tr);
            // the variable the Catch defines is not asked for, the others are
            var plan = SelectionPlanner.Build(script, new[] { tr.Children.Last() });
            t.Eq("a selected Catch runs what is inside it", plan.RunNodes.Count, 1);
            plan = SelectionPlanner.Build(script, new[] { tr });
            var names = plan.Needs.Select(n => n.Name.ToLowerInvariant()).OrderBy(n => n).ToList();
            t.Eq("a selected Try asks for what its actions read", string.Join(",", names), "x,y");

            var withId = Try(new[] { Fail() }, "e", Calc("a", "{e} + 0"));
            var script2 = new AutomationScript();
            script2.Actions.Add(withId);
            plan = SelectionPlanner.Build(script2, new[] { withId });
            t.False("the error variable is not asked for", plan.Needs.Any(n => n.Name.Equals("e", StringComparison.OrdinalIgnoreCase)));
        }
    }
}
