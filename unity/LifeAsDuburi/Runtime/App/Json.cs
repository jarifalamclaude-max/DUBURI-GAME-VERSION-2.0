// Minimal JSON for saved settings (PlayerPrefs strings), the same format the
// browser version keeps in localStorage. Parse gives Dictionary<string, object>,
// List<object>, string, double, bool or null; Stringify accepts those plus
// int/float/long, IDictionary and IEnumerable.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Duburi.Game
{
    public static class Json
    {
        // null when the text is not valid JSON
        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                var p = new Parser(text);
                var v = p.Value();
                p.SkipWs();
                return p.AtEnd ? v : null;
            }
            catch (FormatException) { return null; }
        }

        public static string Stringify(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        // ---- typed helpers for reading parsed objects
        public static Dictionary<string, object> Obj(object v) => v as Dictionary<string, object>;
        public static List<object> Arr(object v) => v as List<object>;
        public static string Str(object v, string fallback = null) => v as string ?? fallback;
        public static double Num(object v, double fallback = 0) => v is double d ? d : fallback;
        public static bool Bool(object v, bool fallback = false) => v is bool b ? b : fallback;
        public static object Get(Dictionary<string, object> o, string key) => o != null && o.TryGetValue(key, out var v) ? v : null;

        static void Write(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: WriteNumber(sb, d); break;
                case float f: WriteNumber(sb, f); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary dict:
                    {
                        sb.Append('{');
                        bool first = true;
                        foreach (DictionaryEntry e in dict)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                            sb.Append(':');
                            Write(sb, e.Value);
                        }
                        sb.Append('}');
                        break;
                    }
                case IEnumerable list:
                    {
                        sb.Append('[');
                        bool first = true;
                        foreach (var item in list)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            Write(sb, item);
                        }
                        sb.Append(']');
                        break;
                    }
                default: WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture)); break;
            }
        }

        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string text) { s = text; }
            public bool AtEnd => i >= s.Length;

            public void SkipWs() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            char Peek() { SkipWs(); if (i >= s.Length) throw new FormatException("end of input"); return s[i]; }

            public object Value()
            {
                char c = Peek();
                if (c == '{') return Object();
                if (c == '[') return Array();
                if (c == '"') return String();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                return Number();
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("expected " + word);
                i += word.Length;
            }

            Dictionary<string, object> Object()
            {
                var o = new Dictionary<string, object>();
                i++; // {
                if (Peek() == '}') { i++; return o; }
                while (true)
                {
                    if (Peek() != '"') throw new FormatException("expected key");
                    string key = String();
                    if (Peek() != ':') throw new FormatException("expected :");
                    i++;
                    o[key] = Value();
                    char c = Peek();
                    i++;
                    if (c == '}') return o;
                    if (c != ',') throw new FormatException("expected , or }");
                }
            }

            List<object> Array()
            {
                var a = new List<object>();
                i++; // [
                if (Peek() == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value());
                    char c = Peek();
                    i++;
                    if (c == ']') return a;
                    if (c != ',') throw new FormatException("expected , or ]");
                }
            }

            string String()
            {
                var sb = new StringBuilder();
                i++; // opening quote
                while (true)
                {
                    if (i >= s.Length) throw new FormatException("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw new FormatException("bad escape");
                    char e = s[i++];
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
                            if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw new FormatException("bad escape");
                    }
                }
            }

            double Number()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (start == i) throw new FormatException("unexpected character");
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new FormatException("bad number");
                return d;
            }
        }
    }
}
