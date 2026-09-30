// Minimal JSON reader/writer (objects -> Dictionary<string, object>, arrays -> List<object>,
// numbers -> double, plus string/bool/null) so the agent has no package dependencies.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SceneAgent
{
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = ReadValue(s, ref i);
            return v;
        }

        /// <summary>Like Parse, but only whitespace may follow the value, so the output check judges a tool call
        /// exactly as in training.</summary>
        public static object ParseStrict(string s)
        {
            int i = 0;
            var v = ReadValue(s, ref i);
            Skip(s, ref i);
            if (i != s.Length) throw new FormatException("extra data after the JSON value");
            return v;
        }

        static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ReadValue(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                Skip(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Skip(s, ref i);
                    string key = ReadString(s, ref i);
                    Skip(s, ref i);
                    if (s[i] != ':') throw new FormatException("expected ':'");
                    i++;
                    d[key] = ReadValue(s, ref i);
                    Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                Skip(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue(s, ref i));
                    Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("expected ',' or ']'");
                }
            }
            if (c == '"') return ReadString(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected string");
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }

        public static string Serialize(object v)
        {
            var sb = new StringBuilder();
            Write(sb, v);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(FormatNumber(d)); break;
                case float f: sb.Append(FormatNumber(f)); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> d:
                    sb.Append('{'); bool first = true;
                    foreach (var kv in d) { if (!first) sb.Append(','); first = false; WriteString(sb, kv.Key); sb.Append(':'); Write(sb, kv.Value); }
                    sb.Append('}'); break;
                case IEnumerable e:
                    sb.Append('['); bool f1 = true;
                    foreach (var x in e) { if (!f1) sb.Append(','); f1 = false; Write(sb, x); }
                    sb.Append(']'); break;
                default: WriteString(sb, v.ToString()); break;
            }
        }

        // integral floats print as "2.0", the same shape as the training data
        public static string FormatNumber(double d)
        {
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            return s.Contains(".") || s.Contains("E") ? s : s + ".0";
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
