// Talks to a local OpenAI-compatible server (llama.cpp llama-server with the Maverick GGUF, Ollama, ...),
// executes the returned tool calls on the SceneWorld
// and feeds tool results back until the model answers in text, asks the user a clarifying question
// (ask_clarification hands the turn back: the answer arrives as the next request), or runs out of turns.
//
// The loop is a coroutine: HTTP runs on a worker thread, tool calls run on the main thread
// (Unity APIs are main-thread only). Play mode: StartCoroutine(agent.Run(...)).
// Batch/editor code can drive it synchronously with RunBlocking(...).
// Every run ends with a result, never with an exception: what went wrong is in AgentResult.error.
// Only the answer acts: calls inside the model's reasoning (<think>) are never run.
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
        /// <summary>Calls the model wrote after asking the user a question: they are not run, since the user's answer
        /// comes first.</summary>
        public List<string> skipped = new List<string>();
    }

    public class AgentResult
    {
        public List<AgentTurn> turns = new List<AgentTurn>();
        public string finalText;
        /// <summary>Set when the model asked the user a question (ask_clarification); the turn ends there.</summary>
        public string clarificationQuestion;
        public string error;
        /// <summary>The scene the model saw (the JSON after "Scene: ").</summary>
        public string scene;
        public double seconds;
        /// <summary>The model was still calling tools when maxTurns ran out: the calls so far were run, but the
        /// command may be unfinished.</summary>
        public bool turnLimitReached;
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
        [Tooltip("The serialization the model was trained on. V0: Maverick 1.0. V1: Maverick 1.1 (schema " +
                 "1.1): the scene also shows recent actions, the open question, attention and people; tool results list " +
                 "what changed; the model can undo its own commands.")]
        public SchemaVersion schemaVersion = SchemaVersion.V0;
        [Tooltip("Model calls per command. In V1 the default 4 means 6 (Maverick 1.1 was trained with up to 6).")]
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
        static readonly Regex ThinkRe = new Regex(@"<think>.*?</think>\s*", RegexOptions.Singleline);
        // These calls end the agent's turn: the user's answer arrives as the next request.
        static readonly HashSet<string> Handoff = new HashSet<string> { "ask_clarification" };

        static string defaultPrompt, defaultPromptV1;
        static string DefaultPrompt => defaultPrompt ??= Resources.Load<TextAsset>("SceneAgent/system_prompt")?.text.Replace("\r\n", "\n");
        static string DefaultPromptV1 => defaultPromptV1 ??= Resources.Load<TextAsset>("SceneAgent/system_prompt_v1")?.text.Replace("\r\n", "\n");
        // no ?. on systemPromptAsset: in the Editor an unassigned field is a "fake null" Unity object, and ?. would call
        // .text on it (UnassignedReferenceException on the first command in Play mode)
        string Prompt => systemPrompt ?? (systemPromptAsset != null ? systemPromptAsset.text.Replace("\r\n", "\n") : null)
                         ?? (V1 ? DefaultPromptV1 : DefaultPrompt)
                         ?? throw new InvalidOperationException($"no system prompt (the package's Resources/SceneAgent/{(V1 ? "system_prompt_v1" : "system_prompt")} is missing)");

        bool V1 => schemaVersion == SchemaVersion.V1;
        int TurnLimit => V1 && maxTurns == 4 ? 6 : maxTurns;
        const int AnswerTokens = 192;

        /// <summary>One run's own state (audit 06): a cancelled run checks only its own token and ends only the command it
        /// opened, so a stale coroutine resumed later can neither run calls nor close the next run's command.</summary>
        class RunState
        {
            public readonly CancellationTokenSource cancel = new CancellationTokenSource();
            public int command = -1;  // the world command this run opened (V1)
        }

        RunState current;

        /// <summary>Stops the running command now: the pending model request is cancelled, no further call is run, and
        /// in V1 the command is closed with the calls already run recorded. Use this, not StopCoroutine; the session's
        /// Cancel calls it.</summary>
        public void Cancel()
        {
            var run = current;
            if (run == null) return;
            try { run.cancel.Cancel(); } catch (ObjectDisposedException) { }
            if (V1 && world != null && run.command >= 0)
            {
                try { world.EndCommand(run.command); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public IEnumerator Run(string utterance, Action<AgentResult> done)
        {
            var t0 = DateTime.UtcNow;
            var res = new AgentResult();
            // a run started while another is still going supersedes it: the older one is cancelled (its command closed),
            // or its later calls would run inside this run's command and nothing could stop it
            Cancel();
            var run = current = new RunState();
            // an unexpected exception ends the command with an error instead of leaving the caller waiting
            var turns = RunTurns(utterance, res, run);
            while (true)
            {
                if (run.cancel.IsCancellationRequested) { res.error = "cancelled; the calls before it stay done"; break; }
                try { if (!turns.MoveNext()) break; }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    res.error = "the agent stopped: " + e.Message;
                    break;
                }
                yield return turns.Current;
            }
            // V1: the command's history entry and open question, also after an error or the turn limit
            if (V1 && world != null && run.command >= 0)
            {
                try { world.EndCommand(run.command); }  // only this run's command: a later run's stays open
                catch (Exception e) { Debug.LogException(e); }
            }
            if (current == run) current = null;
            res.seconds = (DateTime.UtcNow - t0).TotalSeconds;
            done(res);
        }

        IEnumerator RunTurns(string utterance, AgentResult res, RunState run)
        {
            if (server != null)
            {
                while (!server.Ready && !server.Failed) yield return null;
                if (server.Failed)
                {
                    res.error = "model server not available: " + server.status;
                    yield break;
                }
                endpoint = server.Endpoint;
            }
            string sceneJson;
            if (V1)
            {
                // the order of sim_v1 and the training rows: changes made outside the agent, then the scene at the request
                // time (the open question still shown), then the command boundary
                if (world.syncFromTransforms) world.SyncFromTransforms();
                double now = world.Now();
                sceneJson = world.BuildSceneJsonV1(now);
                run.command = world.BeginCommand(utterance, now);
            }
            else sceneJson = world.BuildSceneJson();
            res.scene = sceneJson;
            var messages = new List<object> {
                Msg("system", Prompt),
                Msg("user", "Scene: " + sceneJson + "\nRequest: " + utterance) };
            var toolResults = new List<string>();  // ids returned here become nameable (grammar)
            bool finished = false;
            for (int turn = 0; turn < TurnLimit && res.error == null; turn++)
            {
                var req = new Dictionary<string, object> {
                    { "model", model }, { "messages", messages }, { "temperature", 0.0 }, { "max_tokens", AnswerTokens } };
                if (V1)
                {
                    // the prompt must leave room for the answer; nothing is ever cut from the scene (training never did)
                    string tooLong = null;
                    yield return FitCheck(MiniJson.Serialize(messages), m => tooLong = m);
                    if (tooLong != null) { res.error = tooLong; break; }
                }
                if (constrainedDecoding) req["grammar"] = SceneGrammar.Build(sceneJson, toolResults, schemaVersion);
                string body = MiniJson.Serialize(req);
                var token = run.cancel.Token;
                var task = Task.Run(() => Post(body, token));  // worker thread: no Unity API inside
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted) { res.error = task.Exception?.GetBaseException().Message; break; }
                string output = task.Result, check = null;
                if (!constrainedDecoding && fallbackDecoding)
                {
                    check = SceneGrammar.Check(output, sceneJson, toolResults, schemaVersion);
                    if (check == "invalid_tool" || check == "invalid_value") output = SceneGrammar.Refusal;
                    else if (check == "invalid_id" || check == "malformed")
                    {
                        req["grammar"] = SceneGrammar.Build(sceneJson, toolResults, schemaVersion);
                        string again = MiniJson.Serialize(req);
                        var retry = Task.Run(() => Post(again, token));
                        while (!retry.IsCompleted) yield return null;
                        if (retry.IsFaulted) { res.error = retry.Exception?.GetBaseException().Message; break; }
                        output = retry.Result;
                        // the answer written under the grammar is checked too: a server that ignores the grammar
                        // (not llama-server) can return the same broken call again
                        string second = SceneGrammar.Check(output, sceneJson, toolResults, schemaVersion);
                        if (second != "ok")
                        {
                            res.turns.Add(new AgentTurn { modelOutput = output, check = check });
                            res.error = $"the model's answer failed the output check ({check}, then {second} when written " +
                                        "again under the grammar); " + (V1 && world.AcceptedInCommand > 0
                                            ? "the calls before it were carried out, this answer was not" : "nothing was done");
                            break;
                        }
                    }
                }
                var at = new AgentTurn { modelOutput = output, check = check };
                res.turns.Add(at);
                string answer = SceneGrammar.StripReasoning(output);
                if (answer == null) { res.error = "the model's answer ended inside its reasoning; nothing was done"; break; }
                var blocks = SceneGrammar.CallBlocks(answer);
                if (blocks.Count == 0)
                {
                    res.finalText = Clean(answer);
                    finished = true;
                    break;
                }
                var toolMsgs = new List<object>();
                string question = null;
                foreach (var block in blocks)
                {
                    if (question != null) { at.skipped.Add(block); continue; }
                    // Cancel called while an earlier call of this answer ran (a ToolExecuted handler): no further call
                    if (run.cancel.IsCancellationRequested) { res.error = "cancelled; the calls before it stay done"; break; }
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
                        if (V1) world.RecordAttempt(name, args);  // a refused call is a failed attempt, as in sim_v1
                        result = new Dictionary<string, object> { { "ok", false }, { "error", guard } };
                    }
                    else
                    {
                        try { result = V1 ? world.ExecuteV1(name, args) : world.Execute(name, args); }
                        catch (ToolError e) { result = new Dictionary<string, object> { { "ok", false }, { "error", e.Message } }; }
                        catch (Exception e)  // a value the tool cannot use: the model is told, the scene is unchanged
                        { result = new Dictionary<string, object> { { "ok", false }, { "error", "bad arguments: " + e.Message } }; }
                    }
                    if (Handoff.Contains(name))
                        question = Convert.ToString(args.TryGetValue("question", out var q) ? q : "") ?? "";
                    string resultJson = PyJson(result);
                    at.calls.Add(block);
                    at.results.Add(resultJson);
                    toolResults.Add(resultJson);
                    toolMsgs.Add(new Dictionary<string, object> { { "role", "tool" }, { "name", name }, { "content", resultJson } });
                }
                if (at.skipped.Count > 0)
                    Debug.Log($"[SceneAgent] {at.skipped.Count} call(s) after the question to the user were not run");
                // history: the model's own call text, then tool results (Qwen3 renders them as <tool_response>)
                messages.Add(Msg("assistant", Clean(output)));
                messages.AddRange(toolMsgs);
                if (question != null) { res.clarificationQuestion = question; finished = true; break; }
            }
            if (!finished && res.error == null) res.turnLimitReached = true;
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

        /// <summary>V1: measures the rendered prompt with llama-server (/apply-template, /tokenize, /props) and calls fail
        /// with a clear message when the prompt and the answer do not fit the context. Servers without these endpoints
        /// are not measured; their own overflow error is the check then (Post).</summary>
        IEnumerator FitCheck(string messagesJson, Action<string> fail)
        {
            int k = endpoint.IndexOf("/v1/", StringComparison.Ordinal);
            string root = k > 0 ? endpoint.Substring(0, k) : endpoint.TrimEnd('/');
            var task = Task.Run(() => Measure(root, messagesJson));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) yield break;
            var (tokens, ctx) = task.Result;
            if (tokens > 0 && ctx > 0 && tokens + AnswerTokens > ctx)
                fail($"the scene and request need {tokens} tokens and the answer up to {AnswerTokens}, but the model's context " +
                     $"holds {ctx}: put fewer objects in this room, or raise LocalModelServer.contextSize; nothing was sent to the model");
        }

        static (int tokens, int ctx) Measure(string root, string messagesJson)
        {
            string Call(string path, string body)
            {
                var r = body == null ? Http.GetAsync(root + path).ConfigureAwait(false).GetAwaiter().GetResult()
                    : Http.PostAsync(root + path, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false).GetAwaiter().GetResult();
                return r.IsSuccessStatusCode ? r.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult() : null;
            }
            // asked every time: a restarted server can have another context size
            int ctx = -1;
            var props = Call("/props", null);
            if (props != null && MiniJson.Parse(props) is Dictionary<string, object> pd && pd.TryGetValue("default_generation_settings", out var g)
                && g is Dictionary<string, object> gd && gd.TryGetValue("n_ctx", out var n) && n is double nd)
                ctx = (int)nd;
            if (ctx <= 0) return (-1, ctx);
            var tpl = Call("/apply-template", "{\"messages\":" + messagesJson + "}");
            if (tpl == null || !(MiniJson.Parse(tpl) is Dictionary<string, object> td) || !(td.TryGetValue("prompt", out var p) && p is string prompt))
                return (-1, ctx);
            var tok = Call("/tokenize", MiniJson.Serialize(new Dictionary<string, object> { { "content", prompt } }));
            if (tok == null || !(MiniJson.Parse(tok) is Dictionary<string, object> kd) || !(kd.TryGetValue("tokens", out var t) && t is List<object> tl))
                return (-1, ctx);
            return (tl.Count, ctx);
        }

        string Post(string body, CancellationToken token)
        {
            var resp = Http.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"), token).ConfigureAwait(false).GetAwaiter().GetResult();
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
                case int n: return n.ToString(CultureInfo.InvariantCulture);
                case IDictionary<string, object> dict:
                    return "{" + string.Join(", ", dict.Select(kv => MiniJson.Serialize(kv.Key) + ": " + PyJson(kv.Value))) + "}";
                case IEnumerable e:
                    return "[" + string.Join(", ", e.Cast<object>().Select(PyJson)) + "]";
                default: return MiniJson.Serialize(Convert.ToString(v, CultureInfo.InvariantCulture));
            }
        }
    }
}
