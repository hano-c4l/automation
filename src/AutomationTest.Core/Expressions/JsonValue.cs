using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AutomationTest.Core.Expressions
{
    /// <summary>
    /// A piece of JSON that an expression can look into: {r.Json.data[0].name}. An object is read with .name (or ["name"] for a
    /// name with spaces or dashes), an array with [0] (counted from 0, like JSON itself) and .Count; a text, number, true/false
    /// that is reached this way becomes an ordinary String, Number or Boolean (null becomes an empty String).
    /// </summary>
    public sealed class JsonValue
    {
        private enum Kind { Null, Boolean, Number, String, Array, Object }

        private readonly Kind _kind;
        private readonly object _scalar;
        private readonly List<JsonValue> _items;
        private readonly List<KeyValuePair<string, JsonValue>> _properties;

        private JsonValue(Kind kind, object scalar, List<JsonValue> items, List<KeyValuePair<string, JsonValue>> properties)
        {
            _kind = kind; _scalar = scalar; _items = items; _properties = properties;
        }

        /// <summary>The text is not JSON.</summary>
        public static bool TryParse(string text, out JsonValue value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                var parser = new Parser(text);
                value = parser.ParseDocument();
                return true;
            }
            catch (FormatException) { return false; }
        }

        public bool IsContainer { get { return _kind == Kind.Array || _kind == Kind.Object; } }

        /// <summary>How many items an array has, or how many properties an object has.</summary>
        public int Count { get { return _kind == Kind.Array ? _items.Count : _kind == Kind.Object ? _properties.Count : 0; } }

        /// <summary>What an expression sees: a String, Number or Boolean for a plain value, this object for an array or an object.</summary>
        public object ToExpressionValue()
        {
            switch (_kind)
            {
                case Kind.Null: return "";
                case Kind.Boolean: return (bool)_scalar;
                case Kind.Number: return (double)_scalar;
                case Kind.String: return (string)_scalar;
                default: return this;
            }
        }

        /// <summary>.name of an object (the exact name first, then any letter case), .Count of an array or an object.</summary>
        public object GetMember(string name)
        {
            if (_kind == Kind.Object)
            {
                foreach (var p in _properties) if (p.Key == name) return p.Value.ToExpressionValue();
                foreach (var p in _properties) if (string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)) return p.Value.ToExpressionValue();
                if (string.Equals(name, "Count", StringComparison.OrdinalIgnoreCase)) return (double)_properties.Count;
                throw new ExpressionException("The JSON object has no \"" + name + "\"" + (_properties.Count == 0 ? " (it is empty)." : " (it has: " + string.Join(", ", _properties.Select(p => p.Key).Take(12)) + (_properties.Count > 12 ? ", ..." : "") + ")."));
            }
            if (_kind == Kind.Array)
            {
                if (string.Equals(name, "Count", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Length", StringComparison.OrdinalIgnoreCase)) return (double)_items.Count;
                throw new ExpressionException("A JSON array has no \"" + name + "\": use [0] for an item, or .Count for how many.");
            }
            throw new ExpressionException("A JSON " + Describe() + " has no \"" + name + "\".");
        }

        /// <summary>[0] of an array (from 0), ["name"] of an object.</summary>
        public object GetIndex(object index)
        {
            if (_kind == Kind.Array)
            {
                if (!(index is double d)) throw new ExpressionException("An array is read with a number: [0] is the first item.");
                int i = (int)Math.Round(d);
                if (i < 0 || i >= _items.Count) throw new ExpressionException("The JSON array has " + _items.Count + " item(s), so there is no [" + i + "] (the first is [0]).");
                return _items[i].ToExpressionValue();
            }
            if (_kind == Kind.Object)
            {
                string key = index is string s ? s : Format.Display(index);
                foreach (var p in _properties) if (p.Key == key) return p.Value.ToExpressionValue();
                throw new ExpressionException("The JSON object has no [\"" + key + "\"]" + (_properties.Count == 0 ? " (it is empty)." : " (it has: " + string.Join(", ", _properties.Select(p => p.Key).Take(12)) + (_properties.Count > 12 ? ", ..." : "") + ")."));
            }
            throw new ExpressionException("A JSON " + Describe() + " cannot be read with [ ].");
        }

        private string Describe()
        {
            switch (_kind)
            {
                case Kind.Null: return "null";
                case Kind.Boolean: return "true/false";
                case Kind.Number: return "number";
                case Kind.String: return "text";
                case Kind.Array: return "array";
                default: return "object";
            }
        }

        /// <summary>The JSON text again (short, on one line).</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }

        private void Write(StringBuilder sb)
        {
            switch (_kind)
            {
                case Kind.Null: sb.Append("null"); break;
                case Kind.Boolean: sb.Append((bool)_scalar ? "true" : "false"); break;
                case Kind.Number: sb.Append(Format.Number((double)_scalar)); break;
                case Kind.String: Quote(sb, (string)_scalar); break;
                case Kind.Array:
                    sb.Append('[');
                    for (int i = 0; i < _items.Count; i++) { if (i > 0) sb.Append(','); _items[i].Write(sb); }
                    sb.Append(']');
                    break;
                default:
                    sb.Append('{');
                    for (int i = 0; i < _properties.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Quote(sb, _properties[i].Key);
                        sb.Append(':');
                        _properties[i].Value.Write(sb);
                    }
                    sb.Append('}');
                    break;
            }
        }

        private static void Quote(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (char c in text)
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

        // ------------------------------------------------------------------ parser

        private sealed class Parser
        {
            private readonly string _text;
            private int _i;
            private int _depth;

            public Parser(string text) { _text = text; }

            public JsonValue ParseDocument()
            {
                if (_text.Length > 0 && _text[0] == '﻿') _i = 1;
                var value = ParseValue();
                SkipWhite();
                if (_i != _text.Length) throw new FormatException("Unexpected text after the JSON.");
                return value;
            }

            private void SkipWhite()
            {
                while (_i < _text.Length && char.IsWhiteSpace(_text[_i])) _i++;
            }

            private JsonValue ParseValue()
            {
                SkipWhite();
                if (_i >= _text.Length) throw new FormatException("The JSON ends too soon.");
                char c = _text[_i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return new JsonValue(Kind.String, ParseString(), null, null);
                if (c == 't' && Take("true")) return new JsonValue(Kind.Boolean, true, null, null);
                if (c == 'f' && Take("false")) return new JsonValue(Kind.Boolean, false, null, null);
                if (c == 'n' && Take("null")) return new JsonValue(Kind.Null, null, null, null);
                return ParseNumber();
            }

            private bool Take(string word)
            {
                if (string.CompareOrdinal(_text, _i, word, 0, word.Length) != 0) throw new FormatException("Unexpected text in the JSON.");
                _i += word.Length;
                return true;
            }

            private JsonValue ParseObject()
            {
                if (++_depth > 200) throw new FormatException("The JSON is nested too deep.");
                _i++;
                var props = new List<KeyValuePair<string, JsonValue>>();
                SkipWhite();
                if (_i < _text.Length && _text[_i] == '}') { _i++; _depth--; return new JsonValue(Kind.Object, null, null, props); }
                while (true)
                {
                    SkipWhite();
                    if (_i >= _text.Length || _text[_i] != '"') throw new FormatException("A name is expected in the JSON object.");
                    string key = ParseString();
                    SkipWhite();
                    if (_i >= _text.Length || _text[_i] != ':') throw new FormatException("A : is expected after a name.");
                    _i++;
                    props.Add(new KeyValuePair<string, JsonValue>(key, ParseValue()));
                    SkipWhite();
                    if (_i < _text.Length && _text[_i] == ',') { _i++; continue; }
                    if (_i < _text.Length && _text[_i] == '}') { _i++; break; }
                    throw new FormatException("A , or } is expected in the JSON object.");
                }
                _depth--;
                return new JsonValue(Kind.Object, null, null, props);
            }

            private JsonValue ParseArray()
            {
                if (++_depth > 200) throw new FormatException("The JSON is nested too deep.");
                _i++;
                var items = new List<JsonValue>();
                SkipWhite();
                if (_i < _text.Length && _text[_i] == ']') { _i++; _depth--; return new JsonValue(Kind.Array, null, items, null); }
                while (true)
                {
                    items.Add(ParseValue());
                    SkipWhite();
                    if (_i < _text.Length && _text[_i] == ',') { _i++; continue; }
                    if (_i < _text.Length && _text[_i] == ']') { _i++; break; }
                    throw new FormatException("A , or ] is expected in the JSON array.");
                }
                _depth--;
                return new JsonValue(Kind.Array, null, items, null);
            }

            private string ParseString()
            {
                _i++; // the opening quote
                var sb = new StringBuilder();
                while (_i < _text.Length)
                {
                    char c = _text[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _text.Length) break;
                    char e = _text[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            int code;
                            if (_i + 4 > _text.Length || !int.TryParse(_text.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw new FormatException("A bad \\u in the JSON text.");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw new FormatException("A bad escape in the JSON text.");
                    }
                }
                throw new FormatException("A JSON text is not closed.");
            }

            private JsonValue ParseNumber()
            {
                int start = _i;
                while (_i < _text.Length && "+-0123456789.eE".IndexOf(_text[_i]) >= 0) _i++;
                double number;
                if (_i == start || !double.TryParse(_text.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    throw new FormatException("A value is expected in the JSON.");
                return new JsonValue(Kind.Number, number, null, null);
            }
        }
    }
}
