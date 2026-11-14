using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutomationTest.Core.Expressions
{
    /// <summary>
    /// Small expression language used by Variable, Condition, Mouse and Text actions.
    /// Variables are written {name}; inside braces a bare name also works and properties are read with a dot:
    /// {img.Position.X + 1}. {1,1} is a Position and {x,y,w,h} a Range. Operators: + - * / %, = == != &lt; &lt;= &gt; &gt;=,
    /// &amp;&amp; || !, parentheses. Text in "double" or 'single' quotes; true, false, now, today. All number and date
    /// text uses the invariant culture, so it behaves the same on every machine.
    /// </summary>
    public static class Expression
    {
        public static readonly string[] ReservedWords = { "true", "false", "now", "today" };

        /// <summary>Whether the text can be used as a variable ID (letters, digits, underscore; not starting with a digit; not reserved).</summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || !(char.IsLetter(id[0]) || id[0] == '_')) return false;
            foreach (char c in id) if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return Array.IndexOf(ReservedWords, id.ToLowerInvariant()) < 0;
        }

        /// <exception cref="ExpressionException">The text is not a valid expression.</exception>
        public static void CheckSyntax(string text)
        {
            Parse(text, false);
        }

        /// <summary>Like CheckSyntax but also accepts a top-level comma list, as used for a Mouse position ("x, y").</summary>
        public static void CheckPositionSyntax(string text)
        {
            Parse(text, true);
        }

        public static object Evaluate(string text, VariableStore variables)
        {
            var nodes = Parse(text, false);
            return nodes[0].Eval(variables);
        }

        public static bool EvaluateBool(string text, VariableStore variables)
        {
            object value = Evaluate(text, variables);
            if (value is bool b) return b;
            throw new ExpressionException("\"" + text.Trim() + "\" must give true or false, but gives " + Format.TypeName(value) + ".");
        }

        public static string EvaluateString(string text, VariableStore variables)
        {
            return Format.Display(Evaluate(text, variables));
        }

        /// <summary>Evaluates a position: one expression giving a Position ("{a.Position} + {1,1}") or two numbers ("{a.Position.X + 1}, {a.Position.Y + 1}").</summary>
        public static PointValue EvaluatePoint(string text, VariableStore variables)
        {
            var nodes = Parse(text, true);
            if (nodes.Count == 1)
            {
                object value = nodes[0].Eval(variables);
                if (value is PointValue p) return p;
                if (value is ImageValue image) return image.Position; // a picture variable used directly means its position
                throw new ExpressionException("A position is needed, but \"" + text.Trim() + "\" gives " + Format.TypeName(value) + ".");
            }
            if (nodes.Count == 2)
                return new PointValue(Ops.ToNumber(nodes[0].Eval(variables)), Ops.ToNumber(nodes[1].Eval(variables)));
            throw new ExpressionException("A position has two numbers (x, y); found " + nodes.Count + ".");
        }

        /// <summary>The variables an expression reads (case-insensitive names).</summary>
        /// <param name="allowList">True for a Mouse position, which may be "x, y".</param>
        /// <exception cref="ExpressionException">The text is not a valid expression.</exception>
        public static ISet<string> ReferencedVariables(string text, bool allowList)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in Parse(text, allowList)) node.Collect(names);
            return names;
        }

        public static string ToDisplay(object value)
        {
            return Format.Display(value);
        }

        // ------------------------------------------------------------------ parsing

        private static List<Node> Parse(string text, bool allowList)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ExpressionException("The expression is empty.");
            var parser = new Parser(text);
            var nodes = parser.ParseTop(allowList);
            return nodes;
        }

        private abstract class Node
        {
            public abstract object Eval(VariableStore vars);

            /// <summary>Adds the names of the variables this expression reads.</summary>
            public virtual void Collect(ISet<string> names) { }
        }

        private sealed class Literal : Node
        {
            private readonly object _value;
            public Literal(object value) { _value = value; }
            public override object Eval(VariableStore vars) { return _value; }
        }

        private sealed class Now : Node
        {
            private readonly bool _dateOnly;
            public Now(bool dateOnly) { _dateOnly = dateOnly; }
            public override object Eval(VariableStore vars) { return _dateOnly ? DateTime.Today : DateTime.Now; }
        }

        private sealed class VarRef : Node
        {
            private readonly string _name;
            public VarRef(string name) { _name = name; }
            public override void Collect(ISet<string> names) { names.Add(_name); }
            public override object Eval(VariableStore vars)
            {
                if (!vars.TryGet(_name, out object value))
                    throw new ExpressionException("Variable {" + _name + "} has no value yet.");
                return value;
            }
        }

        private sealed class Member : Node
        {
            private readonly Node _target;
            private readonly string _name;
            public Member(Node target, string name) { _target = target; _name = name; }
            public override void Collect(ISet<string> names) { _target.Collect(names); }
            public override object Eval(VariableStore vars) { return Ops.GetMember(_target.Eval(vars), _name); }
        }

        /// <summary>{t[row, column]}: one value of a table.</summary>
        private sealed class Index : Node
        {
            private readonly Node _target, _row, _column;
            public Index(Node target, Node row, Node column, int pos) { _target = target; _row = row; _column = column; }
            public override void Collect(ISet<string> names) { _target.Collect(names); _row.Collect(names); _column.Collect(names); }
            public override object Eval(VariableStore vars)
            {
                object target = _target.Eval(vars);
                if (!(target is TableValue table)) throw new ExpressionException("[row, column] reads a table, but the value is " + Format.TypeName(target) + ".");
                double r = Ops.ToNumber(_row.Eval(vars)), c = Ops.ToNumber(_column.Eval(vars));
                return table.Get((int)Math.Round(r), (int)Math.Round(c));
            }
        }

        private sealed class Unary : Node
        {
            private readonly char _op;
            private readonly Node _operand;
            public Unary(char op, Node operand) { _op = op; _operand = operand; }
            public override void Collect(ISet<string> names) { _operand.Collect(names); }
            public override object Eval(VariableStore vars)
            {
                object v = _operand.Eval(vars);
                if (_op == '!')
                {
                    if (v is bool b) return !b;
                    throw new ExpressionException("! needs true or false, not " + Format.TypeName(v) + ".");
                }
                if (v is PointValue p) return new PointValue(-p.X, -p.Y);
                return -Ops.ToNumber(v);
            }
        }

        private sealed class Binary : Node
        {
            private readonly string _op;
            private readonly Node _left, _right;
            public Binary(string op, Node left, Node right) { _op = op; _left = left; _right = right; }
            public override void Collect(ISet<string> names) { _left.Collect(names); _right.Collect(names); }

            public override object Eval(VariableStore vars)
            {
                if (_op == "&&" || _op == "||")
                {
                    bool l = Ops.ToBool(_left.Eval(vars), _op);
                    if (_op == "&&" && !l) return false; // short-circuit
                    if (_op == "||" && l) return true;
                    return Ops.ToBool(_right.Eval(vars), _op);
                }
                return Ops.Apply(_op, _left.Eval(vars), _right.Eval(vars));
            }
        }

        private sealed class Tuple : Node
        {
            private readonly List<Node> _items;
            public Tuple(List<Node> items) { _items = items; }
            public override void Collect(ISet<string> names) { foreach (var item in _items) item.Collect(names); }
            public override object Eval(VariableStore vars)
            {
                if (_items.Count == 2) return new PointValue(Ops.ToNumber(_items[0].Eval(vars)), Ops.ToNumber(_items[1].Eval(vars)));
                return new RangeValue(Ops.ToNumber(_items[0].Eval(vars)), Ops.ToNumber(_items[1].Eval(vars)), Ops.ToNumber(_items[2].Eval(vars)), Ops.ToNumber(_items[3].Eval(vars)));
            }
        }

        private enum T { Number, String, Ident, Op, End }

        private struct Token
        {
            public T Kind;
            public string Text;
            public double Number;
            public int Pos;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private readonly List<Token> _tokens = new List<Token>();
            private int _i;
            private int _braces;

            public Parser(string text)
            {
                _s = text;
                Tokenize();
            }

            private void Fail(string message, int pos)
            {
                throw new ExpressionException(message + " (at position " + (pos + 1) + " of \"" + _s.Trim() + "\")");
            }

            private void Tokenize()
            {
                int i = 0;
                while (i < _s.Length)
                {
                    char c = _s[i];
                    if (char.IsWhiteSpace(c)) { i++; continue; }
                    int start = i;
                    if (char.IsDigit(c) || (c == '.' && i + 1 < _s.Length && char.IsDigit(_s[i + 1])))
                    {
                        while (i < _s.Length && (char.IsDigit(_s[i]) || _s[i] == '.')) i++;
                        string num = _s.Substring(start, i - start);
                        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) Fail("Bad number \"" + num + "\"", start);
                        _tokens.Add(new Token { Kind = T.Number, Number = value, Text = num, Pos = start });
                    }
                    else if (c == '"' || c == '\'')
                    {
                        var sb = new StringBuilder();
                        i++;
                        bool closed = false;
                        while (i < _s.Length)
                        {
                            if (_s[i] == '\\' && i + 1 < _s.Length) { sb.Append(_s[i + 1]); i += 2; continue; }
                            if (_s[i] == c) { closed = true; i++; break; }
                            sb.Append(_s[i++]);
                        }
                        if (!closed) Fail("Text is not closed", start);
                        _tokens.Add(new Token { Kind = T.String, Text = sb.ToString(), Pos = start });
                    }
                    else if (char.IsLetter(c) || c == '_')
                    {
                        while (i < _s.Length && (char.IsLetterOrDigit(_s[i]) || _s[i] == '_')) i++;
                        _tokens.Add(new Token { Kind = T.Ident, Text = _s.Substring(start, i - start), Pos = start });
                    }
                    else
                    {
                        string two = i + 1 < _s.Length ? _s.Substring(i, 2) : "";
                        if (two == "&&" || two == "||" || two == "==" || two == "!=" || two == "<=" || two == ">=")
                        {
                            _tokens.Add(new Token { Kind = T.Op, Text = two, Pos = start });
                            i += 2;
                        }
                        else if ("+-*/%()<>=!{},.[]".IndexOf(c) >= 0)
                        {
                            _tokens.Add(new Token { Kind = T.Op, Text = c.ToString(), Pos = start });
                            i++;
                        }
                        else Fail("Unexpected character '" + c + "'", start);
                    }
                }
                _tokens.Add(new Token { Kind = T.End, Text = "", Pos = _s.Length });
            }

            private Token Cur { get { return _tokens[_i]; } }

            private bool IsOp(string op) { return Cur.Kind == T.Op && Cur.Text == op; }

            private bool Accept(string op)
            {
                if (!IsOp(op)) return false;
                _i++;
                return true;
            }

            public List<Node> ParseTop(bool allowList)
            {
                var nodes = new List<Node> { ParseOr() };
                while (allowList && Accept(",")) nodes.Add(ParseOr());
                if (Cur.Kind != T.End)
                    Fail("Unexpected \"" + Cur.Text + "\"" + (IsOp(",") ? " (a comma is only allowed inside { } or between the two numbers of a position)" : ""), Cur.Pos);
                return nodes;
            }

            private Node ParseOr()
            {
                var left = ParseAnd();
                while (IsOp("||")) { _i++; left = new Binary("||", left, ParseAnd()); }
                return left;
            }

            private Node ParseAnd()
            {
                var left = ParseEquality();
                while (IsOp("&&")) { _i++; left = new Binary("&&", left, ParseEquality()); }
                return left;
            }

            private Node ParseEquality()
            {
                var left = ParseRelational();
                while (IsOp("=") || IsOp("==") || IsOp("!="))
                {
                    string op = Cur.Text == "!=" ? "!=" : "=";
                    _i++;
                    left = new Binary(op, left, ParseRelational());
                }
                return left;
            }

            private Node ParseRelational()
            {
                var left = ParseAdditive();
                while (IsOp("<") || IsOp("<=") || IsOp(">") || IsOp(">="))
                {
                    string op = Cur.Text;
                    _i++;
                    left = new Binary(op, left, ParseAdditive());
                }
                return left;
            }

            private Node ParseAdditive()
            {
                var left = ParseMultiplicative();
                while (IsOp("+") || IsOp("-"))
                {
                    string op = Cur.Text;
                    _i++;
                    left = new Binary(op, left, ParseMultiplicative());
                }
                return left;
            }

            private Node ParseMultiplicative()
            {
                var left = ParseUnary();
                while (IsOp("*") || IsOp("/") || IsOp("%"))
                {
                    string op = Cur.Text;
                    _i++;
                    left = new Binary(op, left, ParseUnary());
                }
                return left;
            }

            private Node ParseUnary()
            {
                if (IsOp("!")) { _i++; return new Unary('!', ParseUnary()); }
                if (IsOp("-")) { _i++; return new Unary('-', ParseUnary()); }
                return ParsePostfix();
            }

            private Node ParsePostfix()
            {
                var node = ParsePrimary();
                while (IsOp(".") || IsOp("["))
                {
                    if (IsOp("["))
                    {
                        int open = Cur.Pos;
                        _i++;
                        var row = ParseOr();
                        if (!Accept(",")) Fail("A table needs a row and a column: [row, column]", Cur.Pos);
                        var column = ParseOr();
                        if (!Accept("]")) Fail("Missing ]", Cur.Pos);
                        node = new Index(node, row, column, open);
                        continue;
                    }
                    _i++;
                    if (Cur.Kind != T.Ident) Fail("A property name is expected after '.'", Cur.Pos);
                    node = new Member(node, Cur.Text);
                    _i++;
                }
                return node;
            }

            private Node ParsePrimary()
            {
                var t = Cur;
                switch (t.Kind)
                {
                    case T.Number:
                        _i++;
                        return new Literal(t.Number);
                    case T.String:
                        _i++;
                        return new Literal(t.Text);
                    case T.Ident:
                        _i++;
                        switch (t.Text.ToLowerInvariant())
                        {
                            case "true": return new Literal(true);
                            case "false": return new Literal(false);
                            case "now": return new Now(false);
                            case "today": return new Now(true);
                        }
                        if (_braces > 0) return new VarRef(t.Text);
                        Fail("\"" + t.Text + "\" is not known; write {" + t.Text + "} to use a variable, or put text in quotes", t.Pos);
                        return null;
                    case T.Op:
                        if (t.Text == "(")
                        {
                            _i++;
                            var inner = ParseOr();
                            if (!Accept(")")) Fail("Missing )", Cur.Pos);
                            return inner;
                        }
                        if (t.Text == "{")
                        {
                            _i++;
                            _braces++;
                            var items = new List<Node> { ParseOr() };
                            while (Accept(",")) items.Add(ParseOr());
                            _braces--;
                            if (!Accept("}")) Fail("Missing }", Cur.Pos);
                            if (items.Count == 1) return items[0];
                            if (items.Count == 2 || items.Count == 4) return new Tuple(items);
                            Fail("{ } holds one expression, two numbers (a position) or four (a range)", t.Pos);
                        }
                        break;
                }
                Fail(t.Kind == T.End ? "The expression ends too early" : "Unexpected \"" + t.Text + "\"", t.Pos);
                return null;
            }
        }

        // ------------------------------------------------------------------ operations

        private static class Ops
        {
            public static double ToNumber(object v)
            {
                if (v is double d) return d;
                if (v is string s && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return parsed;
                throw new ExpressionException("A number is needed, but the value is " + Format.TypeName(v) + (v is string ? " \"" + v + "\"" : "") + ".");
            }

            public static bool ToBool(object v, string op)
            {
                if (v is bool b) return b;
                throw new ExpressionException(op + " needs true or false, not " + Format.TypeName(v) + ".");
            }

            private static bool TryDate(object v, out DateTime date)
            {
                if (v is DateTime d) { date = d; return true; }
                if (v is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
                date = default(DateTime);
                return false;
            }

            public static object GetMember(object target, string name)
            {
                string n = name.ToLowerInvariant();
                switch (target)
                {
                    case PointValue p:
                        if (n == "x") return p.X;
                        if (n == "y") return p.Y;
                        break;
                    case SizeValue z:
                        if (n == "width") return z.Width;
                        if (n == "height") return z.Height;
                        break;
                    case RangeValue r:
                        if (n == "x") return r.X;
                        if (n == "y") return r.Y;
                        if (n == "width") return r.Width;
                        if (n == "height") return r.Height;
                        if (n == "position") return r.Position;
                        if (n == "size") return r.Size;
                        break;
                    case TableValue table:
                        if (n == "rows") return (double)table.Rows;
                        if (n == "columns") return (double)table.Columns;
                        break;
                    case CellValue c:
                        if (n == "found") return c.Found;
                        if (n == "row" || n == "column" || n == "address")
                        {
                            if (!c.Found) throw new ExpressionException("The text was not found, so it has no " + name + "; check {...}.Found first.");
                            return n == "row" ? (object)(double)c.Row : n == "column" ? (double)c.Column : c.Address;
                        }
                        break;
                    case ImageValue i:
                        if (n == "found") return i.Found;
                        if (n == "position" || n == "size")
                        {
                            if (!i.Found) throw new ExpressionException("The picture was not found, so it has no " + name + "; check {...}.Found first.");
                            return n == "position" ? (object)i.Position : i.Size;
                        }
                        break;
                }
                throw new ExpressionException(Format.TypeName(target) + " has no property \"" + name + "\".");
            }

            public static object Apply(string op, object l, object r)
            {
                switch (op)
                {
                    case "+": return Add(l, r);
                    case "-": return Subtract(l, r);
                    case "*":
                        if (l is PointValue pm) return new PointValue(pm.X * ToNumber(r), pm.Y * ToNumber(r));
                        return ToNumber(l) * ToNumber(r);
                    case "/":
                    {
                        double d = ToNumber(r);
                        if (d == 0) throw new ExpressionException("Division by zero.");
                        if (l is PointValue pd) return new PointValue(pd.X / d, pd.Y / d);
                        return ToNumber(l) / d;
                    }
                    case "%":
                    {
                        double d = ToNumber(r);
                        if (d == 0) throw new ExpressionException("Division by zero.");
                        return ToNumber(l) % d;
                    }
                    case "=": return AreEqual(l, r);
                    case "!=": return !AreEqual(l, r);
                    default: return Compare(op, l, r);
                }
            }

            private static object Add(object l, object r)
            {
                if (l is string || r is string) return Format.Display(l) + Format.Display(r);
                if (l is PointValue a && r is PointValue b) return new PointValue(a.X + b.X, a.Y + b.Y);
                if (l is DateTime dl && r is double dr) return dl.AddDays(dr);
                if (l is double nl && r is DateTime dr2) return dr2.AddDays(nl);
                return ToNumber(l) + ToNumber(r);
            }

            private static object Subtract(object l, object r)
            {
                if (l is PointValue a && r is PointValue b) return new PointValue(a.X - b.X, a.Y - b.Y);
                if (l is DateTime d1 && r is DateTime d2) return (d1 - d2).TotalDays;
                if (l is DateTime dl && r is double n) return dl.AddDays(-n);
                return ToNumber(l) - ToNumber(r);
            }

            private static bool AreEqual(object l, object r)
            {
                if (l is PointValue a && r is PointValue b) return a.X == b.X && a.Y == b.Y;
                if (l is bool lb && r is bool rb) return lb == rb;
                if (l is DateTime || r is DateTime)
                    return TryDate(l, out DateTime x) && TryDate(r, out DateTime y) && x == y;
                if (l is string ls && r is string rs) return string.Equals(ls, rs, StringComparison.Ordinal);
                if (l is double || r is double)
                {
                    try { return ToNumber(l) == ToNumber(r); }
                    catch (ExpressionException) { return false; }
                }
                return false;
            }

            private static object Compare(string op, object l, object r)
            {
                int c;
                if (l is DateTime || r is DateTime)
                {
                    if (!TryDate(l, out DateTime x) || !TryDate(r, out DateTime y))
                        throw new ExpressionException("Cannot compare " + Format.TypeName(l) + " with " + Format.TypeName(r) + ".");
                    c = x.CompareTo(y);
                }
                else if (l is string ls && r is string rs && !IsNumeric(ls, rs))
                    c = string.CompareOrdinal(ls, rs);
                else
                    c = ToNumber(l).CompareTo(ToNumber(r));

                switch (op)
                {
                    case "<": return c < 0;
                    case "<=": return c <= 0;
                    case ">": return c > 0;
                    default: return c >= 0;
                }
            }

            private static bool IsNumeric(string a, string b)
            {
                return double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            }
        }
    }
}
