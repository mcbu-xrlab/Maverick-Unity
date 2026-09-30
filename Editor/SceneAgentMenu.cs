// Editor tools for developers: Tools > Scene Agent >
//   Set Up Scene               one "Scene Agent" object with SceneWorld, SceneAgent, LocalModelServer, a session and the
//                              chat box, wired together (idempotent)
//   Make Selected Interactive  SceneItem on every selected object: id, label (and a colour word) from its name, the room,
//                              what it stands on (from the renderers' bounds); moved under the Scene Agent object
//   Validate Scene             ids, labels the model's catalogue does not know, supports, rooms, model files
//   Open Model Folder          StreamingAssets/SceneAgent, where llama-server and the .gguf go
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SceneAgent.EditorTools
{
    public static class SceneAgentMenu
    {
        const string Root = "Tools/Scene Agent/";
        /// <summary>Automation (tests, build scripts): no modal dialogs, results only in the Console.</summary>
        public static bool Quiet;
        static readonly string[] Colours = { "red", "green", "blue", "yellow", "white", "black", "gray", "grey", "orange",
                                             "purple", "pink", "brown", "silver", "gold", "beige" };

        static T Ensure<T>(GameObject g) where T : Component
        {
            var c = g.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(g);
        }

        [MenuItem(Root + "Set Up Scene", priority = 1)]
        public static void SetUpScene()
        {
            var world = Object.FindObjectOfType<SceneWorld>();
            if (world == null)
            {
                var go = new GameObject("Scene Agent");
                Undo.RegisterCreatedObjectUndo(go, "Set Up Scene Agent");
                world = go.AddComponent<SceneWorld>();
                world.initFromTransforms = true;
            }
            var root = world.gameObject;
            var agent = Ensure<SceneAgent>(root);
            var server = Ensure<LocalModelServer>(root);
            var session = Ensure<SceneAgentSession>(root);
            Ensure<SceneAgentChatBox>(root);
            var gaze = Ensure<SceneAgentGaze>(root);
            Undo.RecordObjects(new Object[] { agent, session, gaze }, "Set Up Scene Agent");
            gaze.world = world;
            gaze.session = session;
            agent.world = world;
            agent.server = server;
            session.agent = agent;
            session.world = world;
            if (string.IsNullOrEmpty(session.logDirectory)) session.logDirectory = "SceneAgentSessions";
            EditorUtility.SetDirty(root);
            Selection.activeGameObject = root;
            Debug.Log("[SceneAgent] Scene set up on \"" + root.name + "\". Next: select the objects the agent may use and run " +
                      "Tools > Scene Agent > Make Selected Interactive, then Validate Scene. " + ModelFilesStatus(server));
        }

        [MenuItem(Root + "Make Selected Interactive", priority = 2)]
        public static void MakeSelectedInteractive()
        {
            var world = Object.FindObjectOfType<SceneWorld>();
            if (world == null) { SetUpScene(); world = Object.FindObjectOfType<SceneWorld>(); }
            string room = !string.IsNullOrEmpty(world.playerRoom) ? world.playerRoom : world.rooms.FirstOrDefault() ?? "room";
            var selected = new HashSet<GameObject>(Selection.gameObjects);
            // ids taken by items that are not being (re)made now; a duplicate (Ctrl+D copies the SceneItem and its id)
            // gets a fresh id below
            var used = new HashSet<string>(world.Items.Where(i => !string.IsNullOrEmpty(i.itemId) && !selected.Contains(i.gameObject)).Select(i => i.itemId));
            var made = new List<SceneItem>();
            foreach (var go in Selection.gameObjects)
            {
                if (go == world.gameObject || go.GetComponent<SceneWorld>() != null) continue;
                var it = Ensure<SceneItem>(go);
                Undo.RecordObject(it, "Make Interactive");
                if (string.IsNullOrEmpty(it.label))
                {
                    var (colour, label) = LabelFromName(go.name);
                    it.label = label;
                    if (string.IsNullOrEmpty(it.color)) it.color = colour;
                }
                if (string.IsNullOrEmpty(it.itemId) || used.Contains(it.itemId)) it.itemId = NextId(it.label, used);
                else used.Add(it.itemId);
                if (string.IsNullOrEmpty(it.state)) it.state = DefaultState(it);
                if (string.IsNullOrEmpty(it.room)) it.room = room;
                // directly under the Scene Agent object: SceneWorld.Sync re-parents items there anyway, and an item nested in
                // another item would make that one's bounds (and so what stands on it) wrong
                if (go.transform.parent != world.transform) Undo.SetTransformParent(go.transform, world.transform, "Make Interactive");
                made.Add(it);
            }
            Undo.RecordObject(world, "Make Interactive");
            foreach (var r in made.Select(i => i.room).Distinct()) if (!world.rooms.Contains(r)) world.rooms.Add(r);
            if (string.IsNullOrEmpty(world.playerRoom)) world.playerRoom = room;
            InferSupports(world);
            var unknown = made.Where(i => !i.overrideAffordances && !KnownLabel(i.label)).Select(i => $"{i.itemId} ('{i.label}')").ToList();
            Debug.Log($"[SceneAgent] {made.Count} interactive: " + string.Join(", ", made.Select(i => $"{i.itemId} on {i.on}")) +
                      (unknown.Count > 0 ? $". Not in the model's catalogue (rename to a common word, or tick overrideAffordances): {string.Join(", ", unknown)}" : ""));
        }

        [MenuItem(Root + "Make Selected Interactive", true)]
        static bool CanMake() => Selection.gameObjects.Length > 0;

        [MenuItem(Root + "Validate Scene", priority = 3)]
        public static void ValidateScene()
        {
            var sb = new StringBuilder();
            int errors = 0, warnings = 0;
            void E(string m) { errors++; sb.AppendLine("ERROR   " + m); }
            void W(string m) { warnings++; sb.AppendLine("warning " + m); }
            var world = Object.FindObjectOfType<SceneWorld>();
            if (world == null)
            {
                Debug.LogWarning("[SceneAgent] Validate Scene: no SceneWorld in the scene: run Tools > Scene Agent > Set Up Scene.");
                if (!Application.isBatchMode && !Quiet) EditorUtility.DisplayDialog("Scene Agent", "No SceneWorld in the scene: run Tools > Scene Agent > Set Up Scene.", "OK");
                return;
            }
            var agent = world.GetComponent<SceneAgent>() != null ? world.GetComponent<SceneAgent>() : Object.FindObjectOfType<SceneAgent>();
            if (agent == null) E("no SceneAgent component");
            else if (agent.world == null) E("SceneAgent.world is not set");
            if (Resources.Load<TextAsset>("SceneAgent/system_prompt") == null && (agent == null || agent.systemPromptAsset == null))
                E("the system prompt is missing (package Resources/SceneAgent/system_prompt.txt)");
            var server = agent != null && agent.server != null ? agent.server : Object.FindObjectOfType<LocalModelServer>();
            if (server == null) W("no LocalModelServer: the agent will call " + (agent != null ? agent.endpoint : "?") + " (start a server yourself)");
            else sb.AppendLine("model   " + ModelFilesStatus(server));
            if (server != null)
            {
                string exe = LocalModelServer.Resolve(server.serverExe), model = server.ResolveModel(exe);
                if (exe == null || !File.Exists(exe)) E("llama-server not found: " + exe);
                if (model == null || !File.Exists(model)) E("model file not found: " + (model ?? "no .gguf next to llama-server"));
            }

            var items = Object.FindObjectsOfType<SceneItem>(true);
            if (items.Length == 0) E("no SceneItem: select objects and run Make Selected Interactive");
            foreach (var g in items.GroupBy(i => i.itemId ?? "").Where(g => g.Count() > 1)) E($"duplicate id '{g.Key}' on {string.Join(", ", g.Select(i => i.name))}");
            var ids = new HashSet<string>(items.Select(i => i.itemId));
            foreach (var it in items)
            {
                string n = $"'{it.name}'";
                if (string.IsNullOrEmpty(it.itemId)) E($"{n} has no id");
                else if (!Regex.IsMatch(it.itemId, "^[a-z0-9_]+$")) W($"{n}: id '{it.itemId}' should look like label_1 (lower case, digits, _)");
                if (string.IsNullOrEmpty(it.label)) E($"{n} has no label");
                else if (!it.overrideAffordances && !KnownLabel(it.label))
                    W($"{n}: label '{it.label}' is not in the model's catalogue; Unity will refuse grab/place/set_state on it " +
                      "unless you tick overrideAffordances (or use a common word)");
                if (!it.transform.IsChildOf(world.transform)) E($"{n} is not under '{world.name}' (SceneWorld only sees its children)");
                if (string.IsNullOrEmpty(it.state) && DefaultState(it) != null)
                    W($"{n}: has states ({string.Join("/", StatesOf(it))}) but none is set; the model will not know whether it is {DefaultState(it)}");
                if (string.IsNullOrEmpty(it.room)) E($"{n} has no room");
                else if (world.rooms.Count > 0 && !world.rooms.Contains(it.room)) E($"{n}: room '{it.room}' is not in SceneWorld.rooms");
                if (!string.IsNullOrEmpty(it.on) && it.on != "floor" && !it.on.EndsWith("_hand") && it.on != "both_hands" && !ids.Contains(it.on))
                    E($"{n}: stands on unknown id '{it.on}'");
            }
            foreach (var g in items.GroupBy(i => i.room ?? ""))
                if (g.Count() > 60) W($"room '{g.Key}' has {g.Count()} objects; the model sees one room at a time and 4096 tokens hold about 60 (split the room or raise contextSize)");
            if (!world.initFromTransforms) W("SceneWorld.initFromTransforms is off: positions come from each SceneItem.pos, not from where the objects stand");

            string head = $"{items.Length} objects, {world.rooms.Count} room(s), {errors} error(s), {warnings} warning(s)";
            Debug.Log("[SceneAgent] Validate Scene: " + head + "\n" + sb);
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("Scene Agent · Validate Scene", head + (errors + warnings > 0 ? "\n\nDetails in the Console." : "\n\nReady to press Play."), "OK");
        }

        [MenuItem(Root + "Open Model Folder", priority = 20)]
        public static void OpenModelFolder()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "SceneAgent");
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(dir);
        }

        // ------------------------------------------------------------------ helpers
        static string ModelFilesStatus(LocalModelServer s)
        {
            string exe = LocalModelServer.Resolve(s.serverExe);
            string model = s.ResolveModel(exe);
            if (exe != null) exe = Path.GetFullPath(exe);
            if (model != null) model = Path.GetFullPath(model);
            bool e = exe != null && File.Exists(exe), m = model != null && File.Exists(model);
            return (e ? $"llama-server: {exe}" : $"llama-server MISSING ({exe})") + " · " +
                   (m ? $"model: {Path.GetFileName(model)} ({new FileInfo(model).Length / 1e9:F2} GB)" : "model (.gguf) MISSING") +
                   (e && m ? "" : " → Tools > Scene Agent > Open Model Folder, and see the package README 'Model files'.");
        }

        static string[] StatesOf(SceneItem it) => it.overrideAffordances ? (it.states ?? new string[0])
            : SceneCatalog.States.TryGetValue(it.label ?? "", out var s) ? s : new string[0];

        /// <summary>The state a device starts in when none is set: off, else closed, else its first state.</summary>
        public static string DefaultState(SceneItem it)
        {
            var s = StatesOf(it);
            return s.Length == 0 ? null : s.Contains("off") ? "off" : s.Contains("closed") ? "closed" : s[0];
        }

        static readonly HashSet<string> Known = new HashSet<string>(SceneCatalog.Grasp.Concat(SceneCatalog.Top.Keys)
            .Concat(SceneCatalog.Container).Concat(SceneCatalog.Fixed).Concat(SceneCatalog.States.Keys).Concat(SceneCatalog.Button));

        public static bool KnownLabel(string label) => label != null && Known.Contains(label);

        /// <summary>"RedMug (2)" → ("red", "mug"); "Floor_Lamp" → ("", "floor lamp").</summary>
        public static (string colour, string label) LabelFromName(string name)
        {
            string s = Regex.Replace(name, @"\s*\(\d+\)\s*$", "");                 // "Mug (2)"
            s = Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");                      // "RedMug"
            s = Regex.Replace(s, @"[_\-.]+", " ");
            s = Regex.Replace(s, @"\s*\d+$", "").Trim().ToLowerInvariant();       // "mug 2"
            var words = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
            string colour = "";
            if (words.Count > 1 && Colours.Contains(words[0])) { colour = words[0] == "grey" ? "gray" : words[0]; words.RemoveAt(0); }
            return (colour, string.Join(" ", words));
        }

        static string NextId(string label, HashSet<string> used)
        {
            string b = Regex.Replace((label ?? "item").ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            int n = 1;
            while (used.Contains($"{b}_{n}")) n++;
            used.Add($"{b}_{n}");
            return $"{b}_{n}";
        }

        /// <summary>What each item stands on: the highest other item whose top is just under its bottom and whose
        /// footprint holds its centre; otherwise the floor.</summary>
        public static void InferSupports(SceneWorld world)
        {
            var items = world.Items.Where(i => i.gameObject.activeInHierarchy).ToList();
            var bounds = items.ToDictionary(i => i, BoundsOf);
            foreach (var a in items)
            {
                if (a.on != null && (a.on.EndsWith("_hand") || a.on == "both_hands")) continue;
                var ba = bounds[a];
                SceneItem best = null;
                foreach (var b in items)
                {
                    if (b == a || b.room != a.room) continue;
                    var bb = bounds[b];
                    bool under = bb.max.y <= ba.min.y + 0.05f && bb.max.y >= ba.min.y - 0.15f;
                    bool holds = ba.center.x >= bb.min.x && ba.center.x <= bb.max.x && ba.center.z >= bb.min.z && ba.center.z <= bb.max.z;
                    if (under && holds && (best == null || bb.max.y > bounds[best].max.y)) best = b;
                }
                Undo.RecordObject(a, "Infer supports");
                a.on = best != null ? best.itemId : "floor";
                if (best != null && string.IsNullOrEmpty(a.relation)) a.relation = "on";
            }
        }

        static Bounds BoundsOf(SceneItem i)
        {
            // its own renderers only: not those of other items parented under it
            var rs = i.GetComponentsInChildren<Renderer>().Where(r => r.GetComponentInParent<SceneItem>() == i).ToArray();
            if (rs.Length == 0) return new Bounds(i.transform.position, i.transform.lossyScale);
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
