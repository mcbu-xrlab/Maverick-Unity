// The v1 session state around the tool calls (schema V1, package 1.1): command boundaries, history and undo, object
// versions, the open question, attention and people. It is a port of the Python simulator Maverick 1.1 was
// trained on, and every string it writes (call strings, tool results, scene fields) matches that simulator byte for byte.
// In V0 none of this is used: SceneAgent calls Execute and BuildSceneJson exactly as in 1.0.1.
//
// One command:  BeginCommand(request, now)  ExecuteV1(...) / Undo  ...  EndCommand()
// The scene for the request is built before BeginCommand (BuildSceneJsonV1), so it still shows the open question.
// Objects moved by a person, by physics or by your own code must go through ApplyExternalChange: it bumps their
// versions, so undo refuses to overwrite the change.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace SceneAgent
{
    /// <summary>A person in the scene (v1 "people"): the id the model uses and their role.</summary>
    [Serializable]
    public class ScenePerson
    {
        public string id;
        public string role;
    }

    /// <summary>The logical state of one item at a point in time (sim_v1 FIELDS, plus its version).</summary>
    public class ItemState
    {
        public string room, on, relation, orientation, state;
        public double[] pos;       // null: held or carried
        public bool hasRot;        // the simulator's "rot" key exists (set by rotate); false means 0, 0, 0
        public double yaw, pitch, roll;
        public double[] carry;
        public int ver;
        /// <summary>The item this image belongs to (null when loaded from data). Undo refuses when the id now belongs to
        /// another instance (an item destroyed and created again with the same id).</summary>
        public SceneItem instance;

        public bool SameAs(ItemState b) =>
            room == b.room && on == b.on && relation == b.relation && orientation == b.orientation && state == b.state &&
            SameArr(pos, b.pos) && hasRot == b.hasRot && (!hasRot || (yaw == b.yaw && pitch == b.pitch && roll == b.roll)) &&
            SameArr(carry, b.carry);

        static bool SameArr(double[] x, double[] y) => x == null ? y == null : y != null && x.SequenceEqual(y);
    }

    public class PlayerState
    {
        public string room, leftHand, rightHand;
        public bool SameAs(PlayerState b) => room == b.room && leftHand == b.leftHand && rightHand == b.rightHand;
    }

    /// <summary>A call as the model wrote it: the arguments keep the model's order (call strings depend on it).</summary>
    public class RecordedCall
    {
        public string name;
        public List<KeyValuePair<string, object>> args = new List<KeyValuePair<string, object>>();

        public RecordedCall(string name, Dictionary<string, object> a)
        {
            this.name = name;
            if (a != null) args.AddRange(a);
        }

        public object Arg(string k) => args.FirstOrDefault(kv => kv.Key == k).Value;
    }

    /// <summary>One history entry (sim_v1.end): newest first in SceneWorld.history.</summary>
    public class CommandRecord
    {
        public double t;
        public List<RecordedCall> calls = new List<RecordedCall>();
        public string status;   // "done" or "failed"
        public bool undoable;
        /// <summary>The before-image of every item the command changed, in the order they first changed.</summary>
        public List<KeyValuePair<string, ItemState>> before = new List<KeyValuePair<string, ItemState>>();
        public PlayerState playerBefore;
        /// <summary>Each changed item's version after the command (the undo check).</summary>
        public List<KeyValuePair<string, int>> vers = new List<KeyValuePair<string, int>>();
    }

    public class PendingQuestion
    {
        public string request, question;
        public List<string> candidateIds = new List<string>();
        public double t;
    }

    public class AttentionEntry
    {
        public string id;
        public double t;
        public bool active;
    }

    /// <summary>Where each person looked and pointed (sim_v1.attention_report / attention_release). Per person and act
    /// ("gaze" or "point"): newest first, distinct targets, at most 10 kept.</summary>
    public class AttentionLedger
    {
        public readonly Dictionary<string, Dictionary<string, List<AttentionEntry>>> lists =
            new Dictionary<string, Dictionary<string, List<AttentionEntry>>>();

        public bool Any => lists.Count > 0;

        public List<AttentionEntry> Get(string person, string act) =>
            lists.TryGetValue(person, out var acts) && acts.TryGetValue(act, out var l) ? l : null;

        public void Report(string person, string act, string itemId, double t)
        {
            if (!lists.TryGetValue(person, out var acts)) lists[person] = acts = new Dictionary<string, List<AttentionEntry>>();
            if (!acts.TryGetValue(act, out var l)) acts[act] = l = new List<AttentionEntry>();
            l.RemoveAll(e => e.id == itemId);
            foreach (var e in l) e.active = false;
            l.Insert(0, new AttentionEntry { id = itemId, t = t, active = true });
            if (l.Count > 10) l.RemoveRange(10, l.Count - 10);
        }

        public void Release(string person, string act)
        {
            var l = Get(person, act);
            if (l != null && l.Count > 0) l[0].active = false;
        }

        public void Clear() => lists.Clear();
    }

    public partial class SceneWorld
    {
        public const int ActionLimit = 3, UndoMaxSteps = 3, ChangedCap = 20, HistoryKept = 10;
        public const double ActionMaxAge = 300, AttentionMaxAge = 30, PendingMaxAge = 60;
        const int AttentionLimit = 3;
        static readonly HashSet<string> ReadOnly = new HashSet<string> { "find_objects", "ask_clarification" };

        [Header("V1 (schema 1.1)")]
        [Tooltip("The people in the scene, when more than one person can give commands (for example a technician and a " +
                 "remote expert). Empty: one user.")]
        public List<ScenePerson> people = new List<ScenePerson>();
        [Tooltip("Who gives the current command (a person id). SceneAgentSession sets it per command.")]
        public string speaker = "user";
        [Tooltip("Whose hands the agent controls (a person id).")]
        public string actor = "user";
        [Tooltip("At the start of every V1 command, items whose transform was moved more than 2 cm or turned more than 5 " +
                 "degrees away from their logical state (by physics or by your own code) are taken as moved by a person.")]
        public bool syncFromTransforms;
        /// <summary>The clock in seconds (default: Time.realtimeSinceStartupAsDouble). Tests set it.</summary>
        [NonSerialized] public Func<double> clock;

        public readonly Dictionary<string, int> versions = new Dictionary<string, int>();
        public readonly List<CommandRecord> history = new List<CommandRecord>();
        public PendingQuestion pending;
        public readonly AttentionLedger attention = new AttentionLedger();
        /// <summary>Items whose rotation the simulator holds explicitly (rotated at least once). Only matters for the
        /// "changed" result of a rotation by a whole turn.</summary>
        public readonly HashSet<string> rotationSet = new HashSet<string>();

        class OpenCommand
        {
            public int id;
            public string request;
            public double t;
            public readonly List<KeyValuePair<string, ItemState>> before = new List<KeyValuePair<string, ItemState>>();
            public PlayerState playerBefore;
            public readonly List<RecordedCall> accepted = new List<RecordedCall>(), attempted = new List<RecordedCall>();
            public PendingQuestion pending;
            /// <summary>Items changed from outside after this command had changed them (sim_v1 "tainted").</summary>
            public readonly HashSet<string> tainted = new HashSet<string>();
        }

        OpenCommand cmd;
        bool v1Order;
        List<SceneItem> itemOrder;

        public double Now() => clock != null ? clock() : Time.realtimeSinceStartupAsDouble;
        public bool InCommand => cmd != null;
        /// <summary>Stateful calls accepted so far in the open command.</summary>
        public int AcceptedInCommand => cmd?.accepted.Count ?? 0;
        public int Ver(string id) => id != null && versions.TryGetValue(id, out var v) ? v : 0;

        /// <summary>The items in a stable order: the hierarchy order when first asked, then kept across reparenting (a
        /// held item moves in the hierarchy), new items appended. The simulator's world order.</summary>
        public List<SceneItem> OrderedItems()
        {
            var cur = Items.ToList();
            if (itemOrder == null) return itemOrder = cur;
            var live = new HashSet<SceneItem>(cur);
            itemOrder.RemoveAll(x => x == null || !live.Contains(x));
            var known = new HashSet<SceneItem>(itemOrder);
            foreach (var x in cur) if (!known.Contains(x)) itemOrder.Add(x);
            return itemOrder;
        }

        /// <summary>Forget the kept order (after a scene reload); the next call takes the hierarchy order again.</summary>
        public void ResetItemOrder() => itemOrder = null;

        /// <summary>Clear history, the open question, attention and versions (a scene reload or a new session).</summary>
        public void ResetHistory()
        {
            // a running command would lose its record and its later calls would fail (audit 04): cancel it first
            if (cmd != null) throw new InvalidOperationException("a command is running: cancel it before resetting the history");
            history.Clear(); pending = null; attention.Clear(); versions.Clear();  // rotation presence is world state: kept
        }

        // ------------------------------------------------------------------ snapshots
        static string Nn(string s) => string.IsNullOrEmpty(s) ? null : s;

        public ItemState Snap(SceneItem o)
        {
            bool rot = rotationSet.Contains(o.itemId) || o.yaw != 0 || o.pitch != 0 || o.roll != 0;
            return new ItemState {
                room = o.room, on = Nn(o.on), relation = Nn(o.relation), orientation = Nn(o.orientation), state = Nn(o.state),
                pos = o.hasPos && o.pos != null ? (double[])o.pos.Clone() : null, hasRot = rot,
                yaw = rot ? o.yaw : 0, pitch = rot ? o.pitch : 0, roll = rot ? o.roll : 0,
                carry = o.carry != null ? (double[])o.carry.Clone() : null, ver = Ver(o.itemId), instance = o };
        }

        void Restore(SceneItem o, ItemState st)
        {
            o.room = st.room; o.on = st.on; o.relation = st.relation; o.orientation = st.orientation; o.state = st.state;
            o.hasPos = st.pos != null;
            if (st.pos != null) o.pos = (double[])st.pos.Clone();
            if (st.hasRot) rotationSet.Add(o.itemId); else rotationSet.Remove(o.itemId);
            o.yaw = st.yaw; o.pitch = st.pitch; o.roll = st.roll;
            o.carry = st.carry != null ? (double[])st.carry.Clone() : null;
            versions[o.itemId] = st.ver;
        }

        PlayerState Player() => new PlayerState { room = playerRoom, leftHand = Nn(leftHand), rightHand = Nn(rightHand) };

        static Dictionary<string, object> PlayerView(PlayerState p) => new Dictionary<string, object> {
            { "room", p.room }, { "left_hand", p.leftHand }, { "right_hand", p.rightHand } };

        // ------------------------------------------------------------------ formatting (sim_v1 call_str, obj_view)
        /// <summary>Python str() of a value, as sim_v1._fmt writes call arguments: integral numbers without ".0", lists as
        /// [a, b], True / False / None.</summary>
        public static string PyStr(object v)
        {
            switch (v)
            {
                case null: return "None";
                case bool b: return b ? "True" : "False";
                case string s: return s;
                case double d: return PyNum(d);
                case float f: return PyNum(f);
                case int n: return n.ToString(CultureInfo.InvariantCulture);
                case long n: return n.ToString(CultureInfo.InvariantCulture);
                case IDictionary<string, object> dict:
                    return "{" + string.Join(", ", dict.Select(kv => PyRepr(kv.Key) + ": " + PyReprValue(kv.Value))) + "}";
                case System.Collections.IEnumerable e:
                    return "[" + string.Join(", ", e.Cast<object>().Select(PyStr)) + "]";
                default: return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }

        static string PyNum(double d)
        {
            if (!double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Floor(d) && Math.Abs(d) < 9.2e18)
                return ((long)d).ToString(CultureInfo.InvariantCulture);
            return PyFloatRepr(d);
        }

        /// <summary>Python repr() of a float.</summary>
        public static string PyFloatRepr(double d)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
            string s = MiniJson.FormatNumber(d);
            int e = s.IndexOf('E');
            if (e < 0) return s;
            // .NET "1E-05" / "1.5E+16"; Python "1e-05" / "1.5e+16"
            string mant = s.Substring(0, e), exp = s.Substring(e + 1);
            string sign = exp.StartsWith("-") ? "-" : "+";
            exp = exp.TrimStart('+', '-').TrimStart('0');
            if (exp.Length < 2) exp = exp.PadLeft(2, '0');
            return mant + "e" + sign + exp;
        }

        static string PyReprValue(object v) => v is string s ? PyRepr(s) : PyStrValue(v);

        /// <summary>Python str() of a value, as in an error message ("invalid steps '['2']'"): unlike call strings, list
        /// items are shown with repr. JSON does not tell 4 from 4.0, so an integral number prints as an int.</summary>
        public static string PyStrValue(object v)
        {
            switch (v)
            {
                case null: return "None";
                case bool b: return b ? "True" : "False";
                case string s: return s;
                case double d: return d == 0 && BitConverter.DoubleToInt64Bits(d) < 0 ? "-0.0" : PyNum(d);  // -0 is a float
                case IDictionary<string, object> dict:
                    return "{" + string.Join(", ", dict.Select(kv => PyRepr(kv.Key) + ": " + PyReprValue(kv.Value))) + "}";
                case System.Collections.IEnumerable e:
                    return "[" + string.Join(", ", e.Cast<object>().Select(PyReprValue)) + "]";
                default: return PyStr(v);
            }
        }

        /// <summary>Python repr() of a string.</summary>
        public static string PyRepr(string s)
        {
            char q = s.Contains("'") && !s.Contains("\"") ? '"' : '\'';
            var sb = new System.Text.StringBuilder().Append(q);
            foreach (char c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == q) sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ' || c == 0x7f) sb.Append("\\x").Append(((int)c).ToString("x2"));
                else sb.Append(c);
            }
            return sb.Append(q).ToString();
        }

        /// <summary>The message of Python's float(v) for a value it cannot convert (the v1 "bad arguments" text).</summary>
        static string PyFloatError(object v)
        {
            switch (v)
            {
                case string s: return "could not convert string to float: " + PyRepr(s);
                case IDictionary<string, object> _: return "float() argument must be a string or a real number, not 'dict'";
                case System.Collections.IEnumerable _: return "float() argument must be a string or a real number, not 'list'";
                default: return "float() argument must be a string or a real number, not '" + v.GetType().Name + "'";
            }
        }

        /// <summary>A call in the syntax the scene shows: set_state(valve_2, open).</summary>
        public static string CallStr(RecordedCall c) => c.name + "(" + string.Join(", ", c.args.Select(kv => PyStr(kv.Value))) + ")";

        /// <summary>An item as visible_objects shows it: id, label, color?, on?, state?, pos? (rounded to 0.1).</summary>
        public static Dictionary<string, object> ObjView(SceneItem o)
        {
            var v = new Dictionary<string, object> { { "id", o.itemId }, { "label", o.label } };
            if (!string.IsNullOrEmpty(o.color)) v["color"] = o.color;
            if (!string.IsNullOrEmpty(o.on)) v["on"] = o.on;
            if (!string.IsNullOrEmpty(o.state)) v["state"] = o.state;
            if (o.hasPos && o.pos != null) v["pos"] = o.pos.Select(c => (object)SceneMath.Round(c, 1)).ToList();
            return v;
        }

        void AddChanged(Dictionary<string, object> res, HashSet<string> ids)
        {
            var objs = OrderedItems().Where(o => ids.Contains(o.itemId)).Select(o => (object)ObjView(o)).ToList();
            res["changed"] = objs.Take(ChangedCap).ToList();
            if (objs.Count > ChangedCap) { res["truncated"] = true; res["more"] = objs.Count - ChangedCap; }
        }

        // ------------------------------------------------------------------ commands
        /// <summary>A request starts (sim_v1.begin). The open question is answered by it; highlights and offered
        /// candidates belong to the previous command. Build the request's scene before calling this.</summary>
        int commandSeq;

        /// <returns>The command's id, for EndCommand(id).</returns>
        public int BeginCommand(string request, double now)
        {
            if (cmd != null) EndCommand();  // a command that was stopped from outside (StopCoroutine) is closed first
            pending = null;
            var lit = highlighted.ToList();
            highlighted.Clear();
            clarify = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var o in Items) if (lit.Contains(o.itemId)) Sync(o);
            cmd = new OpenCommand { id = ++commandSeq, request = request, t = now };
            return cmd.id;
        }

        /// <summary>The command ends (answer, question, error or turn limit): its history entry and open question.</summary>
        /// <summary>Ends the command with this id; a stale caller whose command is already over changes nothing.</summary>
        public void EndCommand(int id)
        {
            if (cmd != null && cmd.id == id) EndCommand();
        }

        public void EndCommand()
        {
            if (cmd == null) return;
            var c = cmd;
            cmd = null;
            CommandRecord entry = null;
            if (c.accepted.Count > 0)
                entry = new CommandRecord {
                    t = c.t, calls = c.accepted.ToList(), status = "done", undoable = !c.accepted.Any(Irreversible),
                    before = c.before.ToList(), playerBefore = c.playerBefore,
                    vers = c.before.Select(kv => new KeyValuePair<string, int>(kv.Key, c.tainted.Contains(kv.Key) ? -1 : Ver(kv.Key))).ToList() };
            else if (c.attempted.Count > 0)
                entry = new CommandRecord { t = c.t, calls = c.attempted.ToList(), status = "failed", undoable = false };
            if (entry != null)
            {
                history.Insert(0, entry);
                if (history.Count > HistoryKept) history.RemoveRange(HistoryKept, history.Count - HistoryKept);
            }
            if (c.pending != null) { c.pending.t = c.t; pending = c.pending; }
        }

        bool Irreversible(RecordedCall c)
        {
            if (c.name != "press") return false;
            string id = c.Arg("object_id") as string;
            var o = Items.FirstOrDefault(x => x.itemId == id);
            return o == null || !o.reversiblePress;
        }

        /// <summary>A stateful call that failed inside the open command (a guard rejection, a broken call).</summary>
        public void RecordAttempt(string name, Dictionary<string, object> a)
        {
            if (cmd != null && name != "undo" && !ReadOnly.Contains(name ?? "")) cmd.attempted.Add(new RecordedCall(name, a));
        }

        /// <summary>One call inside the open command (sim_v1.apply): returns the v1 result; throws ToolError (or another
        /// exception for a value the tool cannot use) when the call is not possible, and records the attempt.</summary>
        public object ExecuteV1(string name, Dictionary<string, object> a)
        {
            if (cmd == null) throw new InvalidOperationException("ExecuteV1 outside a command: call BeginCommand first");
            a = a ?? new Dictionary<string, object>();
            object result = name == "undo" ? Undo(a) : Apply(name, a);
            if (ToolExecuted != null)
                foreach (Action<string, Dictionary<string, object>, object> h in ToolExecuted.GetInvocationList())
                {
                    try { h(name, a, result); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            return result;
        }

        object Apply(string name, Dictionary<string, object> a)
        {
            var items = OrderedItems();
            var snap = items.ToDictionary(o => o, Snap);
            var p0 = Player();
            object res;
            v1Order = true;
            try { res = ExecuteTool(name, a); }
            catch (Exception)
            {
                RecordAttempt(name, a);
                throw;
            }
            finally { v1Order = false; }
            if (ReadOnly.Contains(name))
            {
                if (name == "ask_clarification")
                    cmd.pending = new PendingQuestion { request = cmd.request, question = S(a, "question"), candidateIds = L(a, "candidate_ids") };
                return res;
            }
            if (name == "rotate") rotationSet.Add(S(a, "object_id"));
            var changed = new HashSet<string>();
            foreach (var o in items)
            {
                if (snap[o].SameAs(Snap(o))) continue;
                changed.Add(o.itemId);
                if (!cmd.before.Any(kv => kv.Key == o.itemId))
                    cmd.before.Add(new KeyValuePair<string, ItemState>(o.itemId, snap[o]));
                versions[o.itemId] = Ver(o.itemId) + 1;
            }
            var p1 = Player();
            bool moved = !p1.SameAs(p0);
            if (moved && cmd.playerBefore == null) cmd.playerBefore = p0;
            cmd.accepted.Add(new RecordedCall(name, a));
            if (name == "teleport")
                return new Dictionary<string, object> {
                    { "ok", true }, { "room", p1.room },
                    { "visible_objects", items.Where(o => o.room == p1.room).Select(o => (object)ObjView(o)).ToList() },
                    { "player", PlayerView(p1) } };
            var outp = new Dictionary<string, object> { { "ok", true } };
            if (name != "highlight" && name != "press") AddChanged(outp, changed);
            if (moved) outp["player"] = PlayerView(p1);
            return outp;
        }

        /// <summary>The history entries undo can reach and the scene shows: at most 3, younger than 300 s.</summary>
        public List<CommandRecord> VisibleHistory(double now) =>
            history.Where(e => now - e.t <= ActionMaxAge).Take(ActionLimit).ToList();

        /// <summary>undo(steps): reverses whole commands exactly (sim_v1._undo). The batch is checked before anything
        /// changes; restore runs newest first, versions included; then every restored item is shown once.</summary>
        object Undo(Dictionary<string, object> a)
        {
            object raw = a.TryGetValue("steps", out var sv) ? sv : 1.0;
            double sd = raw is double d ? d : raw is int n ? n : raw is long l ? l : raw is float f ? f : double.NaN;
            if (raw is bool || double.IsNaN(sd) || sd != Math.Floor(sd) || sd < 1 || sd > UndoMaxSteps)
                throw new ToolError($"invalid steps '{PyStrValue(raw)}'");
            int steps = (int)sd;
            var vis = VisibleHistory(cmd.t);
            if (vis.Count == 0) throw new ToolError("nothing to undo");
            if (steps > vis.Count) throw new ToolError($"cannot undo: only {vis.Count} commands to undo");
            var batch = vis.Take(steps).ToList();
            foreach (var e in batch)
            {
                if (e.status != "done") throw new ToolError($"cannot undo: {CallStr(e.calls[0])} did not happen");
                if (!e.undoable)
                {
                    var c = e.calls.FirstOrDefault(Irreversible) ?? e.calls[0];
                    throw new ToolError($"cannot undo: {CallStr(c)} cannot be reversed");
                }
            }
            var expected = new List<KeyValuePair<string, int>>();
            foreach (var e in batch)  // newest first: the version after the newest command that touched the item
                foreach (var kv in e.vers)
                    if (!expected.Any(x => x.Key == kv.Key)) expected.Add(kv);
            var byId = new Dictionary<string, SceneItem>();
            foreach (var o in OrderedItems()) if (!byId.ContainsKey(o.itemId)) byId[o.itemId] = o;
            foreach (var kv in expected)
                if (!byId.ContainsKey(kv.Key) || Ver(kv.Key) != kv.Value) throw new ToolError($"cannot undo: {kv.Key} was changed since");
            // continuity (sim_v1, audit 06): from the newest record to the oldest, each older record of an item must end
            // at the version the newer one started from; a gap (or -1, a change during the command) is a person's change
            var started = new Dictionary<string, int>();
            foreach (var e in batch)
            {
                foreach (var kv in e.vers)
                    if (started.TryGetValue(kv.Key, out var from) && from != kv.Value) throw new ToolError($"cannot undo: {kv.Key} was changed since");
                foreach (var kv in e.before) started[kv.Key] = kv.Value.ver;
            }
            foreach (var e in batch)
                foreach (var kv in e.before)
                {
                    // gone, or its id now names another instance (destroyed and created again). ReferenceEquals: a
                    // destroyed Unity object compares equal to null with ==, which would skip this check (audit 06)
                    var inst = kv.Value.instance;
                    if (!byId.ContainsKey(kv.Key) || (!ReferenceEquals(inst, null) && !ReferenceEquals(inst, byId[kv.Key])))
                        throw new ToolError($"cannot undo: {kv.Key} was changed since");
                }
            // valid: restore newest first, so an older record of the same item wins (it is restored later)
            var restored = new HashSet<string>();
            PlayerState player = null;
            foreach (var e in batch)
            {
                foreach (var kv in e.before) { Restore(byId[kv.Key], kv.Value); restored.Add(kv.Key); }
                if (e.playerBefore != null) player = e.playerBefore;
            }
            if (player != null) { playerRoom = player.room; leftHand = player.leftHand; rightHand = player.rightHand; }
            foreach (var e in batch) history.Remove(e);
            var outp = new Dictionary<string, object> { { "ok", true },
                { "undone", batch.SelectMany(e => e.calls).Select(c => (object)CallStr(c)).ToList() } };
            AddChanged(outp, restored);
            if (player != null) outp["player"] = PlayerView(Player());
            // visuals once, after the whole batch: held roots before what they carry
            var touched = OrderedItems().Where(o => restored.Contains(o.itemId)).ToList();
            foreach (var o in touched) Sync(o);
            foreach (var o in touched) if (!o.hasPos && o.carry != null) Sync(o);
            return outp;
        }

        // ------------------------------------------------------------------ changes made outside the agent
        /// <summary>A change made by a person, by physics or by your own code (sim_v1.external_change): run update on
        /// the logical state; every item it changed gets a new version (the given item always), so undo will not
        /// overwrite the change. Never a history entry.</summary>
        public void ApplyExternalChange(SceneItem item, Action<SceneItem> update)
        {
            var items = OrderedItems();
            var snap = items.ToDictionary(o => o, Snap);
            update?.Invoke(item);
            foreach (var o in items)
                if (o == item || !snap[o].SameAs(Snap(o)))
                {
                    versions[o.itemId] = Ver(o.itemId) + 1;
                    // during a command that already changed it: undo of that command must refuse (sim_v1, audit 04)
                    if (cmd != null && cmd.before.Any(kv => kv.Key == o.itemId)) cmd.tainted.Add(o.itemId);
                    Sync(o);
                }
        }

        /// <summary>syncFromTransforms: items moved in the scene without the agent become external changes. Only the part
        /// that changed beyond its tolerance is copied: a push leaves the logical angles as they were (eulerAngles may
        /// spell the same rotation differently, e.g. pitch 180 as yaw 180 and roll 180, or add float noise), and a turn
        /// in place leaves the logical position as it was.</summary>
        public void SyncFromTransforms()
        {
            foreach (var o in OrderedItems())
            {
                if (!o.hasPos || o.pos == null || HandSlots.Contains(o.on) || o.transform.parent != transform) continue;
                var tr = o.transform;
                var p = tr.position - (roomOrigin != null ? roomOrigin(o.room) : Vector3.zero);
                var logical = new Vector3((float)o.pos[0], (float)o.pos[1] + tr.localScale.y / 2f, (float)o.pos[2]);
                var e = tr.rotation.eulerAngles;
                bool moved = (p - logical).magnitude > 0.02f;
                bool turned = Quaternion.Angle(tr.rotation, Quaternion.Euler((float)o.pitch, (float)o.yaw, (float)o.roll)) > 5f;
                if (!moved && !turned) continue;
                ApplyExternalChange(o, x =>
                {
                    if (moved) x.pos = new[] { SceneMath.Round(p.x, 3), SceneMath.Round(p.y - tr.localScale.y / 2f, 3), SceneMath.Round(p.z, 3) };
                    if (turned) { x.yaw = SceneMath.Round(e.y, 3); x.pitch = SceneMath.Round(e.x, 3); x.roll = SceneMath.Round(e.z, 3); }
                });
            }
        }

        // ------------------------------------------------------------------ attention
        public void ReportAttention(string person, string act, string itemId) => attention.Report(person, act, itemId, Now());
        public void ReleaseAttention(string person, string act) => attention.Release(person, act);

        /// <summary>player.gaze in V1: the user's active gaze target (sim_v1.settled_gaze), or null.</summary>
        public string SettledGaze()
        {
            var g = attention.Get("user", "gaze");
            return g != null && g.Count > 0 && g[0].active ? g[0].id : null;
        }

        IEnumerable<string> PeopleIds() => people.Count > 0 ? people.Select(p => p.id) : new[] { "user" };

        static int Ago(double now, double t) => (int)Math.Round(now - t, MidpointRounding.ToEven);

        // ------------------------------------------------------------------ the v1 scene
        /// <summary>The optional v1 scene fields at time now, in their rendered order; empty ones are left out.</summary>
        public Dictionary<string, object> V1Fields(double now)
        {
            var outp = new Dictionary<string, object>();
            if (people.Count > 1)
            {
                var pd = new Dictionary<string, object>();
                foreach (var p in people) pd[p.id] = p.role;
                outp["people"] = pd;
                outp["speaker"] = speaker ?? "user";
                outp["actor"] = actor ?? "user";
            }
            var inRoom = new HashSet<string>(Items.Where(o => o.room == playerRoom).Select(o => o.itemId));
            var att = new Dictionary<string, object>();
            foreach (var who in PeopleIds())
            {
                var acts = new Dictionary<string, object>();
                foreach (var act in new[] { "gaze", "point" })
                {
                    var l = attention.Get(who, act);
                    if (l == null) continue;
                    var es = l.Where(e => inRoom.Contains(e.id) && now - e.t <= AttentionMaxAge).Take(AttentionLimit)
                              .Select(e => (object)new Dictionary<string, object> { { "id", e.id }, { "ago_s", Ago(now, e.t) }, { "active", e.active } })
                              .ToList();
                    if (es.Count > 0) acts[act] = es;
                }
                if (acts.Count > 0) att[who] = acts;
            }
            if (att.Count > 0) outp["attention"] = att;
            var hist = VisibleHistory(now);
            if (hist.Count > 0)
                outp["recent_actions"] = hist.Select(e => (object)new Dictionary<string, object> {
                    { "ago_s", Ago(now, e.t) }, { "calls", e.calls.Select(c => (object)CallStr(c)).ToList() },
                    { "status", e.status }, { "undoable", e.undoable } }).ToList();
            if (pending != null && now - pending.t <= PendingMaxAge)
                outp["pending_clarification"] = new Dictionary<string, object> {
                    { "ago_s", Ago(now, pending.t) }, { "request", pending.request }, { "question", pending.question },
                    { "candidate_ids", pending.candidateIds.Cast<object>().ToList() } };
            return outp;
        }

        /// <summary>The V1 scene for a request at time now: the v0 scene (gaze from the attention ledger when it has
        /// entries) plus the v1 fields. order: the visible objects' order (tests); null = the kept item order.</summary>
        public string BuildSceneJsonV1(double now, IList<string> order = null)
        {
            var vis = OrderedItems().Where(o => o.room == playerRoom).ToList();
            if (order != null)
            {
                var rank = new Dictionary<string, int>();
                for (int i = 0; i < order.Count; i++) if (!rank.ContainsKey(order[i])) rank[order[i]] = i;
                vis = vis.Select((o, i) => (o, i)).OrderBy(x => rank.TryGetValue(x.o.itemId, out var r) ? r : order.Count + x.i)
                         .Select(x => x.o).ToList();
            }
            string g = attention.Any ? SettledGaze() : Nn(gaze);
            var scene = new Dictionary<string, object> {
                { "player", new Dictionary<string, object> { { "room", playerRoom }, { "gaze", g }, { "left_hand", Nn(leftHand) }, { "right_hand", Nn(rightHand) } } },
                { "locations", rooms.Cast<object>().ToList() },
                { "visible_objects", vis.Select(o => (object)ObjView(o)).ToList() } };
            foreach (var kv in V1Fields(now)) scene[kv.Key] = kv.Value;
            return MiniJson.Serialize(scene);
        }
    }
}
