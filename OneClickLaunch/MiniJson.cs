using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OneClickLaunch
{
    /// <summary>
    /// A small JSON writer and reader for the history file. Unity's JsonUtility silently wrote
    /// the file without its list of entries (verified in game on 2026-09-22: the launch was
    /// remembered and the button shown, the file held only the version), and nothing else
    /// JSON-shaped ships with the game or BepInEx. Objects become Dictionary&lt;string, object&gt;,
    /// arrays List&lt;object&gt;, numbers long or double, and the rest string, bool or null.
    /// </summary>
    internal static class MiniJson
    {
        // ----------------------------------------------------------------------- write ----

        internal static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int depth)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> obj:
                    WriteObject(sb, obj, depth);
                    break;
                case IEnumerable<object> list:
                    WriteArray(sb, list, depth);
                    break;
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> obj, int depth)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in obj)
            {
                sb.Append(first ? "\n" : ",\n");
                first = false;
                Indent(sb, depth + 1);
                WriteString(sb, kv.Key);
                sb.Append(": ");
                WriteValue(sb, kv.Value, depth + 1);
            }
            if (!first)
            {
                sb.Append('\n');
                Indent(sb, depth);
            }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable<object> list, int depth)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in list)
            {
                sb.Append(first ? "\n" : ",\n");
                first = false;
                Indent(sb, depth + 1);
                WriteValue(sb, item, depth + 1);
            }
            if (!first)
            {
                sb.Append('\n');
                Indent(sb, depth);
            }
            sb.Append(']');
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------------ read ----

        /// <summary>Parses a document. Throws FormatException on malformed input.</summary>
        internal static object Read(string text)
        {
            var reader = new Reader(text);
            object value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw reader.Error("trailing characters");
            return value;
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _pos;

            public Reader(string text)
            {
                _text = text ?? "";
            }

            public bool AtEnd => _pos >= _text.Length;

            public FormatException Error(string what)
            {
                return new FormatException($"Bad JSON at {_pos}: {what}");
            }

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(_text[_pos])) _pos++;
            }

            private char Peek()
            {
                if (AtEnd) throw Error("unexpected end");
                return _text[_pos];
            }

            private void Expect(char c)
            {
                if (Peek() != c) throw Error($"expected '{c}'");
                _pos++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                char c = Peek();
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ReadWord("true");  return true;
                    case 'f': ReadWord("false"); return false;
                    case 'n': ReadWord("null");  return null;
                    default:
                        if (c == '-' || char.IsDigit(c)) return ReadNumber();
                        throw Error($"unexpected '{c}'");
                }
            }

            private void ReadWord(string word)
            {
                if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0) throw Error($"expected {word}");
                _pos += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var obj = new Dictionary<string, object>();
                Expect('{');
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _pos++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    string key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    obj[key] = ReadValue();
                    SkipWhitespace();
                    char c = Peek();
                    _pos++;
                    if (c == '}') return obj;
                    if (c != ',') throw Error("expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                Expect('[');
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    return list;
                }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWhitespace();
                    char c = Peek();
                    _pos++;
                    if (c == ']') return list;
                    if (c != ',') throw Error("expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    char c = Peek();
                    _pos++;
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    char e = Peek();
                    _pos++;
                    switch (e)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default:
                            throw Error($"bad escape '\\{e}'");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = _pos;
                if (Peek() == '-') _pos++;
                while (!AtEnd && (char.IsDigit(_text[_pos]) || "+-.eE".IndexOf(_text[_pos]) >= 0)) _pos++;
                string token = _text.Substring(start, _pos - start);
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
                throw Error($"bad number '{token}'");
            }
        }

        // --------------------------------------------------------------------- helpers ----

        internal static string GetString(IDictionary<string, object> obj, string key)
        {
            return obj.TryGetValue(key, out object v) && v != null ? v.ToString() : null;
        }

        internal static bool GetBool(IDictionary<string, object> obj, string key)
        {
            return obj.TryGetValue(key, out object v) && v is bool b && b;
        }

        internal static long GetLong(IDictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object v) || v == null) return 0;
            switch (v)
            {
                case long l: return l;
                case double d: return (long)d;
                default: return long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long p) ? p : 0;
            }
        }
    }
}
