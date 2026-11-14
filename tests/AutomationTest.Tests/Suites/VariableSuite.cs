using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AutomationTest.App;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>Variable kinds Clipboard and Password, and what Start Selected remembers between runs.</summary>
    internal static class VariableSuite
    {
        private static ActionNode Make(string id, VariableType type)
        {
            return ActionNode.NewVariable(new VariableSettings { Id = id, Type = type });
        }

        private static object Get(ScriptRunner runner, string name)
        {
            object value;
            return runner.Variables.TryGet(name, out value) ? value : "(not set)";
        }

        [Suite("variables.clipboard")]
        public static void Clipboard(T t)
        {
            string current = "first";
            var runner = new ScriptRunner(new MouseController()) { ClipboardReader = () => current };
            runner.Run(new List<ActionNode> { Make("c", VariableType.Clipboard) }, null, CancellationToken.None);
            t.Eq("the text in the clipboard when the action runs", Get(runner, "c"), "first");
            current = "second";
            t.Eq("the variable does not follow the clipboard afterwards", Get(runner, "c"), "first");
            runner.Run(new List<ActionNode> { Make("c", VariableType.Clipboard) }, null, CancellationToken.None);
            t.Eq("running it again takes the new text", Get(runner, "c"), "second");

            // used in an expression like any String
            var calc = ActionNode.NewVariable(new VariableSettings { Id = "d", Type = VariableType.String, UseExpression = true, Expression = "{c} + \"!\"" });
            runner.Run(new List<ActionNode> { Make("c", VariableType.Clipboard), calc }, null, CancellationToken.None);
            t.Eq("the clipboard text in an expression", Get(runner, "d"), "second!");

            // a clipboard with no text is an empty string, not an error
            var empty = new ScriptRunner(new MouseController()) { ClipboardReader = () => null };
            empty.Run(new List<ActionNode> { Make("c", VariableType.Clipboard) }, null, CancellationToken.None);
            t.Eq("no text: an empty string", Get(empty, "c"), "");

            // without a way to read it (no program around), the action says so
            t.Throws("no way to read the clipboard", () => new ScriptRunner(new MouseController()).Run(new List<ActionNode> { Make("c", VariableType.Clipboard) }, null, CancellationToken.None), "clipboard cannot be read");

            // nothing is stored in the file
            var node = ActionNode.NewVariable(new VariableSettings { Id = "c", Type = VariableType.Clipboard, Text = "left over", UseExpression = true, Expression = "left" });
            var script = new AutomationScript();
            script.Actions.Add(node);
            string json = ScriptSerializer.ToJson(script);
            t.False("no left-over text in the file", json.Contains("left over") || json.Contains("\"left\""));
        }

        [Suite("variables.password")]
        public static void Password(T t)
        {
            var asked = new List<string>();
            var secrets = new List<string>();
            var runner = new ScriptRunner(new MouseController())
            {
                PasswordPrompt = (id, token) => { asked.Add(id); return "pw-" + id; }
            };
            runner.SecretKnown += secrets.Add;

            // asked once for an ID in a run, not at every pass of a Loop
            var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 3 });
            loop.Children.Add(Make("pw", VariableType.Password));
            runner.Run(new List<ActionNode> { loop }, null, CancellationToken.None);
            t.Eq("the value is the one typed", Get(runner, "pw"), "pw-pw");
            t.Eq("asked once in a loop of three", asked.Count, 1);
            t.Eq("the program was told the secret", string.Join(",", secrets), "pw-pw");

            // another ID is another question; the same ID in other letters is the same one
            asked.Clear();
            runner.Run(new List<ActionNode> { Make("a", VariableType.Password), Make("b", VariableType.Password), Make("A", VariableType.Password) }, null, CancellationToken.None);
            t.Eq("two IDs, two questions", string.Join(",", asked), "a,b");

            // every run asks again
            asked.Clear();
            runner.Run(new List<ActionNode> { Make("a", VariableType.Password) }, null, CancellationToken.None);
            runner.Run(new List<ActionNode> { Make("a", VariableType.Password) }, null, CancellationToken.None);
            t.Eq("each run asks again", asked.Count, 2);

            // cancelling the question stops the run (like Stop), it is not an error
            var cancelling = new ScriptRunner(new MouseController()) { PasswordPrompt = (id, token) => null };
            var after = ActionNode.NewVariable(new VariableSettings { Id = "x", Type = VariableType.Number, Text = "1" });
            t.Throws<OperationCanceledException>("Cancel stops the run", () => cancelling.Run(new List<ActionNode> { Make("a", VariableType.Password), after }, null, CancellationToken.None), "password");
            t.Eq("and nothing after it ran", Get(cancelling, "x"), "(not set)");

            // an empty password is allowed, and is not announced as a secret
            var emptySecrets = new List<string>();
            var emptyPw = new ScriptRunner(new MouseController()) { PasswordPrompt = (id, token) => "" };
            emptyPw.SecretKnown += emptySecrets.Add;
            emptyPw.Run(new List<ActionNode> { Make("a", VariableType.Password) }, null, CancellationToken.None);
            t.Eq("an empty password is the empty string", Get(emptyPw, "a"), "");
            t.Eq("and is no secret to hide", emptySecrets.Count, 0);

            // with no program to ask, the action says so
            t.Throws("nobody to ask", () => new ScriptRunner(new MouseController()).Run(new List<ActionNode> { Make("a", VariableType.Password) }, null, CancellationToken.None), "asked for by the program");

            // used in an expression
            var calc = ActionNode.NewVariable(new VariableSettings { Id = "joined", Type = VariableType.String, UseExpression = true, Expression = "\"user:\" + {pw}" });
            runner.Run(new List<ActionNode> { Make("pw", VariableType.Password), calc }, null, CancellationToken.None);
            t.Eq("the password in an expression", Get(runner, "joined"), "user:pw-pw");
        }

        [Suite("variables.password-is-never-stored")]
        public static void NeverStored(T t)
        {
            // set by code: the file does not get it
            var node = ActionNode.NewVariable(new VariableSettings { Id = "pw", Type = VariableType.Password, Text = "hunter2", UseExpression = true, Expression = "hunter2expr" });
            var script = new AutomationScript();
            script.Actions.Add(node);
            string json = ScriptSerializer.ToJson(script);
            t.False("saving drops a stored password", json.Contains("hunter2"));
            t.Eq("and the node is cleaned too", node.Variable.Text + node.Variable.Expression, "");

            // read from a hand-edited file: dropped on load
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "at_pw_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                System.IO.File.WriteAllText(path, "{\"Version\":2,\"Actions\":[{\"Category\":\"Control\",\"Control\":\"Variable\",\"Variable\":{\"Id\":\"pw\",\"Type\":\"Password\",\"Text\":\"hunter2\"}}]}");
                var loaded = ScriptSerializer.Load(path);
                t.Eq("a password in a file is dropped when it is read", loaded.Actions[0].Variable.Text, "");
            }
            finally { try { System.IO.File.Delete(path); } catch (System.IO.IOException) { } }

            // changing a String that has a value into a Password in the editor keeps no value
            var form = new MainForm { HotKeysEnabled = false };
            var h = form.Handle;
            foreach (Control c in form.Controls) { var h2 = c.Handle; }
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var actions = (List<ActionNode>)typeof(MainForm).GetField("_script", flags).GetValue(form).GetType().GetProperty("Actions").GetValue(typeof(MainForm).GetField("_script", flags).GetValue(form));
            actions.Clear();
            actions.Add(ActionNode.NewVariable(new VariableSettings { Id = "s", Type = VariableType.String, Text = "secret text" }));
            typeof(MainForm).GetMethod("RefreshList", flags, null, Type.EmptyTypes, null).Invoke(form, null);
            var list = (ListView)typeof(MainForm).GetField("_list", flags).GetValue(form);
            list.Items[0].Selected = true;
            typeof(MainForm).GetMethod("LoadSelectedIntoEditor", flags).Invoke(form, null);
            ((ComboBox)typeof(MainForm).GetField("_varType", flags).GetValue(form)).SelectedItem = VariableType.Password;
            var built = (ActionNode)typeof(MainForm).GetMethod("BuildNode", flags).Invoke(form, null);
            t.Eq("the editor builds a Password with no text", built.Variable.Text + built.Variable.Expression, "");
            t.Eq("it is a Password", built.Variable.Type, VariableType.Password);
        }

        // ---------------------------------------------------------------- Start Selected remembers the last values

        private static VariableNeed Need(string name, VariableType type, object def = null)
        {
            return new VariableNeed { Name = name, Type = type, Default = def };
        }

        /// <summary>The text boxes of the dialog, by variable name.</summary>
        private static Dictionary<string, string> Boxes(VariableInputForm form)
        {
            var rows = (System.Collections.IList)typeof(VariableInputForm).GetField("_rows", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (object row in rows)
            {
                string name = (string)row.GetType().GetField("Name").GetValue(row);
                var box = (TextBox)row.GetType().GetField("Text").GetValue(row);
                result[name] = box.Text;
            }
            return result;
        }

        private static void Enter(VariableInputForm form, string name, string text)
        {
            var rows = (System.Collections.IList)typeof(VariableInputForm).GetField("_rows", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            foreach (object row in rows)
                if ((string)row.GetType().GetField("Name").GetValue(row) == name) ((TextBox)row.GetType().GetField("Text").GetValue(row)).Text = text;
        }

        private static void Accept(VariableInputForm form)
        {
            typeof(VariableInputForm).GetMethod("Accept", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
        }

        [Suite("variables.remembered-values")]
        public static void Remembered(T t)
        {
            // the example: run 1 has a String, b Number, c Number
            Dictionary<string, Tuple<string, object>> memory = null;
            using (var one = new VariableInputForm(new List<VariableNeed> { Need("a", VariableType.String), Need("b", VariableType.Number), Need("c", VariableType.Number) }, memory))
            {
                Enter(one, "a", "hello"); Enter(one, "b", "5"); Enter(one, "c", "8");
                Accept(one);
                memory = one.Entered;
            }
            t.Eq("run 1 remembers three variables", memory.Count, 3);

            // run 2 has a String and b String: a is filled in, b is not (another type)
            using (var two = new VariableInputForm(new List<VariableNeed> { Need("a", VariableType.String), Need("b", VariableType.String) }, memory))
            {
                var boxes = Boxes(two);
                t.Eq("same name and type: the value comes back", boxes["a"], "hello");
                t.Eq("same name, other type: nothing", boxes["b"], "");
                Enter(two, "b", "text");
                Accept(two);
                memory = two.Entered;
            }

            // run 3 has c Number: it was in run 1, but only the run before counts
            using (var three = new VariableInputForm(new List<VariableNeed> { Need("c", VariableType.Number) }, memory))
                t.Eq("only the run before is remembered", Boxes(three)["c"], "");

            // names are not case sensitive, and a value that was edited is what is remembered
            using (var four = new VariableInputForm(new List<VariableNeed> { Need("A", VariableType.String) }, new Dictionary<string, Tuple<string, object>>(StringComparer.OrdinalIgnoreCase) { { "a", Tuple.Create("String", (object)"hi") } }))
                t.Eq("the name is not case sensitive", Boxes(four)["A"], "hi");

            // the remembered value wins over the default taken from the script
            using (var five = new VariableInputForm(new List<VariableNeed> { Need("n", VariableType.Number, 1.0) }, new Dictionary<string, Tuple<string, object>> { { "n", Tuple.Create("Number", (object)7.0) } }))
                t.Eq("remembered value over the default", Boxes(five)["n"], "7");
            using (var six = new VariableInputForm(new List<VariableNeed> { Need("n", VariableType.Number, 1.0) }, null))
                t.Eq("no memory: the default of the script", Boxes(six)["n"], "1");

            // a password is never remembered or offered again
            Dictionary<string, Tuple<string, object>> afterPassword;
            using (var seven = new VariableInputForm(new List<VariableNeed> { Need("pw", VariableType.Password), Need("x", VariableType.String) }, null))
            {
                Enter(seven, "pw", "top-secret"); Enter(seven, "x", "plain");
                Accept(seven);
                afterPassword = seven.Entered;
                t.Eq("the password is handed to the run", seven.Values["pw"], "top-secret");
                t.Eq("and counted as a secret to hide", string.Join(",", seven.Secrets), "top-secret");
            }
            t.False("a password is not remembered", afterPassword.ContainsKey("pw"));
            t.True("the other value is", afterPassword.ContainsKey("x"));
            using (var eight = new VariableInputForm(new List<VariableNeed> { Need("pw", VariableType.Password) }, new Dictionary<string, Tuple<string, object>> { { "pw", Tuple.Create("Password", (object)"old") } }))
                t.Eq("even if a password were in the memory it is not offered", Boxes(eight)["pw"], "");

            // a Clipboard variable defined elsewhere is asked for as text
            using (var nine = new VariableInputForm(new List<VariableNeed> { Need("clip", VariableType.Clipboard) }, null))
            {
                Enter(nine, "clip", "from here");
                Accept(nine);
                t.Eq("a Clipboard variable can be given as text", nine.Values["clip"], "from here");
            }
        }

        [Suite("variables.editor")]
        public static void EditorKinds(T t)
        {
            // Clipboard and Password go in and out of the editor
            var form = new MainForm { HotKeysEnabled = false };
            var h = form.Handle;
            foreach (Control c in form.Controls) { var h2 = c.Handle; }
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var script = typeof(MainForm).GetField("_script", flags).GetValue(form);
            var actions = (List<ActionNode>)script.GetType().GetProperty("Actions").GetValue(script);
            var list = (ListView)typeof(MainForm).GetField("_list", flags).GetValue(form);
            foreach (VariableType type in new[] { VariableType.Clipboard, VariableType.Password })
            {
                actions.Clear();
                actions.Add(ActionNode.NewVariable(new VariableSettings { Id = "v", Type = type }));
                typeof(MainForm).GetMethod("RefreshList", flags, null, Type.EmptyTypes, null).Invoke(form, null);
                list.SelectedItems.Clear();
                list.Items[0].Selected = true;
                typeof(MainForm).GetMethod("LoadSelectedIntoEditor", flags).Invoke(form, null);
                var back = (ActionNode)typeof(MainForm).GetMethod("BuildNode", flags).Invoke(form, null);
                t.Eq(type + " round trip: type", back.Variable.Type, type);
                t.Eq(type + " round trip: id", back.Variable.Id, "v");
                t.False(type + " round trip: no expression", back.Variable.UseExpression);
            }
        }
    }
}
