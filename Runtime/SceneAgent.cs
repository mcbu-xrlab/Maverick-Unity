// Talks to a local OpenAI-compatible server (llama.cpp llama-server with the Maverick GGUF, Ollama, ...),
// executes the returned tool calls on the SceneWorld
// and feeds tool results back until the model answers in text, asks the user a clarifying question
// (ask_clarification hands the turn back: the answer arrives as the next request), or runs out of turns.
//
// The loop is a coroutine: HTTP runs on a worker thread, tool calls run on the main thread
// (Unity APIs are main-thread only). Play mode: StartCoroutine(agent.Run(...)).
// Batch/editor code can drive it synchronously with RunBlocking(...).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneAgent
{
    [Serializable]
    public class AgentTurn
    {
        public string modelOutput;
        public List<string> calls = new List<string>();
        public List<string> results = new List<string>();
        /// <summary>The output check's verdict on this answer (SceneGrammar.Check): ok, invalid_tool, invalid_value,
        /// invalid_id or malformed. Null when the check is off.</summary>
        public string check;
    }

    public class AgentResult
    {
        public List<AgentTurn> turns = new List<AgentTurn>();
        public string finalText;
        /// <summary>Set when the model asked the user a question (ask_clarification); the turn ends there.</summary>
        public string clarificationQuestion;
        public string error;
        public double seconds;
    }

    public class SceneAgent : MonoBehaviour
    {
        public string endpoint = "http://127.0.0.1:8081/v1/chat/completions";
        public string model = "unity-scene-agent";
        [Tooltip("The model's system prompt. Leave empty to use the prompt the model was trained with " +
                 "(Resources/SceneAgent/system_prompt). Change it only for a model trained on your prompt.")]
        public TextAsset systemPromptAsset;
        [Tooltip("Optional: the LocalModelServer that runs the model. Run() then waits until it is ready and uses its endpoint.")]
        public LocalModelServer server;
        [NonSerialized] public string systemPrompt;
        public int maxTurns = 4;
        public SceneWorld world;
        [Tooltip("Constrain every answer with a grammar: only the 11 tools and ids that exist. Needs llama-server. " +
                 "Not recommended: it can force an action where the model should decline. Use the output check below.")]
        public bool constrainedDecoding = false;
        [Tooltip("Output check (recommended): the model answers freely, then a call to a tool or value that does not exist " +
                 "becomes a refusal, and an unknown object id or a broken call is generated again under a grammar. " +
                 "Used when constrainedDecoding is off.")]
        public bool fallbackDecoding = true;
        [Tooltip("Safety: when the request names an object that is only in another room and the model acts on a different " +
                 "object here instead, that call is not executed; the model is told where the named object is. Example: " +
                 "'put the drill on the workbench' with the drill in another room must not move the screwdriver.")]
        public bool guardSubstitutes = true;

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        static readonly Regex ToolCallRe = new Regex(@"<tool_call>\s*(.*?)\s*</tool_call>", RegexOptions.Singleline);
        static readonly Regex ThinkRe = new Regex(@"<think>.*?</think>\s*", RegexOptions.Singleline);
        // These calls end the agent's turn: the user's answer arrives as the next request.
        static readonly HashSet<string> Handoff = new HashSet<string> { "ask_clarification" };

        static string defaultPrompt;
        static string DefaultPrompt => defaultPrompt ??= Resources.Load<TextAsset>("SceneAgent/system_prompt")?.text.Replace("\r\n", "\n");
        // no ?. on systemPromptAsset: in the Editor an unassigned field is a "fake null" Unity object, and ?. would call
        // .text on it (UnassignedReferenceException on the first command in Play mode)
        string Prompt => systemPrompt ?? (systemPromptAsset != null ? systemPromptAsset.text.Replace("\r\n", "\n") : null) ?? DefaultPrompt
                         ?? throw new InvalidOperationException("no system prompt (the package's Resources/SceneAgent/system_prompt is missing)");

        public IEnumerator Run(string utterance, Action<AgentResult> done)
        {
            var t0 = DateTime.UtcNow;
            var res = new AgentResult();
            if (server != null)
            {
                while (!server.Ready && !server.Failed) yield return null;
                if (server.Failed)
                {
                    res.error = "model server not available: " + server.status;
                    res.seconds = (DateTime.UtcNow - t0).TotalSeconds;
                    done(res);
                    yield break;
                }
                endpoint = server.Endpoint;
            }
            string sceneJson = world.BuildSceneJson();
            var messages = new List<object> {
                Msg("system", Prompt),
                Msg("user", "Scene: " + sceneJson + "\nRequest: " + utterance) };
            var toolResults = new List<string>();  // ids returned here become nameable (grammar)
            for (int turn = 0; turn < maxTurns && res.error == null; turn++)
            {
                var req = new Dictionary<string, object> {
                    { "model", model }, { "messages", messages }, { "temperature", 0.0 }, { "max_tokens", 192 } };
                if (constrainedDecoding) req["grammar"] = SceneGrammar.Build(sceneJson, toolResults);
                string body = MiniJson.Serialize(req);
                var task = Task.Run(() => Post(body));  // worker thread: no Unity API inside
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted) { res.error = task.Exception?.GetBaseException().Message; break; }
                string output = task.Result, check = null;
                if (!constrainedDecoding && fallbackDecoding)
                {
                    check = SceneGrammar.Check(output, sceneJson, toolResults);
                    if (check == "invalid_tool" || check == "invalid_value") output = SceneGrammar.Refusal;
                    else if (check == "invalid_id" || check == "malformed")
                    {
                        req["grammar"] = SceneGrammar.Build(sceneJson, toolResults);
                        string again = MiniJson.Serialize(req);
                        var retry = Task.Run(() => Post(again));
                        while (!retry.IsCompleted) yield return null;
                        if (retry.IsFaulted) { res.error = retry.Exception?.GetBaseException().Message; break; }
                        output = retry.Result;
                    }
                }
                var at = new AgentTurn { modelOutput = output, check = check };
                res.turns.Add(at);
                var blocks = ToolCallRe.Matches(output).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                if (blocks.Count == 0)
                {
                    res.finalText = Clean(output);
                    break;
                }
                var toolMsgs = new List<object>();
                string question = null;
                foreach (var block in blocks)
                {
                    Dictionary<string, object> c;
                    try { c = (Dictionary<string, object>)MiniJson.Parse(block); }
                    catch (Exception e) { res.error = "unparseable tool call: " + e.Message; break; }
                    string name = Convert.ToString(c.TryGetValue("name", out var n) ? n : "");
                    var args = c.TryGetValue("arguments", out var a) && a is Dictionary<string, object> d ? d : new Dictionary<string, object>();
                    object result;
                    string guard = SubstituteGuard(utterance, name, args);
                    if (guard != null)
                    {
                        Debug.Log("[SceneAgent] call not executed: " + guard);
                        result = new Dictionary<string, object> { { "ok", false }, { "error", guard } };
                    }
                    else
                    {
                        try { result = world.Execute(name, args); }
                        catch (ToolError e) { result = new Dictionary<string, object> { { "ok", false }, { "error", e.Message } }; }
                    }
                    if (Handoff.Contains(name))
                        question = Convert.ToString(args.TryGetValue("question", out var q) ? q : "") ?? "";
                    string resultJson = PyJson(result);
                    at.calls.Add(block);
                    at.results.Add(resultJson);
                    toolResults.Add(resultJson);
                    toolMsgs.Add(new Dictionary<string, object> { { "role", "tool" }, { "name", name }, { "content", resultJson } });
                }
                // history: the model's own call text, then tool results (Qwen3 renders them as <tool_response>)
                messages.Add(Msg("assistant", Clean(output)));
                messages.AddRange(toolMsgs);
                if (question != null) { res.clarificationQuestion = question; break; }
            }
            res.seconds = (DateTime.UtcNow - t0).TotalSeconds;
            done(res);
        }

        /// <summary>Synchronous driver for editor/batch code (main thread).</summary>
        public AgentResult RunBlocking(string utterance)
        {
            AgentResult result = null;
            var it = Run(utterance, r => result = r);
            while (it.MoveNext()) Thread.Sleep(2);
            return result;
        }

        static readonly HashSet<string> ActingTools = new HashSet<string> { "grab", "place", "move_by", "rotate", "set_state", "press" };

        /// <summary>Why this call must not run (see guardSubstitutes), or null. It fires only when the acted-on object is
        /// not named in the request while the request names an object that exists only in other rooms.</summary>
        public string SubstituteGuard(string utterance, string tool, Dictionary<string, object> args)
        {
            if (!guardSubstitutes || world == null || !ActingTools.Contains(tool ?? "")) return null;
            string id = args.TryGetValue("object_id", out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
            var item = id == null ? null : world.Items.FirstOrDefault(x => x.itemId == id);
            if (item == null) return null;
            if (string.IsNullOrEmpty(item.label)) return null;
            // tested on 13,438 model calls: it fired 4 times, each time on a wrong action, with no false alarm
            string text = " " + Regex.Replace(utterance.ToLowerInvariant(), "[^a-z0-9]+", " ") + " ";
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string joined = string.Concat(words);
            IEnumerable<string> WordsOf(string label) => label.Split(' ').Where(w => w.Length >= 3);
            bool Phrase(string label) => text.Contains(" " + label + " ") || text.Contains(" " + label + "s ") || text.Contains(" " + label + "es ");
            // the acted-on object: any sign the user meant it ("box" -> toolbox, "note book" -> notebook, "keys" -> key chain)
            bool Loosely(SceneItem o)
            {
                string h = o.label.Split(' ').Last();
                return Phrase(o.label) || WordsOf(o.label).Any(w => words.Contains(w) || words.Contains(w + "s") || words.Contains(w + "es"))
                       || words.Any(w => w.Length >= 3 && h.EndsWith(w)) || joined.Contains(o.label.Replace(" ", ""));
            }
            if (Loosely(item)) return null;
            // an object elsewhere counts only when named by its full label, and only if nothing here shares a word with it
            var hereWords = new HashSet<string>(world.Items.Where(y => y.room == world.playerRoom && !string.IsNullOrEmpty(y.label))
                                                           .SelectMany(y => WordsOf(y.label)));
            var elsewhere = world.Items.FirstOrDefault(x => x.room != world.playerRoom && !string.IsNullOrEmpty(x.label)
                                                            && Phrase(x.label) && !WordsOf(x.label).Any(hereWords.Contains));
            if (elsewhere == null) return null;
            return $"not done: '{id}' is a {item.label}, but the request names a {elsewhere.label}; " +
                   $"the {elsewhere.label} ({elsewhere.itemId}) is in the {elsewhere.room}, not in this room";
        }

        static string Clean(string s) => ThinkRe.Replace(s, "").Replace("<|im_end|>", "").Trim();

        static Dictionary<string, object> Msg(string role, string content) =>
            new Dictionary<string, object> { { "role", role }, { "content", content } };

        string Post(string body)
        {
            var resp = Http.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false).GetAwaiter().GetResult();
            string text = resp.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                var m = Regex.Match(text, @"request \((\d+) tokens\) exceeds the available context size \((\d+) tokens\)");
                if (m.Success)
                    throw new Exception($"the scene and request need {m.Groups[1].Value} tokens but the model's context holds " +
                                        $"{m.Groups[2].Value}: put fewer objects in this room, or raise LocalModelServer.contextSize");
                throw new Exception($"HTTP {(int)resp.StatusCode}: {text}");
            }
            var root = (Dictionary<string, object>)MiniJson.Parse(text);
            var msg = (Dictionary<string, object>)((Dictionary<string, object>)((List<object>)root["choices"])[0])["message"];
            return Convert.ToString(msg.TryGetValue("content", out var c) ? c : "") ?? "";
        }

        // Tool results in the format the model was trained on (", " and ": " separators).
        public static string PyJson(object v)
        {
            switch (v)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case string s: return MiniJson.Serialize(s);
                case double d: return MiniJson.FormatNumber(d);
                case IDictionary<string, object> dict:
                    return "{" + string.Join(", ", dict.Select(kv => MiniJson.Serialize(kv.Key) + ": " + PyJson(kv.Value))) + "}";
                case IEnumerable e:
                    return "[" + string.Join(", ", e.Cast<object>().Select(PyJson)) + "]";
                default: return MiniJson.Serialize(Convert.ToString(v, CultureInfo.InvariantCulture));
            }
        }
    }
}
