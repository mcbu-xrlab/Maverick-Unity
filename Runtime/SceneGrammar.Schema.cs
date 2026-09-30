// Generated from the model's tool schema. Do not edit.
// Per tool: its arguments and their allowed values (null = any value of the argument's type). Used by the output check.
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
    }
}
