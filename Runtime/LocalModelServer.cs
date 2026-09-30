// Starts the local llama.cpp server with the model when the game starts and stops it when the game ends, so the player
// installs nothing and nothing leaves the machine (it listens on 127.0.0.1 only). GPU first; if that server does not
// come up, the same model on the CPU. A server already answering on the port is used as it is.
//
// Default layout (relative paths are looked up in StreamingAssets, then next to the project / the built game):
//   StreamingAssets/SceneAgent/llama-server.exe   (+ the DLLs of the same llama.cpp release)
//   StreamingAssets/SceneAgent/<model>.gguf       (modelPath empty = the first .gguf in that folder)
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SceneAgent
{
    public class LocalModelServer : MonoBehaviour
    {
        public enum Mode { Stopped, Starting, Gpu, Cpu, External, Failed }

        [Tooltip("llama-server executable. Relative: looked up in StreamingAssets, then next to the project / the game.")]
        public string serverExe = "SceneAgent/llama-server.exe";
        [Tooltip("The model (GGUF). Empty: the first .gguf in the server's folder. Relative paths resolve like serverExe.")]
        public string modelPath = "";
        public int port = 8091;
        [Tooltip("Tokens of context. 4096 holds a room of about 60 objects plus a few turns.")]
        public int contextSize = 4096;
        public bool preferGpu = true;
        public int cpuThreads = 8;
        [Tooltip("--cache-ram 0: llama.cpp's host prompt cache (8 GB by default) filled the machine's RAM.")]
        public string extraArgs = "--cache-ram 0";
        public float startTimeoutSeconds = 120f;
        [Tooltip("While the model runs on the GPU, cap rendering at this frame rate so the renderer does not starve it " +
                 "(uncapped rendering can make the model about 5x slower). 0 = leave the frame rate alone; " +
                 "use 0 in XR, where the headset sets the frame rate.")]
        public int capFrameRate = 30;
        [Tooltip("Start the server when this component starts. Off: call Launch() yourself.")]
        public bool launchOnStart = true;

        public Mode mode { get; private set; } = Mode.Stopped;
        public string status { get; private set; } = "not started";
        public string Endpoint => $"http://127.0.0.1:{port}/v1/chat/completions";
        public bool Ready => mode == Mode.Gpu || mode == Mode.Cpu || mode == Mode.External;
        public bool Failed => mode == Mode.Failed;
        /// <summary>Raised whenever mode changes (Starting, Gpu, Cpu, External, Failed, Stopped).</summary>
        public event Action<Mode> ModeChanged;
        public string LogPath => Path.Combine(Application.persistentDataPath, "llama-server.log");

        Process proc;
        StreamWriter log;
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        IEnumerator Start()
        {
            if (launchOnStart) yield return Launch();
        }

        void SetMode(Mode m)
        {
            mode = m;
            if (m == Mode.Gpu && capFrameRate > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = capFrameRate;
                Debug.Log($"[SceneAgent] frame rate capped at {capFrameRate} while the model uses the GPU (LocalModelServer.capFrameRate)");
            }
            ModeChanged?.Invoke(m);
        }

        /// <summary>The first existing file among: the path itself (absolute), StreamingAssets/path, next to the project
        /// (Editor) or the built game.</summary>
        public static string Resolve(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            if (Path.IsPathRooted(p)) return p;
            var roots = new[] { Application.streamingAssetsPath, Path.GetFullPath(Path.Combine(Application.dataPath, "..")) };
            return roots.Select(r => Path.Combine(r, p)).FirstOrDefault(File.Exists) ?? Path.Combine(roots[0], p);
        }

        /// <summary>The model file: modelPath, or the first .gguf next to the server.</summary>
        public string ResolveModel(string exe)
        {
            if (!string.IsNullOrEmpty(modelPath)) return Resolve(modelPath);
            string dir = exe != null ? Path.GetDirectoryName(exe) : null;
            return dir != null && Directory.Exists(dir) ? Directory.GetFiles(dir, "*.gguf").OrderBy(f => f).FirstOrDefault() : null;
        }

        public IEnumerator Launch()
        {
            SetMode(Mode.Starting);
            // a server already answering on the port (e.g. started by hand) is used as is
            var probe = HealthAsync();
            while (!probe.IsCompleted) yield return null;
            if (probe.Result) { status = $"using the server already running on :{port}"; SetMode(Mode.External); yield break; }

            string exe = Resolve(serverExe), model = ResolveModel(exe);
            if (exe == null || !File.Exists(exe) || model == null || !File.Exists(model))
            {
                status = exe == null || !File.Exists(exe)
                    ? $"llama-server not found: {exe} (see the package README, 'Model files')"
                    : $"no model (.gguf) found: {model ?? Path.GetDirectoryName(exe)}";
                Debug.LogError("[SceneAgent] " + status);
                SetMode(Mode.Failed);
                yield break;
            }
            string common = $"-m \"{model}\" --jinja -c {contextSize} --host 127.0.0.1 --port {port} {extraArgs}";
            if (preferGpu)
            {
                status = "loading the model on the GPU…";
                yield return StartAndWait(exe, common + " -ngl 99", cpuOnly: false);
                if (mode == Mode.Gpu) yield break;
                Stop();
                SetMode(Mode.Starting);
            }
            status = "loading the model on the CPU…";
            yield return StartAndWait(exe, common + $" --device none --no-op-offload -ngl 0 -t {cpuThreads}", cpuOnly: true);
            if (!Ready) { status = $"the model server did not start (see {LogPath})"; SetMode(Mode.Failed); }
        }

        IEnumerator StartAndWait(string exe, string args, bool cpuOnly)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };
            if (cpuOnly) psi.EnvironmentVariables["CUDA_VISIBLE_DEVICES"] = "-1";
            try
            {
                log = new StreamWriter(LogPath, append: false, Encoding.UTF8) { AutoFlush = true };
                proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                // the pipes must be drained, or the server blocks once the buffer is full
                proc.OutputDataReceived += (_, e) => Write(e.Data);
                proc.ErrorDataReceived += (_, e) => Write(e.Data);
                proc.Start();
                ChildProcessGuard.Attach(proc);   // dies with this process, even after a crash
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                Debug.Log($"[SceneAgent] model server started ({(cpuOnly ? "CPU" : "GPU")}), log: {LogPath}");
            }
            catch (Exception e)
            {
                status = "could not start llama-server: " + e.Message;
                Debug.LogError("[SceneAgent] " + status);
                yield break;
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < startTimeoutSeconds)
            {
                if (proc.HasExited) break;
                var h = HealthAsync();
                while (!h.IsCompleted) yield return null;
                if (h.Result)
                {
                    status = $"model ready on the {(cpuOnly ? "CPU" : "GPU")}";
                    SetMode(cpuOnly ? Mode.Cpu : Mode.Gpu);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        void Write(string line)
        {
            if (line == null) return;
            lock (this) { try { log?.WriteLine(line); } catch (ObjectDisposedException) { } }
        }

        async Task<bool> HealthAsync()
        {
            try
            {
                var r = await Http.GetAsync($"http://127.0.0.1:{port}/health").ConfigureAwait(false);
                return r.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public void Stop()
        {
            // wait for the exit: a scene reload starts the next server at once, and it must find the port free
            try { if (proc != null && !proc.HasExited) { proc.Kill(); proc.WaitForExit(3000); } } catch (Exception) { }
            proc?.Dispose();
            proc = null;
            lock (this) { log?.Dispose(); log = null; }
            if (mode != Mode.External && mode != Mode.Stopped) SetMode(Mode.Stopped);
        }

        void OnApplicationQuit() => Stop();
        void OnDestroy() => Stop();
    }

    /// <summary>Puts the model server in a Windows job object that is closed, and its processes killed, when this
    /// process ends in any way (quit, crash, the Editor closing). Elsewhere: nothing (Stop still ends the server).</summary>
    static class ChildProcessGuard
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong R, W, O, RB, WB, OB; }
        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        static IntPtr job;

        public static void Attach(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    var info = new ExtendedLimits();
                    info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf(typeof(ExtendedLimits)));   // 9: extended limits
                }
                if (!AssignProcessToJobObject(job, p.Handle))
                    Debug.LogWarning("[SceneAgent] could not tie the model server to this process (error " + Marshal.GetLastWin32Error() + ")");
            }
            catch (Exception e) { Debug.LogWarning("[SceneAgent] could not tie the model server to this process: " + e.Message); }
        }
#else
        public static void Attach(Process p) { }
#endif
    }
}
