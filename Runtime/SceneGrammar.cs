// Grammar-constrained decoding: a GBNF grammar that only allows valid tool calls or plain text.
// The static part (the 11 tools, their arguments in the trained order, the <tool_call> format, plain text that cannot
// start a half-written call) is generated into SceneGrammar.Static.cs. This file adds the per-request rules: the ids
// the model may name (the scene's objects, the player's hands and gaze, and every id an earlier tool result returned,
// e.g. objects find_objects reported in another room) and the scene's rooms. llama-server applies it as "grammar".
using System.Collections.Generic;
using System.Linq;

namespace SceneAgent
{
    public static partial class SceneGrammar
    {
        public static string Build(string sceneJson, IEnumerable<string> toolResultJsons)
        {
            var scene = (Dictionary<string, object>)MiniJson.Parse(sceneJson);
            var ids = new List<string>();
            if (scene.TryGetValue("visible_objects", out var vo) && vo is List<object> objs)
                foreach (var o in objs)
                    if (o is Dictionary<string, object> d && d.TryGetValue("id", out var id) && id is string s) ids.Add(s);
            if (scene.TryGetValue("player", out var pl) && pl is Dictionary<string, object> player)
                foreach (var k in new[] { "left_hand", "right_hand", "gaze" })
                    if (player.TryGetValue(k, out var v) && v is string s && s.Length > 0) ids.Add(s);
            foreach (var r in toolResultJsons)
            {
                try { CollectIds(MiniJson.Parse(r), ids); }
                catch (System.FormatException) { }
            }
            var rooms = scene.TryGetValue("locations", out var l) && l is List<object> ls
                ? ls.OfType<string>().Distinct().ToList() : new List<string>();
            return Static + "id ::= " + Alts(ids.Distinct()) + "\nroom ::= " + (rooms.Count > 0 ? Alts(rooms) : "str") + "\n";
        }

        // each value written as a JSON string, as a GBNF literal: cup_1 -> "\"cup_1\""
        static string Alts(IEnumerable<string> values) =>
            string.Join(" | ", values.Select(v => MiniJson.Serialize(MiniJson.Serialize(v))));

        static void CollectIds(object x, List<string> ids)
        {
            if (x is Dictionary<string, object> d)
            {
                if (d.TryGetValue("id", out var id) && id is string s) ids.Add(s);
                foreach (var v in d.Values) CollectIds(v, ids);
            }
            else if (x is List<object> l)
                foreach (var v in l) CollectIds(v, ids);
        }
    }
}
