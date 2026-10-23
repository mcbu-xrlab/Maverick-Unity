// Generated from the model's tool schema. Do not edit.
// Per tool: its arguments and their allowed values (null = any value of the argument's type), each argument's JSON
// type (an array is a list of strings), and the arguments a call must have. Used by the output check.
// V0: the 11 tools of Maverick 1.0. V1: the same plus undo (schema 1.1).
using System.Collections.Generic;

namespace SceneAgent
{
    public static partial class SceneGrammar
    {
        public static readonly Dictionary<string, Dictionary<string, string[]>> Tools = new Dictionary<string, Dictionary<string, string[]>>
        {
            { "grab", new Dictionary<string, string[]> { { "object_id", null }, { "hand", new[] { "left", "right", "both" } } } },
            { "release", new Dictionary<string, string[]> { { "hand", new[] { "left", "right", "both" } } } },
            { "place", new Dictionary<string, string[]> { { "object_id", null }, { "target_id", null }, { "relation", new[] { "on", "in" } }, { "orientation", new[] { "default", "upright", "horizontal", "vertical", "upside_down" } } } },
            { "move_by", new Dictionary<string, string[]> { { "object_id", null }, { "direction", new[] { "left", "right", "forward", "backward", "up", "down" } }, { "distance", null }, { "unit", new[] { "m", "cm" } } } },
            { "rotate", new Dictionary<string, string[]> { { "object_id", null }, { "degrees", null }, { "axis", new[] { "yaw", "pitch", "roll" } } } },
            { "set_state", new Dictionary<string, string[]> { { "object_id", null }, { "state", new[] { "on", "off", "open", "closed", "half_open", "locked", "unlocked" } } } },
            { "press", new Dictionary<string, string[]> { { "object_id", null }, { "hold_seconds", null } } },
            { "highlight", new Dictionary<string, string[]> { { "object_ids", null } } },
            { "teleport", new Dictionary<string, string[]> { { "location_id", null } } },
            { "find_objects", new Dictionary<string, string[]> { { "label", null }, { "color", null }, { "room", null } } },
            { "ask_clarification", new Dictionary<string, string[]> { { "question", null }, { "candidate_ids", null } } },
        };

        public static readonly Dictionary<string, Dictionary<string, string>> Types = new Dictionary<string, Dictionary<string, string>>
        {
            { "grab", new Dictionary<string, string> { { "object_id", "string" }, { "hand", "string" } } },
            { "release", new Dictionary<string, string> { { "hand", "string" } } },
            { "place", new Dictionary<string, string> { { "object_id", "string" }, { "target_id", "string" }, { "relation", "string" }, { "orientation", "string" } } },
            { "move_by", new Dictionary<string, string> { { "object_id", "string" }, { "direction", "string" }, { "distance", "number" }, { "unit", "string" } } },
            { "rotate", new Dictionary<string, string> { { "object_id", "string" }, { "degrees", "number" }, { "axis", "string" } } },
            { "set_state", new Dictionary<string, string> { { "object_id", "string" }, { "state", "string" } } },
            { "press", new Dictionary<string, string> { { "object_id", "string" }, { "hold_seconds", "number" } } },
            { "highlight", new Dictionary<string, string> { { "object_ids", "array" } } },
            { "teleport", new Dictionary<string, string> { { "location_id", "string" } } },
            { "find_objects", new Dictionary<string, string> { { "label", "string" }, { "color", "string" }, { "room", "string" } } },
            { "ask_clarification", new Dictionary<string, string> { { "question", "string" }, { "candidate_ids", "array" } } },
        };

        public static readonly Dictionary<string, string[]> Required = new Dictionary<string, string[]>
        {
            { "grab", new string[] { "object_id", "hand" } },
            { "release", new string[] { "hand" } },
            { "place", new string[] { "object_id", "target_id" } },
            { "move_by", new string[] { "object_id", "direction", "distance", "unit" } },
            { "rotate", new string[] { "object_id", "degrees" } },
            { "set_state", new string[] { "object_id", "state" } },
            { "press", new string[] { "object_id" } },
            { "highlight", new string[] { "object_ids" } },
            { "teleport", new string[] { "location_id" } },
            { "find_objects", new string[] { "label" } },
            { "ask_clarification", new string[] { "question", "candidate_ids" } },
        };

        public static readonly Dictionary<string, Dictionary<string, string[]>> ToolsV1 = new Dictionary<string, Dictionary<string, string[]>>
        {
            { "grab", new Dictionary<string, string[]> { { "object_id", null }, { "hand", new[] { "left", "right", "both" } } } },
            { "release", new Dictionary<string, string[]> { { "hand", new[] { "left", "right", "both" } } } },
            { "place", new Dictionary<string, string[]> { { "object_id", null }, { "target_id", null }, { "relation", new[] { "on", "in" } }, { "orientation", new[] { "default", "upright", "horizontal", "vertical", "upside_down" } } } },
            { "move_by", new Dictionary<string, string[]> { { "object_id", null }, { "direction", new[] { "left", "right", "forward", "backward", "up", "down" } }, { "distance", null }, { "unit", new[] { "m", "cm" } } } },
            { "rotate", new Dictionary<string, string[]> { { "object_id", null }, { "degrees", null }, { "axis", new[] { "yaw", "pitch", "roll" } } } },
            { "set_state", new Dictionary<string, string[]> { { "object_id", null }, { "state", new[] { "on", "off", "open", "closed", "half_open", "locked", "unlocked" } } } },
            { "press", new Dictionary<string, string[]> { { "object_id", null }, { "hold_seconds", null } } },
            { "highlight", new Dictionary<string, string[]> { { "object_ids", null } } },
            { "teleport", new Dictionary<string, string[]> { { "location_id", null } } },
            { "find_objects", new Dictionary<string, string[]> { { "label", null }, { "color", null }, { "room", null } } },
            { "ask_clarification", new Dictionary<string, string[]> { { "question", null }, { "candidate_ids", null } } },
            { "undo", new Dictionary<string, string[]> { { "steps", null } } },
        };

        public static readonly Dictionary<string, Dictionary<string, string>> TypesV1 = new Dictionary<string, Dictionary<string, string>>
        {
            { "grab", new Dictionary<string, string> { { "object_id", "string" }, { "hand", "string" } } },
            { "release", new Dictionary<string, string> { { "hand", "string" } } },
            { "place", new Dictionary<string, string> { { "object_id", "string" }, { "target_id", "string" }, { "relation", "string" }, { "orientation", "string" } } },
            { "move_by", new Dictionary<string, string> { { "object_id", "string" }, { "direction", "string" }, { "distance", "number" }, { "unit", "string" } } },
            { "rotate", new Dictionary<string, string> { { "object_id", "string" }, { "degrees", "number" }, { "axis", "string" } } },
            { "set_state", new Dictionary<string, string> { { "object_id", "string" }, { "state", "string" } } },
            { "press", new Dictionary<string, string> { { "object_id", "string" }, { "hold_seconds", "number" } } },
            { "highlight", new Dictionary<string, string> { { "object_ids", "array" } } },
            { "teleport", new Dictionary<string, string> { { "location_id", "string" } } },
            { "find_objects", new Dictionary<string, string> { { "label", "string" }, { "color", "string" }, { "room", "string" } } },
            { "ask_clarification", new Dictionary<string, string> { { "question", "string" }, { "candidate_ids", "array" } } },
            { "undo", new Dictionary<string, string> { { "steps", "number" } } },
        };

        public static readonly Dictionary<string, string[]> RequiredV1 = new Dictionary<string, string[]>
        {
            { "grab", new string[] { "object_id", "hand" } },
            { "release", new string[] { "hand" } },
            { "place", new string[] { "object_id", "target_id" } },
            { "move_by", new string[] { "object_id", "direction", "distance", "unit" } },
            { "rotate", new string[] { "object_id", "degrees" } },
            { "set_state", new string[] { "object_id", "state" } },
            { "press", new string[] { "object_id" } },
            { "highlight", new string[] { "object_ids" } },
            { "teleport", new string[] { "location_id" } },
            { "find_objects", new string[] { "label" } },
            { "ask_clarification", new string[] { "question", "candidate_ids" } },
            { "undo", new string[] {  } },
        };
    }
}
