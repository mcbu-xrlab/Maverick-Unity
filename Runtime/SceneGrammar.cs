// Grammar-constrained decoding: a GBNF grammar that only allows valid tool calls or plain text.
// The static part (the 11 tools, plus undo in V1, their arguments in the trained order, the <tool_call> format, plain
// text that cannot start a half-written call) is generated into SceneGrammar.Static.cs. This file adds the per-request
// rules: the ids the model may name (SceneGrammar.Vocab) and the scene's rooms. llama-server applies it as "grammar".
using System.Collections.Generic;
using System.Linq;

namespace SceneAgent
{
    public static partial class SceneGrammar
    {
        public static string Build(string sceneJson, IEnumerable<string> toolResultJsons, SchemaVersion schema = SchemaVersion.V0)
        {
            Vocab(sceneJson, toolResultJsons, out var ids, out var rooms);
            return (schema == SchemaVersion.V1 ? StaticV1 : Static) + "id ::= " + Alts(ids) + "\nroom ::= " + (rooms.Count > 0 ? Alts(rooms) : "str") + "\n";
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
