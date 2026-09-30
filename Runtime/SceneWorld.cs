// Scene state + tool semantics on real GameObjects. The rules match the simulator the model was trained on, so the
// model's calls behave here exactly as they did in training.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace SceneAgent
{
    public class ToolError : Exception { public ToolError(string m) : base(m) { } }

    public class SceneWorld : MonoBehaviour
    {
        public string playerRoom;
        public string gaze;
        public string leftHand, rightHand;
        public List<string> rooms = new List<string>();
        public Transform leftHandAnchor, rightHandAnchor;
        public readonly SortedSet<string> highlighted = new SortedSet<string>(StringComparer.Ordinal);
        public SortedSet<string> clarify = new SortedSet<string>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, double> pressed = new SortedDictionary<string, double>(StringComparer.Ordinal);
        public string lastClarifyQuestion;
        /// <summary>Where a room sits in the Unity scene (for example, rooms side by side). The model only ever sees
        /// room-local positions; null = every room at the origin.</summary>
        [NonSerialized] public Func<string, Vector3> roomOrigin;
        [Tooltip("On: at Start every SceneItem's logical position and rotation are read from where it stands in the scene, " +
                 "and the rooms and the player's room are filled in from the items when empty. Leave off when the scene " +
                 "is built from data by your own code.")]
        public bool initFromTransforms;
        /// <summary>Raised after every tool call Unity accepted: (tool, arguments, result). Hook animations, sounds or the
        /// camera here, e.g. move the player rig when "teleport" changes playerRoom.</summary>
        public event Action<string, Dictionary<string, object>, object> ToolExecuted;

        // affordances per label (SceneCatalog.Generated.cs): the same catalogue the model was trained on, so Unity
        // accepts exactly the calls the model learned to make
        static Dictionary<string, double> Top => SceneCatalog.Top;
        static HashSet<string> Grasp => SceneCatalog.Grasp;
        static HashSet<string> Container => SceneCatalog.Container;
        static HashSet<string> Fixed => SceneCatalog.Fixed;
        static Dictionary<string, string[]> States => SceneCatalog.States;
        static readonly Dictionary<string, string[]> Required = new Dictionary<string, string[]> {
            { "grab", new[] { "object_id", "hand" } }, { "release", new[] { "hand" } }, { "place", new[] { "object_id", "target_id" } },
            { "move_by", new[] { "object_id", "direction", "distance", "unit" } }, { "rotate", new[] { "object_id", "degrees" } },
            { "set_state", new[] { "object_id", "state" } }, { "press", new[] { "object_id" } }, { "highlight", new[] { "object_ids" } },
            { "teleport", new[] { "location_id" } }, { "find_objects", new[] { "label" } }, { "ask_clarification", new[] { "question", "candidate_ids" } } };
        static readonly Dictionary<string, Vector3> Dir = new Dictionary<string, Vector3> {
            { "left", Vector3.left }, { "right", Vector3.right }, { "forward", Vector3.forward },
            { "backward", Vector3.back }, { "up", Vector3.up }, { "down", Vector3.down } };
        static readonly string[] HandSlots = { "left_hand", "right_hand", "both_hands" };

        // what an item can do: the catalogue by label, unless the item overrides it (SceneItem.overrideAffordances)
        static bool CanGrasp(SceneItem o) => o.overrideAffordances ? o.grabbable : Grasp.Contains(o.label);
        static bool TryTop(SceneItem t, out double h)
        {
            if (t.overrideAffordances) { h = t.surfaceHeight; return t.isSurface; }
            return Top.TryGetValue(t.label, out h);
        }
        static bool IsContainer(SceneItem o) => o.overrideAffordances ? o.isContainer : Container.Contains(o.label);
        static bool IsFixed(SceneItem o) => o.overrideAffordances ? o.isFixed : Fixed.Contains(o.label);
        static bool IsButton(SceneItem o) => o.overrideAffordances ? o.isButton : SceneCatalog.Button.Contains(o.label);
        static bool CanBe(SceneItem o, string st) => o.overrideAffordances
            ? (o.states ?? new string[0]).Contains(st) : States.ContainsKey(o.label) && States[o.label].Contains(st);

        void Start()
        {
            if (!initFromTransforms) return;
            foreach (var o in Items)
            {
                if (string.IsNullOrEmpty(o.itemId) || !o.hasPos || HandSlots.Contains(o.on)) continue;
                var tr = o.transform;
                var p = tr.position - (roomOrigin != null ? roomOrigin(o.room) : Vector3.zero);
                o.pos = new[] { SceneMath.Round(p.x, 3), SceneMath.Round(p.y - tr.localScale.y / 2f, 3), SceneMath.Round(p.z, 3) };
                var e = tr.rotation.eulerAngles;
                o.yaw = SceneMath.Round(e.y, 3); o.pitch = SceneMath.Round(e.x, 3); o.roll = SceneMath.Round(e.z, 3);
            }
            if (rooms.Count == 0) rooms = Items.Select(o => o.room).Where(r => !string.IsNullOrEmpty(r)).Distinct().ToList();
            if (string.IsNullOrEmpty(playerRoom) && rooms.Count > 0) playerRoom = rooms[0];
        }

        // Held items are parented to the hand anchors while they are shown in the hand; when the anchors live outside
        // this hierarchy (for example, on the camera rig) they must still count as scene items, or every tool call on a
        // held item fails with "unknown object" ("put it on the counter" after a grab).
        public IEnumerable<SceneItem> Items
        {
            get
            {
                IEnumerable<SceneItem> all = GetComponentsInChildren<SceneItem>(true);
                foreach (var anchor in new[] { leftHandAnchor, rightHandAnchor })
                    if (anchor != null && !anchor.IsChildOf(transform))
                        all = all.Concat(anchor.GetComponentsInChildren<SceneItem>(true));
                return all.Distinct();
            }
        }

        SceneItem Get(string id)
        {
            var o = Items.FirstOrDefault(x => x.itemId == id);
            if (o == null) throw new ToolError($"unknown object '{id}'");
            if (o.room != playerRoom) throw new ToolError($"object '{id}' is not visible");
            return o;
        }

        // ------------------------------------------------------------------ scene JSON for the prompt
        public string BuildSceneJson()
        {
            var vis = new List<object>();
            // hierarchy order, not sorted: training scenes list objects in random order, and the model does not depend
            // on the order
            foreach (var o in Items.Where(x => x.room == playerRoom))
            {
                var v = new Dictionary<string, object> { { "id", o.itemId }, { "label", o.label } };
                if (!string.IsNullOrEmpty(o.color)) v["color"] = o.color;
                v["on"] = o.on;
                if (!string.IsNullOrEmpty(o.state)) v["state"] = o.state;
                if (o.hasPos) v["pos"] = o.pos.Select(c => (object)SceneMath.Round(c, 1)).ToList();
                vis.Add(v);
            }
            var scene = new Dictionary<string, object> {
                { "player", new Dictionary<string, object> { { "room", playerRoom }, { "gaze", Nz(gaze) }, { "left_hand", Nz(leftHand) }, { "right_hand", Nz(rightHand) } } },
                { "locations", rooms.Cast<object>().ToList() }, { "visible_objects", vis } };
            return MiniJson.Serialize(scene);
        }

        static object Nz(string s) => string.IsNullOrEmpty(s) ? null : s;

        // ------------------------------------------------------------------ tools
        static string S(Dictionary<string, object> a, string k) => a.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        static double D(Dictionary<string, object> a, string k, double def = 0) => a.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : def;
        static List<string> L(Dictionary<string, object> a, string k) => a.TryGetValue(k, out var v) && v is List<object> l ? l.Select(x => Convert.ToString(x)).ToList() : new List<string>();

        static string[] HandsFor(string hand) =>
            hand == "left" ? new[] { "left" } : hand == "right" ? new[] { "right" } : hand == "both" ? new[] { "left", "right" } : throw new ToolError($"invalid hand '{hand}'");

        string HandGet(string h) => h == "left" ? leftHand : rightHand;
        void HandSet(string h, string v) { if (h == "left") leftHand = v; else rightHand = v; }
        void FreeHand(SceneItem o) { if (leftHand == o.itemId) leftHand = null; if (rightHand == o.itemId) rightHand = null; }

        void MoveTree(SceneItem o, double[] d)
        {
            for (int i = 0; i < 3; i++) o.pos[i] = SceneMath.Round(o.pos[i] + d[i], 3);
            foreach (var c in Items) if (c.on == o.itemId && c.hasPos) MoveTree(c, d);
        }

        /// <summary>Executes one call; returns the tool result object (serialised back to the model). Throws ToolError
        /// when the call is not possible here; ToolExecuted is raised only for accepted calls.</summary>
        public object Execute(string name, Dictionary<string, object> a)
        {
            var result = ExecuteTool(name, a);
            ToolExecuted?.Invoke(name, a, result);
            return result;
        }

        object ExecuteTool(string name, Dictionary<string, object> a)
        {
            if (!Required.ContainsKey(name ?? "")) throw new ToolError($"unknown tool '{name}'");
            var missing = Required[name].Where(k => !a.ContainsKey(k) || a[k] == null).ToList();
            if (missing.Count > 0) throw new ToolError("missing arguments [" + string.Join(", ", missing.Select(m => $"'{m}'")) + "]");
            var ok = new Dictionary<string, object> { { "ok", true } };
            switch (name)
            {
                case "grab":
                {
                    var o = Get(S(a, "object_id"));
                    if (!CanGrasp(o)) throw new ToolError($"'{o.itemId}' cannot be picked up");
                    if (HandSlots.Contains(o.on)) throw new ToolError($"'{o.itemId}' is already held");
                    string hand = S(a, "hand");
                    var hs = HandsFor(hand);
                    if (hs.Any(h => !string.IsNullOrEmpty(HandGet(h)))) throw new ToolError($"{hand} hand is not free");
                    foreach (var h in hs) HandSet(h, o.itemId);
                    o.on = hand == "both" ? "both_hands" : hand + "_hand"; o.hasPos = false;
                    Sync(o); return ok;
                }
                case "release":
                {
                    string hand = S(a, "hand");
                    var held = new SortedSet<string>(HandsFor(hand).Select(HandGet).Where(x => !string.IsNullOrEmpty(x)), StringComparer.Ordinal);
                    if (held.Count == 0) throw new ToolError($"nothing held in {hand} hand");
                    foreach (var id in held) { var o = Get(id); FreeHand(o); o.on = "floor"; o.hasPos = true; o.pos = new[] { 0.0, 0.0, 0.6 }; Sync(o); }
                    return ok;
                }
                case "place":
                {
                    var o = Get(S(a, "object_id")); var t = Get(S(a, "target_id"));
                    string rel = S(a, "relation") ?? "on", orient = S(a, "orientation") ?? "default";
                    if (!CanGrasp(o)) throw new ToolError($"'{o.itemId}' cannot be carried");
                    if (o.itemId == t.itemId || HandSlots.Contains(t.on)) throw new ToolError("invalid target");
                    if (rel == "on" && !TryTop(t, out _)) throw new ToolError($"'{t.itemId}' is not a surface");
                    if (rel == "in" && !IsContainer(t)) throw new ToolError($"'{t.itemId}' is not a container");
                    if (rel == "in" && (t.state == "closed" || t.state == "locked")) throw new ToolError($"'{t.itemId}' is closed");
                    if (!new[] { "default", "upright", "horizontal", "vertical", "upside_down" }.Contains(orient)) throw new ToolError($"invalid orientation '{orient}'");
                    FreeHand(o);
                    double top = rel == "on" && TryTop(t, out var th) ? th : 0.0;
                    o.on = t.itemId; o.relation = rel; o.orientation = orient; o.hasPos = true;
                    o.pos = new[] { t.pos[0], SceneMath.Round(t.pos[1] + top, 3), t.pos[2] };
                    Sync(o); return ok;
                }
                case "move_by":
                {
                    var o = Get(S(a, "object_id"));
                    if (HandSlots.Contains(o.on) || IsFixed(o)) throw new ToolError($"'{o.itemId}' cannot be moved");
                    string dir = S(a, "direction"), unit = S(a, "unit");
                    if (dir == null || !Dir.ContainsKey(dir) || (unit != "m" && unit != "cm")) throw new ToolError("invalid direction or unit");
                    double dist = D(a, "distance") * (unit == "cm" ? 0.01 : 1.0);
                    var v = Dir[dir];
                    MoveTree(o, new[] { v.x * dist, v.y * dist, v.z * dist });
                    foreach (var x in Items) Sync(x);
                    return ok;
                }
                case "rotate":
                {
                    var o = Get(S(a, "object_id"));
                    if (HandSlots.Contains(o.on) || IsFixed(o)) throw new ToolError($"'{o.itemId}' cannot be rotated");
                    string axis = S(a, "axis") ?? "yaw";
                    double Wrap(double v) => SceneMath.Round(((v % 360) + 360) % 360, 3);
                    double deg = D(a, "degrees");
                    if (axis == "yaw") o.yaw = Wrap(o.yaw + deg);
                    else if (axis == "pitch") o.pitch = Wrap(o.pitch + deg);
                    else if (axis == "roll") o.roll = Wrap(o.roll + deg);
                    else throw new ToolError($"invalid axis '{axis}'");
                    Sync(o); return ok;
                }
                case "set_state":
                {
                    var o = Get(S(a, "object_id")); string st = S(a, "state");
                    if (!CanBe(o, st)) throw new ToolError($"'{o.itemId}' cannot be {st}");
                    o.state = st; Sync(o); return ok;
                }
                case "press":
                {
                    var o = Get(S(a, "object_id"));
                    if (!IsButton(o)) throw new ToolError($"'{o.itemId}' is not a button");
                    pressed[o.itemId] = D(a, "hold_seconds"); return ok;
                }
                case "highlight":
                {
                    var ids = L(a, "object_ids");
                    if (ids.Count == 0) throw new ToolError("nothing to highlight");
                    foreach (var id in ids) Get(id);
                    foreach (var id in ids) { highlighted.Add(id); Sync(Get(id)); }
                    return ok;
                }
                case "teleport":
                {
                    string loc = S(a, "location_id");
                    if (!rooms.Contains(loc)) throw new ToolError($"unknown location '{loc}'");
                    foreach (var o in Items) if (HandSlots.Contains(o.on)) o.room = loc;
                    playerRoom = loc; return ok;
                }
                case "find_objects":
                {
                    string label = S(a, "label"), color = S(a, "color"), room = S(a, "room");
                    // hierarchy order, like the training simulator's world order
                    return Items.Where(o => o.label == label && (color == null || o.color == color) && (room == null || o.room == room))
                        .Select(o =>
                        {
                            var d = new Dictionary<string, object> { { "id", o.itemId }, { "label", o.label } };
                            if (!string.IsNullOrEmpty(o.color)) d["color"] = o.color;
                            d["room"] = o.room; d["on"] = o.on;
                            return (object)d;
                        }).ToList();
                }
                case "ask_clarification":
                    clarify = new SortedSet<string>(L(a, "candidate_ids"), StringComparer.Ordinal);
                    lastClarifyQuestion = S(a, "question");
                    return ok;
            }
            throw new ToolError($"unknown tool '{name}'");
        }

        // ------------------------------------------------------------------ visuals
        public void Sync(SceneItem o)
        {
            var tr = o.transform;
            if (HandSlots.Contains(o.on))
            {
                var anchor = o.on == "left_hand" ? leftHandAnchor : rightHandAnchor;
                if (anchor != null) { tr.SetParent(anchor, false); tr.localPosition = Vector3.zero; }
            }
            else
            {
                tr.SetParent(transform, true);
                float half = tr.localScale.y / 2f;
                var origin = roomOrigin != null ? roomOrigin(o.room) : Vector3.zero;
                tr.position = origin + new Vector3((float)o.pos[0], (float)o.pos[1] + half, (float)o.pos[2]);
            }
            tr.rotation = Quaternion.Euler((float)o.pitch, (float)o.yaw, (float)o.roll);
            var r = o.GetComponent<Renderer>();
            if (r != null && Application.isPlaying)
                r.material.SetColor("_EmissionColor", highlighted.Contains(o.itemId) ? Color.yellow : Color.black);
            var light = o.GetComponentInChildren<Light>();
            if (light != null) light.enabled = o.state == "on";
        }

        // ------------------------------------------------------------------ same text form as sim.signature_str
        static string Num(double v, int digits)
        {
            if (Math.Abs(v) < 0.5 * Math.Pow(10, -digits)) v = 0.0;
            return SceneMath.Fixed(v, digits);
        }

        public string Signature()
        {
            var parts = new List<string>();
            foreach (var o in Items.OrderBy(x => x.itemId, StringComparer.Ordinal))
            {
                string p = o.hasPos ? string.Join(",", o.pos.Select(v => Num(v, 2))) : "-";
                parts.Add(string.Join("|", o.itemId, o.room, o.on ?? "", o.relation ?? "", string.IsNullOrEmpty(o.orientation) ? "default" : o.orientation,
                    p, $"{Num(o.yaw, 1)},{Num(o.pitch, 1)},{Num(o.roll, 1)}", o.state ?? ""));
            }
            parts.Add($"P:{playerRoom},{leftHand ?? ""},{rightHand ?? ""}");
            parts.Add("H:" + string.Join(",", highlighted));
            parts.Add("C:" + string.Join(",", clarify));
            parts.Add("X:" + string.Join(",", pressed.Select(kv => $"{kv.Key}={Num(kv.Value, 1)}")));
            return string.Join(";", parts);
        }
    }
}
