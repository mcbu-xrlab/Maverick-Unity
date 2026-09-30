// One conversation with the agent: runs commands, keeps the pending clarification, turns the user's pick into the
// follow-up the model was trained on (the original request plus a hint, with the gaze on the picked object), and
// optionally writes every exchange to a JSON-lines log (what was asked, the scene the model saw, calls, answer, time).
// SceneAgentChatBox and SceneAgentScriptRunner use it; your own UI can too.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SceneAgent
{
    public class SceneAgentSession : MonoBehaviour
    {
        public SceneAgent agent;
        public SceneWorld world;
        [Tooltip("Folder for a JSON-lines log of every command (relative = under Application.persistentDataPath). Empty: no log.")]
        public string logDirectory = "";
        [Tooltip("Line shows each tool call as raw JSON (for developers) instead of a short readable form.")]
        public bool rawToolLines;

        public bool Busy { get; private set; }
        public AgentResult LastResult { get; private set; }
        /// <summary>The request waiting for the user's pick after ask_clarification (null when none).</summary>
        public string PendingRequest { get; private set; }
        /// <summary>The ids the model offered in its last question.</summary>
        public List<string> Candidates => world != null ? world.clarify.ToList() : new List<string>();
        /// <summary>Human-readable lines: the request, each call and its result, the answer.</summary>
        public event Action<string> Line;
        public event Action<AgentResult> Finished;
        public string LogFile { get; private set; }

        int index = -1;

        void Awake()
        {
            if (agent == null) agent = GetComponent<SceneAgent>();
            if (world == null) world = agent != null && agent.world != null ? agent.world : GetComponent<SceneWorld>();
            // "Game.exe -sceneAgentScript commands.txt" works in any scene with a session, not only where a runner was
            // added (found by the user test: the flag was silently ignored)
            if (Environment.GetCommandLineArgs().Contains("-sceneAgentScript") && GetComponent<SceneAgentScriptRunner>() == null)
                gameObject.AddComponent<SceneAgentScriptRunner>();
        }

        /// <summary>Runs one user command (typed or recognised speech). While a question is pending, a short reply that
        /// tells the candidates apart ("the blue one", "the one on the shelf") answers it, in the follow-up form the model
        /// was trained on ("pick up the toolbox I mean the blue one"); anything else is a new command.</summary>
        public IEnumerator Command(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) yield break;
            if (PendingRequest != null && LooksLikeAnswer(text))
            {
                string reply = text.StartsWith("i mean", StringComparison.OrdinalIgnoreCase) ? text : "I mean " + text;
                string request = $"{PendingRequest} {reply}";
                PendingRequest = null;
                yield return Run(request, null);
                yield break;
            }
            PendingRequest = null;
            yield return Run(text, null);
        }

        static readonly HashSet<string> CommandVerbs = new HashSet<string> {
            "pick", "put", "place", "grab", "take", "drop", "release", "open", "close", "shut", "turn", "switch", "move",
            "push", "pull", "rotate", "spin", "highlight", "show", "find", "where", "bring", "fetch", "get", "go", "teleport",
            "press", "lift", "set", "make", "throw", "toss", "hand", "give", "lock", "unlock", "what", "how", "can", "could" };
        static readonly HashSet<string> AnswerStarts = new HashSet<string> {
            "the", "that", "this", "one", "it", "it's", "its", "number", "left", "right", "first", "second", "third", "last",
            "top", "bottom", "front", "back", "near", "closest", "furthest", "biggest", "smallest", "both", "neither" };

        /// <summary>Whether text typed while a question is pending answers it (see Command).</summary>
        public bool LooksLikeAnswer(string text)
        {
            var words = text.ToLowerInvariant().Split(new[] { ' ', ',', '.', '!', '?', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 8 || CommandVerbs.Contains(words[0])) return false;
            if (words.Length > 1 && words[0] == "i" && words[1] == "mean") return true;
            if (AnswerStarts.Contains(words[0])) return true;
            // a word that tells the offered objects apart: a colour, a label, what one of them stands on
            var cues = new HashSet<string>(Candidates.Select(id => world.Items.FirstOrDefault(x => x.itemId == id)).Where(x => x != null)
                .SelectMany(c => new[] { c.color, c.label, LabelOf(c.on) }).Where(s => !string.IsNullOrEmpty(s)).SelectMany(s => s.Split(' ')));
            return words.Any(cues.Contains);
        }

        /// <summary>Answers the pending question with the object the user picked.</summary>
        public IEnumerator Answer(string itemId)
        {
            var it = world.Items.FirstOrDefault(x => x.itemId == itemId);
            if (it == null || PendingRequest == null) yield break;
            var cands = world.clarify.Select(id => world.Items.FirstOrDefault(x => x.itemId == id)).Where(x => x != null).ToList();
            string request = $"{PendingRequest} {Hint(it, cands)}";
            PendingRequest = null;
            yield return Run(request, itemId);
        }

        IEnumerator Run(string request, string gazeItem)
        {
            Busy = true;
            world.highlighted.Clear();   // highlights and questions belong to the previous command
            world.clarify.Clear();
            if (gazeItem != null) world.gaze = gazeItem;
            Line?.Invoke("You: " + request);
            string scene = world.BuildSceneJson();
            AgentResult res = null;
            yield return agent.Run(request, r => res = r);
            foreach (var turn in res.turns)
                for (int i = 0; i < turn.calls.Count; i++)
                {
                    string result = i < turn.results.Count ? turn.results[i] : null;
                    Line?.Invoke(rawToolLines ? "  → " + turn.calls[i] + (result != null ? "  " + result : "") : "  → " + Pretty(turn.calls[i], result));
                }
            if (res.error != null) Line?.Invoke("Error: " + res.error);
            if (res.clarificationQuestion != null) { PendingRequest = request; Line?.Invoke("Agent: " + res.clarificationQuestion); }
            else if (!string.IsNullOrEmpty(res.finalText)) Line?.Invoke("Agent: " + res.finalText);
            foreach (var it in world.Items) world.Sync(it);
            Record(request, gazeItem, scene, res);
            LastResult = res;
            Busy = false;
            Finished?.Invoke(res);
        }

        /// <summary>"I mean the red one." / "I mean the one on the shelf." / "I mean the mug I'm looking at."</summary>
        public string Hint(SceneItem it, List<SceneItem> candidates)
        {
            var others = candidates.Where(c => c.itemId != it.itemId).ToList();
            if (!string.IsNullOrEmpty(it.color) && others.All(c => c.color != it.color)) return $"I mean the {it.color} one.";
            if (others.All(c => c.on != it.on))
                return it.on == "floor" ? "I mean the one on the floor." : $"I mean the one on the {LabelOf(it.on)}.";
            return $"I mean the {Describe(it)} I'm looking at.";
        }

        /// <summary>A tool call for people: place(hammer_1, toolbox_1, in) ok / not done: the reason / found 2.</summary>
        public static string Pretty(string callJson, string resultJson)
        {
            string call;
            try
            {
                var c = (Dictionary<string, object>)MiniJson.Parse(callJson);
                var a = c.TryGetValue("arguments", out var x) ? x as Dictionary<string, object> : null;
                string args = a == null ? "" : string.Join(", ", a.Values.Select(v => v is List<object> l
                    ? string.Join(" + ", l.Select(Convert.ToString)) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)));
                call = $"{c["name"]}({args})";
            }
            catch (Exception) { return callJson; }
            if (resultJson == null) return call;
            try
            {
                var r = MiniJson.Parse(resultJson);
                if (r is Dictionary<string, object> d && d.TryGetValue("ok", out var ok) && ok is bool b)
                    return call + (b ? "  ok" : "  not done: " + (d.TryGetValue("error", out var e) ? Convert.ToString(e) : "").Replace("not done: ", ""));
                if (r is List<object> found) return call + $"  found {found.Count}";
            }
            catch (Exception) { }
            return call + "  " + resultJson;
        }

        /// <summary>"red mug" / "lamp": what a button for this object should say.</summary>
        public string Describe(SceneItem it) => string.IsNullOrEmpty(it.color) ? it.label : $"{it.color} {it.label}";

        string LabelOf(string id) => world.Items.FirstOrDefault(x => x.itemId == id)?.label ?? id;

        void Record(string request, string answer, string scene, AgentResult res)
        {
            if (string.IsNullOrEmpty(logDirectory)) return;
            try
            {
                if (LogFile == null)
                {
                    string dir = Path.IsPathRooted(logDirectory) ? logDirectory : Path.Combine(Application.persistentDataPath, logDirectory);
                    Directory.CreateDirectory(dir);
                    LogFile = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
                }
                index++;
                var rec = new Dictionary<string, object> {
                    { "i", (double)index }, { "time", DateTime.Now.ToString("s") }, { "request", request }, { "clarification_answer", answer },
                    { "scene", scene }, { "calls", res.turns.SelectMany(t => t.calls).Cast<object>().ToList() },
                    { "results", res.turns.SelectMany(t => t.results).Cast<object>().ToList() },
                    { "check", res.turns.Select(t => (object)t.check).ToList() }, { "question", res.clarificationQuestion },
                    { "final_text", res.finalText }, { "error", res.error }, { "seconds", Math.Round(res.seconds, 2) },
                    { "model_calls", (double)res.turns.Count } };
                File.AppendAllText(LogFile, MiniJson.Serialize(rec) + "\n");
            }
            catch (Exception e) { Debug.LogWarning("[SceneAgent] session log: " + e.Message); }
        }
    }
}
