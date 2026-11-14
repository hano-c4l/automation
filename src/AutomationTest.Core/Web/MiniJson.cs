using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutomationTest.Core.Web
{
    /// <summary>
    /// Small JSON reader and writer for the browser messages, whose shape varies (the Chrome DevTools Protocol replies are free-form).
    /// Objects are Dictionary&lt;string, object&gt;, arrays are object[], numbers are long or double, plus string, bool and null.
    /// </summary>
    public static class MiniJson
    {
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static object Parse(string text)
        {
            int i = 0;
            object value = ReadValue(text, ref i);
            SkipSpace(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after the JSON value.");
            return value;
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (var pair in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        WriteValue(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                case IFormattable n: sb.Append(n.ToString(null, CultureInfo.InvariantCulture)); break;
                default: WriteString(sb, value.ToString()); break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static void SkipSpace(string t, ref int i)
        {
            while (i < t.Length && char.IsWhiteSpace(t[i])) i++;
        }

        private static object ReadValue(string t, ref int i)
        {
            SkipSpace(t, ref i);
            if (i >= t.Length) throw new FormatException("Unexpected end of JSON.");
            char c = t[i];
            if (c == '{')
            {
                i++;
                var dict = new Dictionary<string, object>();
                SkipSpace(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return dict; }
                while (true)
                {
                    SkipSpace(t, ref i);
                    string key = ReadString(t, ref i);
                    SkipSpace(t, ref i);
                    Expect(t, ref i, ':');
                    dict[key] = ReadValue(t, ref i);
                    SkipSpace(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    Expect(t, ref i, '}');
                    return dict;
                }
            }
            if (c == '[')
            {
                i++;
                var list = new List<object>();
                SkipSpace(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return list.ToArray(); }
                while (true)
                {
                    list.Add(ReadValue(t, ref i));
                    SkipSpace(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    Expect(t, ref i, ']');
                    return list.ToArray();
                }
            }
            if (c == '"') return ReadString(t, ref i);
            if (Literal(t, ref i, "true")) return true;
            if (Literal(t, ref i, "false")) return false;
            if (Literal(t, ref i, "null")) return null;

            int start = i;
            while (i < t.Length && "+-0123456789.eE".IndexOf(t[i]) >= 0) i++;
            string number = t.Substring(start, i - start);
            if (number.Length == 0) throw new FormatException("Unexpected character '" + c + "' in JSON.");
            if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
            return double.Parse(number, CultureInfo.InvariantCulture);
        }

        private static bool Literal(string t, ref int i, string word)
        {
            if (string.CompareOrdinal(t, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        private static void Expect(string t, ref int i, char c)
        {
            if (i >= t.Length || t[i] != c) throw new FormatException("Expected '" + c + "' in JSON.");
            i++;
        }

        private static string ReadString(string t, ref int i)
        {
            Expect(t, ref i, '"');
            var sb = new StringBuilder();
            while (i < t.Length)
            {
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) break;
                char e = t[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > t.Length) throw new FormatException("Bad \\u escape in JSON.");
                        sb.Append((char)int.Parse(t.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break; // " \ /
                }
            }
            throw new FormatException("Unterminated string in JSON.");
        }
    }
}
