// The output check: the model answers freely, then its output is checked against the tool schema and the scene:
//   "ok"            plain text, or every call names a known tool with known arguments, allowed values and known ids
//   "invalid_tool"  a tool the schema does not have                      -> the agent refuses (Refusal)
//   "invalid_value" an argument, allowed value or room the schema/scene does not have -> the agent refuses
//   "invalid_id"    an id outside the scene, the hands, the gaze and earlier tool results -> generate again under the grammar
//   "malformed"     a call that does not parse, lacks a required argument or has one of the wrong type (strings, finite
//                   numbers, lists of strings; null counts as absent), a half-written call, or reasoning left open
//                   -> generate again under the grammar
// Reasoning never acts: complete <think> blocks are removed first, and anything before a stray </think> is reasoning.
// The first out-of-schema call decides at once; an unknown id is remembered while the rest is checked.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SceneAgent
{
    public static partial class SceneGrammar
    {
        public const string Refusal = "I can't do that; none of my tools can.";
        static readonly HashSet<string> IdKeys = new HashSet<string> { "object_id", "target_id" };
        static readonly HashSet<string> IdListKeys = new HashSet<string> { "object_ids", "candidate_ids" };
        static readonly HashSet<string> RoomKeys = new HashSet<string> { "location_id", "room" };
        static readonly Regex CallRe = new Regex(@"<tool_call>\s*(.*?)\s*</tool_call>", RegexOptions.Singleline);
        static readonly Regex ThinkRe = new Regex(@"<think>.*?</think>\s*", RegexOptions.Singleline);

        static readonly Regex NumRe = new Regex(@"^-?\d+(\.\d+)?(e[+-]?\d+)?$");
        static HashSet<string> enumValues;
        static HashSet<string> EnumValues => enumValues ??= new HashSet<string>(
            Tools.Values.Concat(ToolsV1.Values).SelectMany(a => a.Values).Where(v => v != null).SelectMany(v => v));

        /// <summary>Ids named inside a recent_actions call string ("place(mug_1, table_2, on, default)"): every argument
        /// that is not a number, an allowed value of the schema, a room, a person, or True / False / None.</summary>
        public static List<string> CallArgIds(string call, ICollection<string> rooms, ICollection<string> people)
        {
            int a = call.IndexOf('('), b = call.LastIndexOf(')');
            string inner = a >= 0 && b > a ? call.Substring(a + 1, b - a - 1) : "";
            var outp = new List<string>();
            foreach (var piece in inner.Split(new[] { ", " }, StringSplitOptions.None))
            {
                string tok = piece.Trim('[', ']');
                if (tok.Length > 0 && !NumRe.IsMatch(tok) && !EnumValues.Contains(tok) && !rooms.Contains(tok) && !people.Contains(tok)
                    && tok != "True" && tok != "False" && tok != "None")
                    outp.Add(tok);
            }
            return outp;
        }

        /// <summary>The ids the model may name and the rooms: the scene's objects, hands and gaze; in V1 scenes the ids in
        /// attention, the open question's candidates and the calls in recent_actions; ids in earlier tool results.</summary>
        public static void Vocab(string sceneJson, IEnumerable<string> toolResultJsons, out List<string> ids, out List<string> rooms)
        {
            var scene = (Dictionary<string, object>)MiniJson.Parse(sceneJson);
            ids = new List<string>();
            if (scene.TryGetValue("visible_objects", out var vo) && vo is List<object> objs)
                foreach (var o in objs)
                    if (o is Dictionary<string, object> d && d.TryGetValue("id", out var id) && id is string s) ids.Add(s);
            if (scene.TryGetValue("player", out var pl) && pl is Dictionary<string, object> player)
                foreach (var k in new[] { "left_hand", "right_hand", "gaze" })
                    if (player.TryGetValue(k, out var v) && v is string s && s.Length > 0) ids.Add(s);
            rooms = scene.TryGetValue("locations", out var l) && l is List<object> ls
                ? ls.OfType<string>().Distinct().ToList() : new List<string>();
            if (scene.TryGetValue("attention", out var at) && at is Dictionary<string, object> att)
                foreach (var acts in att.Values.OfType<Dictionary<string, object>>())
                    foreach (var entries in acts.Values.OfType<List<object>>())
                        foreach (var e in entries)
                            if (e is Dictionary<string, object> ed && ed.TryGetValue("id", out var eid) && eid is string es) ids.Add(es);
            if (scene.TryGetValue("pending_clarification", out var pc) && pc is Dictionary<string, object> pend
                && pend.TryGetValue("candidate_ids", out var cids) && cids is List<object> cl)
                ids.AddRange(cl.OfType<string>());
            var people = scene.TryGetValue("people", out var pp) && pp is Dictionary<string, object> pd
                ? new HashSet<string>(pd.Keys) : new HashSet<string>();
            if (scene.TryGetValue("recent_actions", out var ra) && ra is List<object> recent)
                foreach (var e in recent.OfType<Dictionary<string, object>>())
                    if (e.TryGetValue("calls", out var cs) && cs is List<object> calls)
                        foreach (var c in calls.OfType<string>()) ids.AddRange(CallArgIds(c, rooms, people));
            foreach (var r in toolResultJsons ?? Enumerable.Empty<string>())
            {
                try { CollectIds(MiniJson.Parse(r), ids); }
                catch (Exception) { }
            }
            ids = ids.Distinct().ToList();
        }

        /// <summary>The answer without its reasoning, or null when reasoning is left open.</summary>
        public static string StripReasoning(string text)
        {
            text = ThinkRe.Replace(text ?? "", "");
            int close = text.LastIndexOf("</think>", StringComparison.Ordinal);
            if (close >= 0) text = text.Substring(close + "</think>".Length);
            return text.Contains("<think>") ? null : text;
        }

        /// <summary>The bodies of the complete tool-call blocks in an answer (reasoning already removed).</summary>
        public static List<string> CallBlocks(string answer) =>
            CallRe.Matches(answer ?? "").Cast<Match>().Select(m => m.Groups[1].Value).ToList();

        public static string Check(string text, string sceneJson, IEnumerable<string> toolResultJsons, SchemaVersion schema = SchemaVersion.V0)
        {
            var tools = schema == SchemaVersion.V1 ? ToolsV1 : Tools;
            var types = schema == SchemaVersion.V1 ? TypesV1 : Types;
            var required = schema == SchemaVersion.V1 ? RequiredV1 : Required;
            text = StripReasoning(text);
            if (text == null) return "malformed";
            var blocks = CallBlocks(text);
            string rest = CallRe.Replace(text, "");
            if (rest.Contains("<tool_call>") || rest.Contains("</tool_call>")) return "malformed";
            if (blocks.Count == 0) return "ok";
            Vocab(sceneJson, toolResultJsons, out var idList, out var rooms);
            var ids = new HashSet<string>(idList);
            string verdict = "ok";
            foreach (var b in blocks)
            {
                object parsed;
                try { parsed = MiniJson.ParseStrict(b); }
                catch (Exception) { return "malformed"; }
                if (!(parsed is Dictionary<string, object> obj) || !obj.TryGetValue("name", out var nameObj)) return "malformed";
                if (!(nameObj is string name) || !tools.TryGetValue(name, out var spec)) return "invalid_tool";
                obj.TryGetValue("arguments", out var argsObj);
                // an empty or false "arguments" value counts as no arguments
                bool empty = argsObj == null || argsObj is List<object> el && el.Count == 0 || argsObj is string es && es.Length == 0
                             || argsObj is double dz && dz == 0 || argsObj is bool bf && !bf;
                if (!empty && !(argsObj is Dictionary<string, object>)) return "malformed";
                var args = empty ? new Dictionary<string, object>() : (Dictionary<string, object>)argsObj;
                foreach (var kv in args)
                {
                    string k = kv.Key;
                    object v = kv.Value;
                    if (!spec.TryGetValue(k, out var allowed)) return "invalid_value";
                    if (allowed != null && !(v is string sv && allowed.Contains(sv))) return "invalid_value";
                    if (RoomKeys.Contains(k) && rooms.Count > 0 && !(v is string rv && rooms.Contains(rv))) return "invalid_value";
                    if (v != null && !TypeOk(types[name][k], v)) return "malformed";
                    if (IdKeys.Contains(k) && !(v is string iv && ids.Contains(iv))) verdict = "invalid_id";
                    if (IdListKeys.Contains(k) && v is List<object> l && l.Any(x => !ids.Contains((string)x))) verdict = "invalid_id";
                }
                if (required[name].Any(k => !args.TryGetValue(k, out var rv2) || rv2 == null)) return "malformed";
            }
            return verdict;
        }

        static bool TypeOk(string kind, object v)
        {
            switch (kind)
            {
                case "string": return v is string;
                case "number": return v is double d && !double.IsNaN(d) && !double.IsInfinity(d);
                case "array": return v is List<object> l && l.All(x => x is string);
                default: return true;
            }
        }
    }
}
