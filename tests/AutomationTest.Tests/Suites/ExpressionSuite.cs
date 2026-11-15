using System;
using AutomationTest.Core.Expressions;

namespace AutomationTest.Tests.Suites
{
    internal static class ExpressionSuite
    {
        private static VariableStore Vars(params object[] pairs)
        {
            var v = new VariableStore();
            for (int i = 0; i < pairs.Length; i += 2) v.Set((string)pairs[i], pairs[i + 1]);
            return v;
        }

        [Suite("expressions")]
        public static void Run(T t)
        {
            var none = new VariableStore();

            // numbers and precedence
            t.Eq("1 + 2 * 3", Expression.Evaluate("1 + 2 * 3", none), 7.0);
            t.Eq("parentheses", Expression.Evaluate("(1 + 2) * 3", none), 9.0);
            t.Eq("division", Expression.Evaluate("7 / 2", none), 3.5);
            t.Eq("modulo", Expression.Evaluate("7 % 4", none), 3.0);
            t.Eq("unary minus", Expression.Evaluate("-3 + 5", none), 2.0);

            // variables, written bare inside braces
            var v = Vars("a", 5.0, "name", "Ann", "flag", true);
            t.Eq("variable", Expression.Evaluate("{a} + 1", v), 6.0);
            t.Eq("expression inside braces", Expression.Evaluate("{a * 2}", v), 10.0);
            t.Eq("variables are not case sensitive", Expression.Evaluate("{A}", v), 5.0);

            // strings
            t.Eq("concatenation", Expression.Evaluate("\"hi \" + {name}", v), "hi Ann");
            t.Eq("single quotes", Expression.Evaluate("'x' + 'y'", none), "xy");
            t.Eq("number joins a string", Expression.EvaluateString("\"n=\" + 3", none), "n=3");

            // comparison and logic
            t.True("greater", Expression.EvaluateBool("{a} > 3", v));
            t.False("not equal", Expression.EvaluateBool("{a} != 5", v));
            t.True("equal", Expression.EvaluateBool("{a} == 5", v));
            t.True("single = compares too", Expression.EvaluateBool("{a} = 5", v));
            t.True("and", Expression.EvaluateBool("{a} > 1 && {flag}", v));
            t.True("or", Expression.EvaluateBool("{a} > 100 || {flag}", v));
            t.True("not", Expression.EvaluateBool("!({a} > 100)", v));
            t.True("true literal", Expression.EvaluateBool("true", none));
            t.False("false literal", Expression.EvaluateBool("false", none));

            // short circuit: the right side is not evaluated, so an unknown variable there is fine
            t.False("and short-circuits", Expression.EvaluateBool("false && {missing}", none));
            t.True("or short-circuits", Expression.EvaluateBool("true || {missing}", none));

            // positions and members
            var p = Vars("p", new PointValue(10, 20));
            t.Eq("position X", Expression.Evaluate("{p.X}", p), 10.0);
            t.Eq("position arithmetic", Expression.Evaluate("{p.Y + 1}", p), 21.0);
            var point = Expression.EvaluatePoint("{p} + {1,2}", p);
            t.Eq("position sum X", Convert.ToDouble(point.X), 11.0);
            t.Eq("position sum Y", Convert.ToDouble(point.Y), 22.0);

            // tables
            var table = new object[2, 2] { { 1.0, "b" }, { 3.0, "d" } };
            var tv = Vars("t", new TableValue(table));
            t.Eq("table rows", Expression.Evaluate("{t.Rows}", tv), 2.0);
            t.Eq("table columns", Expression.Evaluate("{t.Columns}", tv), 2.0);
            t.Eq("table cell", Expression.Evaluate("{t[2, 1]}", tv), 3.0);
            t.Eq("table cell by expression", Expression.Evaluate("{t[{a} - 4, 2]}", Vars("t", new TableValue(table), "a", 5.0)), "b");
            t.Throws<ExpressionException>("table out of range", () => Expression.Evaluate("{t[3, 1]}", tv), "row");

            // errors
            t.Throws<ExpressionException>("unknown variable names it", () => Expression.Evaluate("{nope} + 1", none), "nope");
            t.Throws<ExpressionException>("syntax error", () => Expression.CheckSyntax("1 +"), "");
            t.Throws<ExpressionException>("bool needed", () => Expression.EvaluateBool("5", none), "");

            // ids
            t.True("valid id", Expression.IsValidId("abc_1"));
            t.False("id starts with a digit", Expression.IsValidId("1abc"));
            t.False("id true", Expression.IsValidId("true"));
            t.False("id now", Expression.IsValidId("now"));
            t.False("empty id", Expression.IsValidId(""));

            // which variables an expression reads (Start Selected asks for them)
            var names = new System.Collections.Generic.List<string>(Expression.ReferencedVariables("{a} + {b.X} > {c}", false));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            t.Eq("referenced variables", string.Join(",", names), "a,b,c");

            // numbers use the invariant culture whatever the machine language is
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("vi-VN");
                t.Eq("decimal point in vi-VN", Expression.Evaluate("1.5 + 1", none), 2.5);
                t.Eq("number text in vi-VN", Expression.EvaluateString("1.5", none), "1.5");
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }
    }
}
