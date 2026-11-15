using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>Auto Script: running another script file as a part of a run.</summary>
    internal static class AutoScriptSuite
    {
        private static ActionNode Calc(string id, string expression, VariableType type = VariableType.Number)
        {
            return ActionNode.NewVariable(new VariableSettings { Id = id, Type = type, UseExpression = true, Expression = expression });
        }

        private static ActionNode Num(string id, string text)
        {
            return ActionNode.NewVariable(new VariableSettings { Id = id, Type = VariableType.Number, Text = text });
        }

        private static ActionNode Call(string file, string inputs = "", string returns = "", bool expression = false)
        {
            return ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.AutoScript, Value = file, Params = inputs, Returns = returns, ValueIsExpression = expression });
        }

        private static string Save(string folder, string name, params ActionNode[] actions)
        {
            var script = new AutomationScript();
            script.Actions.AddRange(actions);
            string path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ScriptSerializer.Save(script, path);
            return path;
        }

        private static ScriptRunner Run(string folder, params ActionNode[] actions)
        {
            var runner = new ScriptRunner(new MouseController()) { ScriptFolder = folder };
            runner.Run(new List<ActionNode>(actions), null, CancellationToken.None);
            return runner;
        }

        private static object Get(ScriptRunner runner, string name)
        {
            object value;
            return runner.Variables.TryGet(name, out value) ? value : "(not set)";
        }

        [Suite("autoscript.run")]
        public static void RunIt(T t)
        {
            string folder = Path.Combine(Path.GetTempPath(), "at_auto_" + Guid.NewGuid().ToString("N"));
            try
            {
                // inputs go in, results come back
                string double_ = Save(folder, "double.json", Calc("result", "{n} * 2"), Calc("leak", "1"));
                var r = Run(folder, Num("secret", "5"), Call(double_, "n = 21", "result"));
                t.Eq("the result comes back", Get(r, "result"), 42.0);
                t.Eq("a variable of the script that is not returned stays there", Get(r, "leak"), "(not set)");
                t.Eq("the inputs are not left here either", Get(r, "n"), "(not set)");

                // the inputs are worked out with this script's variables
                r = Run(folder, Num("a", "4"), Call(double_, "n = {a} + 1", "result"));
                t.Eq("an input is an expression of this script", Get(r, "result"), 10.0);

                // the script cannot see the variables of this one
                string peek = Save(folder, "peek.json", Calc("seen", "{secret}"));
                t.Throws("the script sees only its inputs", () => Run(folder, Num("secret", "5"), Call(peek, "", "seen")), "secret");
                r = Run(folder, Num("secret", "5"), Call(peek, "secret = {secret}", "seen"));
                t.Eq("unless it is handed over", Get(r, "seen"), 5.0);

                // a relative path starts in the folder of the script that calls; the path can be an expression
                r = Run(folder, Call("double.json", "n = 3", "result"));
                t.Eq("a relative path", Get(r, "result"), 6.0);
                r = Run(folder, Call("\"dou\" + \"ble.json\"", "n = 4", "result", expression: true));
                t.Eq("the file name as an expression", Get(r, "result"), 8.0);
                string deep = Save(folder, "sub\\inner.json", Call("..\\double.json", "n = 5", "result"), Calc("inner", "{result} + 1"));
                r = Run(folder, Call("sub\\inner.json", "", "inner"));
                t.Eq("a script that calls one in another folder uses its own folder", Get(r, "inner"), 11.0);

                // a Return ends the script, not the one that called it
                string early = Save(folder, "early.json", Calc("v", "1"), ActionNode.NewJump(ControlKind.Return), Calc("v", "2"));
                r = Run(folder, Call(early, "", "v"), Calc("after", "{v} + 100"));
                t.Eq("Return ends only the script", Get(r, "v"), 1.0);
                t.Eq("and the caller goes on", Get(r, "after"), 101.0);

                // an error in the script is an error of the action, and a Try can catch it
                string broken = Save(folder, "broken.json", Calc("x", "{nothing} + 1"));
                t.Throws("an error in the script says which script", () => Run(folder, Call(broken)), "In broken.json");
                var tr = ActionNode.NewTry();
                tr.Children.Insert(0, Call(broken));
                tr.Children[1].Catch.Id = "err";
                var caught = Run(folder, tr);
                t.True("a Try around it catches the error", ((string)Get(caught, "err")).Contains("broken.json"));

                // a Break in the script does not reach a Loop of the caller
                string brk = Save(folder, "brk.json", ActionNode.NewJump(ControlKind.Break));
                var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 3 });
                loop.Children.Add(Call(brk));
                t.Throws("Break in the script is an error there", () => Run(folder, loop), "inside a Loop");

                // problems that are told clearly
                t.Throws("no such file", () => Run(folder, Call("missing.json")), "cannot open");
                t.Throws("no file given", () => Run(folder, Call("")), "enter the script file");
                string notReturned = Save(folder, "norez.json", Calc("a", "1"));
                t.Throws("a result that the script does not set", () => Run(folder, Call(notReturned, "", "b")), "did not set the variable {b}");
                t.Throws("an input without an expression", () => Run(folder, Call(double_, "n =")), "no expression");
                t.Throws("an input with a bad name", () => Run(folder, Call(double_, "1x = 2")), "valid name");
                File.WriteAllText(Path.Combine(folder, "garbage.json"), "this is not a script");
                t.Throws("a file that is not a script", () => Run(folder, Call("garbage.json")), "cannot open");

                // a script that calls itself, and scripts that call each other
                string self = Path.Combine(folder, "self.json");
                Save(folder, "self.json", Call(self));
                t.Throws("a script that calls itself", () => Run(folder, Call(self)), "calls itself");
                Save(folder, "ping.json", Call(Path.Combine(folder, "pong.json")));
                Save(folder, "pong.json", Call(Path.Combine(folder, "ping.json")));
                t.Throws("two scripts that call each other", () => Run(folder, Call(Path.Combine(folder, "ping.json"))), "calls itself");

                // the same script can be called twice, one after the other
                r = Run(folder, Call(double_, "n = 1", "result"), Calc("first", "{result}"), Call(double_, "n = 2", "result"));
                t.Eq("calling twice (first)", Get(r, "first"), 2.0);
                t.Eq("calling twice (second)", Get(r, "result"), 4.0);
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
        }

        [Suite("autoscript.shared-with-the-run")]
        public static void Shared(T t)
        {
            string folder = Path.Combine(Path.GetTempPath(), "at_auto_" + Guid.NewGuid().ToString("N"));
            try
            {
                // a password is asked once for the whole run, also when the script asks for it again
                string child = Save(folder, "pw.json", ActionNode.NewVariable(new VariableSettings { Id = "pw", Type = VariableType.Password }), Calc("len", "{pw}", VariableType.String));
                var asked = new List<string>();
                var runner = new ScriptRunner(new MouseController()) { ScriptFolder = folder, PasswordPrompt = (id, token) => { asked.Add(id); return "p"; } };
                runner.Run(new List<ActionNode> { ActionNode.NewVariable(new VariableSettings { Id = "pw", Type = VariableType.Password }), Call(child, "", "len") }, null, CancellationToken.None);
                t.Eq("asked once for the run", asked.Count, 1);
                t.Eq("the script got the password", Get(runner, "len"), "p");

                // Stop goes through
                string forever = Save(folder, "forever.json", ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 0 }));
                var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 0 });
                loop.Children.Add(Calc("n", "1"));
                string endless = Save(folder, "endless.json", loop);
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(200);
                    t.Throws<OperationCanceledException>("Stop ends a script that is running inside", () =>
                        new ScriptRunner(new MouseController()) { ScriptFolder = folder }.Run(new List<ActionNode> { Call(endless) }, null, cts.Token), "");
                }

                // the run is clean afterwards: a second run is not confused by a script that failed in the first
                var again = new ScriptRunner(new MouseController()) { ScriptFolder = folder };
                try { again.Run(new List<ActionNode> { Call(Save(folder, "bad.json", Calc("x", "{no} + 1"))) }, null, CancellationToken.None); } catch (InvalidOperationException) { }
                again.Run(new List<ActionNode> { Call(Save(folder, "ok.json", Calc("y", "7")), "", "y") }, null, CancellationToken.None);
                t.Eq("the next run works", Get(again, "y"), 7.0);
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
        }

        [Suite("autoscript.in-the-editor")]
        public static void InTheEditor(T t)
        {
            var e = new EditorSuite.Editor();
            var node = ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.AutoScript, Value = "C:/x/job.json", Params = "a = 1\r\nb = {c} + 1", Returns = "r1, r2", ValueIsExpression = false });
            var back = e.Through(node).Command;
            t.Eq("kind", back.Kind, CommandKind.AutoScript);
            t.Eq("file", back.Value, "C:/x/job.json");
            t.Eq("inputs", back.Params.Replace("\r\n", "\n"), "a = 1\nb = {c} + 1");
            t.Eq("returns", back.Returns, "r1, r2");

            var other = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsCopy, Value = "a", Target = "b", Params = "stale = 1", Returns = "stale" })).Command;
            t.Eq("other kinds drop the inputs and returns", other.Params + other.Returns, "");

            // what a selection of Start Selected needs and gives
            var script = new AutomationScript();
            var call = ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.AutoScript, Value = "x.json", Params = "n = {outer} + 1", Returns = "a, b" });
            var use = Calc("z", "{a} + {b}");
            script.Actions.Add(call);
            script.Actions.Add(use);
            var plan = SelectionPlanner.Build(script, new[] { call, use });
            t.Eq("the inputs read variables and the returns define them", string.Join(",", plan.Needs.Select(n => n.Name)), "outer");
        }
    }
}
