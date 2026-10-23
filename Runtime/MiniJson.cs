// Minimal JSON reader/writer (objects -> Dictionary<string, object>, arrays -> List<object>,
// numbers -> double, plus string/bool/null) so the agent has no package dependencies.
// The reader follows the JSON standard as Python's json module does (number syntax, escapes, no raw control characters,
// JSON whitespace), so the output check accepts exactly what the reference check accepts.
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

        static void Skip(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }

        static bool Digit(char c) => c >= '0' && c <= '9';

        static bool At(string s, int i, string word) => string.CompareOrdinal(s, i, word, 0, word.Length) == 0 && s.Length - i >= word.Length;

        static void Digits(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && Digit(s[i])) i++;
            if (i == start) throw new FormatException("invalid number");
        }

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
            if (At(s, i, "true")) { i += 4; return true; }
            if (At(s, i, "false")) { i += 5; return false; }
            if (At(s, i, "null")) { i += 4; return null; }
            // -?(0|[1-9][0-9]*)(.[0-9]+)?([eE][+-]?[0-9]+)?
            int start = i;
            if (s[i] == '-') i++;
            if (i < s.Length && s[i] == '0') i++;
            else if (i < s.Length && s[i] >= '1' && s[i] <= '9') Digits(s, ref i);
            else throw new FormatException("invalid value");
            if (i < s.Length && s[i] == '.') { i++; Digits(s, ref i); }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                Digits(s, ref i);
            }
            double v = double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            // Mono's double.Parse("-0.0") gives +0.0; Python keeps the sign, and "-0.0" is printed as such in scenes
            return v == 0 && s[start] == '-' ? -0.0 : v;
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
                    if (e == 'u')
                    {
                        int code = 0;
                        for (int k = 1; k <= 4; k++)
                        {
                            char h = s[i + k];
                            int d = h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                            if (d < 0) throw new FormatException("invalid \\u escape");
                            code = code * 16 + d;
                        }
                        sb.Append((char)code); i += 4;
                    }
                    else if (e == '"' || e == '\\' || e == '/') sb.Append(e);
                    else if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 'b') sb.Append('\b');
                    else if (e == 'f') sb.Append('\f');
                    else throw new FormatException("invalid escape");
                }
                else if (s[i] < ' ') throw new FormatException("control character in string");
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
            // Mono prints negative zero as "0"; Python (the training scenes) prints "-0.0" (round(-0.04, 1))
            if (d == 0 && BitConverter.DoubleToInt64Bits(d) < 0) return "-0.0";
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
                // the same escapes as Python's json.dumps (the training data)
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c == '\b') sb.Append("\\b");
                else if (c == '\f') sb.Append("\\f");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
