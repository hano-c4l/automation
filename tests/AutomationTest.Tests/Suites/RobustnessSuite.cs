using System;
using System.IO;
using System.Linq;
using System.Text;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Model;
using AutomationTest.Core.Web;

namespace AutomationTest.Tests.Suites
{
    /// <summary>Things that keep the program and the user's files whole when something goes wrong.</summary>
    internal static class RobustnessSuite
    {
        [Suite("robustness.safe-file")]
        public static void SafeFiles(T t)
        {
            string folder = Path.Combine(Path.GetTempPath(), "AutomationTest_safe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = Path.Combine(folder, "script.json");
                SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes("first"));
                t.Eq("a new file is written", File.ReadAllText(path), "first");
                SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes("second, longer"));
                t.Eq("a file that exists is replaced", File.ReadAllText(path), "second, longer");
                SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes("3"));
                t.Eq("also by something shorter", File.ReadAllText(path), "3");
                t.Eq("no temporary file is left behind", Directory.GetFiles(folder).Length, 1);

                // a script saved twice opens again and nothing else appears next to it
                var script = new AutomationScript();
                script.Actions.Add(ActionNode.NewVariable(new VariableSettings { Id = "a", Text = "1" }));
                ScriptSerializer.Save(script, path);
                ScriptSerializer.Save(script, path);
                t.Eq("a script saved over itself opens", ScriptSerializer.Load(path).Actions.Count, 1);
                t.Eq("and is the only file", Directory.GetFiles(folder).Length, 1);

                // a file that cannot be replaced says so and stays as it was
                File.SetAttributes(path, FileAttributes.ReadOnly);
                string before = File.ReadAllText(path);
                bool refused = false;
                try { SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes("x")); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { refused = true; }
                File.SetAttributes(path, FileAttributes.Normal);
                t.True("a read-only file is refused", refused);
                t.Eq("and keeps its content", File.ReadAllText(path), before);
                t.Eq("with no temporary file left", Directory.GetFiles(folder).Length, 1);
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        [Suite("robustness.json-depth")]
        public static void JsonDepth(T t)
        {
            t.True("ordinary nesting is read", MiniJson.Parse("{\"a\":[1,[2,[3,{\"b\":null}]]]}") != null);
            string deep = new string('[', 100000) + new string(']', 100000);
            t.Throws<FormatException>("a value nested 100000 deep is refused instead of ending the program", () => MiniJson.Parse(deep), "too deep");
            string fine = string.Concat(Enumerable.Repeat("[", 200)) + string.Concat(Enumerable.Repeat("]", 200));
            t.True("200 deep is still read", MiniJson.Parse(fine) != null);
        }

        [Suite("robustness.expression-cache")]
        public static void ExpressionCache(T t)
        {
            // the parsed form is kept; the values are not
            var v = new VariableStore();
            v.Set("a", 1.0);
            t.Eq("first time", Expression.Evaluate("{a} + 1", v), 2.0);
            v.Set("a", 10.0);
            t.Eq("the same text with another value", Expression.Evaluate("{a} + 1", v), 11.0);
            var other = new VariableStore();
            other.Set("a", "x");
            t.Eq("and with another store", Expression.Evaluate("{a} + 1", other), "x1");
            // "1, 2" is a position list but not a single expression: the two ways of reading a text do not mix
            t.Eq("a list is read as a position", Expression.EvaluatePoint("1, 2", v).Y, 2.0);
            t.Throws<ExpressionException>("the same text is still refused as one value", () => Expression.Evaluate("1, 2", v), "comma");
            t.Throws<ExpressionException>("a mistake fails every time", () => Expression.Evaluate("1 +", v), "");
            t.Throws<ExpressionException>("a mistake fails every time (again)", () => Expression.Evaluate("1 +", v), "");
            for (int i = 0; i < 5000; i++) Expression.Evaluate(i + " + 1", v); // more texts than are kept
            t.Eq("after many different texts", Expression.Evaluate("{a} + 1", v), 11.0);
        }
    }
}
