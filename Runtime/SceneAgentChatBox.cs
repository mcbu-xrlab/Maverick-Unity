// A minimal on-screen chat for trying the agent without writing any UI (flat screen; for XR, drive SceneAgentSession
// from your own UI or speech input). Type a command, press Enter; when the model asks which object you mean, click it.
// F1 hides / shows the box.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SceneAgent
{
    [RequireComponent(typeof(SceneAgentSession))]
    public class SceneAgentChatBox : MonoBehaviour
    {
        public int visibleLines = 12;
        public KeyCode toggleKey = KeyCode.F1;
        [Tooltip("Commands offered as one-click examples while the box is empty.")]
        public string[] examples = { "put the red mug on the table", "turn on the lamp", "where is the book?" };

        SceneAgentSession session;
        LocalModelServer server;
        readonly List<string> lines = new List<string>();
        string input = "";
        bool visible = true;
        Vector2 scroll;
        GUIStyle wrap;

        void Awake()
        {
            session = GetComponent<SceneAgentSession>();
            session.Line += l => { lines.Add(l); scroll.y = float.MaxValue; };
        }

        void Start()  // after every Awake: the session has found its agent by now
        {
            // no ?? here: Unity objects compare to null through their own operator
            server = session.agent != null ? session.agent.server : null;
            if (server == null) server = GetComponent<LocalModelServer>();
            if (server == null) server = FindObjectOfType<LocalModelServer>();
        }

        void Send(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || session.Busy) return;
            input = "";
            StartCoroutine(session.Command(text));
        }

        void OnGUI()
        {
            // the toggle through IMGUI events: works with the old Input Manager and with the Input System package
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == toggleKey) { visible = !visible; Event.current.Use(); }
            if (!visible) return;
            wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            float w = Mathf.Min(Screen.width - 20, 900), h = 60 + visibleLines * 20;
            var area = new Rect(10, Screen.height - h - 10, w, h);
            GUI.Box(area, "");
            GUILayout.BeginArea(new Rect(area.x + 8, area.y + 6, area.width - 16, area.height - 12));

            string st = server == null ? "no LocalModelServer: using " + session.agent.endpoint
                : server.Ready ? server.status : server.Failed ? "⚠ " + server.status : server.status;
            string gaze = string.IsNullOrEmpty(session.world.gaze) ? "" : $" · pointing at: {session.world.gaze}";
            GUILayout.Label($"Scene Agent · room: {session.world.playerRoom}{gaze} · {st}{(session.Busy ? " · thinking…" : "")}");

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(visibleLines * 20));
            foreach (var l in lines.Skip(Mathf.Max(0, lines.Count - 200))) GUILayout.Label(l, wrap);
            if (lines.Count == 0)
            {
                GUILayout.Label("Type a command in English, e.g.:");
                GUILayout.BeginHorizontal();
                foreach (var ex in examples) if (GUILayout.Button(ex)) input = ex;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (session.PendingRequest != null && !session.Busy)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Click one, or type e.g. 'the red one':", GUILayout.Width(240));
                foreach (var id in session.Candidates)
                {
                    var it = session.world.Items.FirstOrDefault(x => x.itemId == id);
                    if (it != null && GUILayout.Button($"{session.Describe(it)} ({id})")) StartCoroutine(session.Answer(id));
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("SceneAgentInput");
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
                         && GUI.GetNameOfFocusedControl() == "SceneAgentInput";
            input = GUILayout.TextField(input, GUILayout.ExpandWidth(true));
            GUI.enabled = !session.Busy;
            if (GUILayout.Button("Send", GUILayout.Width(70)) || enter) Send(input);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
