// The output check: the model answers freely, then its output is checked against the tool schema and the scene:
//   "ok"            plain text, or every call names a known tool with known arguments, allowed values and known ids
//   "invalid_tool"  a tool the schema does not have                      -> the agent refuses (Refusal)
//   "invalid_value" an argument, allowed value or room the schema/scene does not have -> the agent refuses
//   "invalid_id"    an id outside the scene, the hands, the gaze and earlier tool results -> generate again under the grammar
//   "malformed"     a call that does not parse                          -> generate again under the grammar
// The first out-of-schema call decides at once; an unknown id is remembered while the rest is checked.
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

        /// <summary>The ids the model may name (scene objects, hands, gaze, ids in earlier tool results) and the rooms.</summary>
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
            foreach (var r in toolResultJsons ?? Enumerable.Empty<string>())
            {
                try { CollectIds(MiniJson.Parse(r), ids); }
                catch (System.FormatException) { }
            }
            ids = ids.Distinct().ToList();
            rooms = scene.TryGetValue("locations", out var l) && l is List<object> ls
                ? ls.OfType<string>().Distinct().ToList() : new List<string>();
        }

        public static string Check(string text, string sceneJson, IEnumerable<string> toolResultJsons)
        {
            text = text ?? "";
            var blocks = CallRe.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            if (blocks.Count == 0) return text.Contains("<tool_call>") ? "malformed" : "ok";
            Vocab(sceneJson, toolResultJsons, out var idList, out var rooms);
            var ids = new HashSet<string>(idList);
            string verdict = "ok";
            foreach (var b in blocks)
            {
                object parsed;
                try { parsed = MiniJson.ParseStrict(b); }
                catch (System.Exception) { return "malformed"; }
                if (!(parsed is Dictionary<string, object> obj) || !obj.TryGetValue("name", out var nameObj)) return "malformed";
                if (!(nameObj is string name) || !Tools.TryGetValue(name, out var spec)) return "invalid_tool";
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
                    if (IdKeys.Contains(k) && !(v is string iv && ids.Contains(iv))) verdict = "invalid_id";
                    if (IdListKeys.Contains(k) && NamesUnknownId(v, ids)) verdict = "invalid_id";
                }
            }
            return verdict;
        }

        // a string where a list is expected is checked character by character, as in training
        static bool NamesUnknownId(object v, HashSet<string> ids)
        {
            switch (v)
            {
                case null: return false;
                case List<object> l: return l.Any(x => !(x is string s && ids.Contains(s)));
                case string s: return s.Length > 0 && s.Any(ch => !ids.Contains(ch.ToString()));
                default: return true;
            }
        }
    }
}
