// Plays a list of commands through the agent and writes a report: for testing your scene and for demos.
//   one command per line; "// ..." comments; "#answer <item_id>" answers the pending question; "#look <item_id>|none"
//   sets the gaze; "#wait <seconds>" pauses (not counted in any step's time).
// In the Editor: assign a TextAsset and press Play. In a built game or batch run:
//   Game.exe -sceneAgentScript commands.txt [-sceneAgentOut <dir>] [-sceneAgentShots]   (quits when done)
// Report: <out>/report.json with every step's calls, results, answer, error, seconds and the scene signature.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SceneAgent
{
    [RequireComponent(typeof(SceneAgentSession))]
    public class SceneAgentScriptRunner : MonoBehaviour
    {
        public TextAsset script;
        [Tooltip("Output folder (relative = under Application.persistentDataPath). Empty: SceneAgentRuns/<time>.")]
        public string outputDirectory = "";
        public bool screenshots;
        public bool quitWhenDone;
        public float serverTimeoutSeconds = 240f;

        public bool Done { get; private set; }
        public string ReportPath { get; private set; }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        IEnumerator Start()
        {
            string[] lines;
            string cli = Arg("-sceneAgentScript");
            if (cli != null) { lines = File.ReadAllLines(cli); quitWhenDone = true; }
            else if (script != null) lines = script.text.Replace("\r\n", "\n").Split('\n');
            else yield break;
            if (Environment.GetCommandLineArgs().Contains("-sceneAgentShots")) screenshots = true;
            string outDir = Arg("-sceneAgentOut") ?? outputDirectory;
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine("SceneAgentRuns", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            outDir = Path.GetFullPath(Path.IsPathRooted(outDir) ? outDir : Path.Combine(Application.persistentDataPath, outDir));
            Directory.CreateDirectory(outDir);

            var session = GetComponent<SceneAgentSession>();
            // the script sets the gaze (#look); the gaze components come back when it ends, for whoever uses the scene next
            var pausedGaze = FindObjectsOfType<SceneAgentGaze>().Where(g => g.enabled).ToList();
            foreach (var g in pausedGaze) g.enabled = false;
            var server = session.agent != null ? session.agent.server : null;
            var steps = new List<object>();
            float t0 = Time.realtimeSinceStartup;
            if (server != null)
                while (!server.Ready && !server.Failed && Time.realtimeSinceStartup - t0 < serverTimeoutSeconds) yield return null;
            var head = new Dictionary<string, object> {
                { "server_mode", server != null ? server.mode.ToString() : "none (agent.endpoint)" },
                { "server_status", server != null ? server.status : session.agent.endpoint },
                { "server_seconds", Math.Round(Time.realtimeSinceStartup - t0, 1) }, { "unity", Application.unityVersion } };
            int n = 0;
            if (server == null || server.Ready)
            {
                foreach (var raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("//")) continue;
                    if (line.StartsWith("#wait "))
                    {
                        yield return new WaitForSecondsRealtime(float.Parse(line.Substring(6).Trim(), CultureInfo.InvariantCulture));
                        continue;
                    }
                    if (line.StartsWith("#look "))
                    {
                        string id = line.Substring(6).Trim();
                        session.world.gaze = id == "none" ? null : id;
                        continue;
                    }
                    if (line.StartsWith("#answer ")) yield return session.Answer(line.Substring(8).Trim());
                    else yield return session.Command(line);

                    var r = session.LastResult;
                    steps.Add(new Dictionary<string, object> {
                        { "step", line }, { "calls", r.turns.SelectMany(t => t.calls).Cast<object>().ToList() },
                        { "results", r.turns.SelectMany(t => t.results).Cast<object>().ToList() },
                        { "check", r.turns.Select(t => (object)t.check).ToList() }, { "final_text", r.finalText },
                        { "clarification", r.clarificationQuestion }, { "error", r.error },
                        { "seconds", Math.Round(r.seconds, 2) }, { "model_calls", (double)r.turns.Count },
                        { "room", session.world.playerRoom }, { "signature", session.world.Signature() } });
                    if (screenshots)
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"step_{n:00}.png"));
                        yield return new WaitForEndOfFrame();
                        yield return null;
                    }
                    n++;
                }
            }
            var secs = steps.Select(s => (double)((Dictionary<string, object>)s)["seconds"]).OrderBy(x => x).ToList();
            head["steps"] = (double)steps.Count;
            head["errors"] = (double)steps.Count(s => ((Dictionary<string, object>)s)["error"] != null);
            head["seconds_p50"] = secs.Count > 0 ? secs[secs.Count / 2] : 0.0;
            head["results"] = steps;
            ReportPath = Path.Combine(outDir, "report.json");
            File.WriteAllText(ReportPath, MiniJson.Serialize(head));
            Debug.Log($"[SceneAgent] script: {steps.Count} steps, report {ReportPath}");
            foreach (var g in pausedGaze) if (g != null) g.enabled = true;
            Done = true;
            if (quitWhenDone) Application.Quit();
        }
    }
}
