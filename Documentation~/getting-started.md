# Installation and deployment

A working installation needs three separate downloads: the Unity package, llama.cpp, and the model weights.

## Install the package

Use **Window > Package Manager > + > Add package from git URL**:

```text
https://github.com/ErenAta16/Maverick-Unity.git#v1.0.0
```

Alternatively, download the source archive, extract it outside your project's `Assets` folder, and use **Add package from disk** to select its `package.json`. Keep that extracted folder available: the local package depends on it.

The package ID is `com.mcbuxrlab.maverick`. Runtime components use the `SceneAgent` namespace. Do not install the GitHub and Hugging Face copies together; they have the same package ID.

## Install llama.cpp and the model

1. Choose **Tools > Scene Agent > Open Model Folder**.
2. Download a Windows x64 runtime from the [llama.cpp releases](https://github.com/ggml-org/llama.cpp/releases).
3. Extract the runtime files so `llama-server.exe` sits directly in `Assets/StreamingAssets/SceneAgent/`.
4. Include the dependencies listed for that runtime build. Copying only the executable is insufficient.
5. Download the GGUF using the [model guide](model-download.md), and put it beside the executable.

CUDA builds require a compatible NVIDIA GPU and driver. Vulkan and CPU builds provide other options. The exact release used for the original timing measurements was not recorded in the published documentation, so this repository does not claim a tested llama.cpp version pin. Record the build you use when reporting a problem.

The package passes options including `--jinja` and, by default, `--cache-ram 0`. If a server build rejects an option, inspect `llama-server.log` and that build's `--help` output. `LocalModelServer.extraArgs` is editable in the Inspector.

## Prepare the scene

Choose **Tools > Scene Agent > Set Up Scene**. This creates or reuses a Scene Agent object and connects the world, agent, local server, session, chat box, and gaze components.

Select the objects you want to expose and choose **Make Selected Interactive**. This adds `SceneItem` components, creates IDs, derives labels and colors from names, and estimates supporting objects from renderer bounds. It also reparents the selected objects under the Scene Agent object.

Review the generated values. Names such as `Red Mug`, `Blue Toolbox`, and `Floor Lamp` work better than imported mesh names. Set room names explicitly if the scene has several rooms. For objects outside the catalog, enable `overrideAffordances` and configure the available actions.

Run **Validate Scene** before entering Play mode. It checks IDs, labels, supporting objects, rooms, component references, and model file paths. It does not test every possible command or verify a scene's physical layout.

## Server settings

| Setting | Default | Purpose |
| --- | --- | --- |
| `serverExe` | `SceneAgent/llama-server.exe` | Executable path relative to StreamingAssets, with a project/build-root fallback |
| `modelPath` | Empty | Selects the first alphabetically sorted `.gguf` beside the server; set a path if there are several |
| `port` | `8091` | Local HTTP port |
| `contextSize` | `4096` | Context allocated by the server |
| `preferGpu` | `true` | Tries a GPU launch before a CPU launch |
| `cpuThreads` | `8` | Thread count for the CPU launch |
| `extraArgs` | `--cache-ram 0` | Additional llama.cpp arguments |
| `startTimeoutSeconds` | `120` | Readiness timeout for each launch attempt |
| `capFrameRate` | `30` | Desktop rendering cap on entering GPU mode; use `0` in XR |
| `launchOnStart` | `true` | Starts the server with the component |

The server binds to `127.0.0.1`. GPU launch failure triggers a CPU attempt. The displayed GPU mode reflects the launch path; confirm actual offloading in the server log if performance is unexpected.

If a service already answers the health request on the configured port, the package reuses it. It does not verify the identity of the loaded model. Use a dedicated port or check the external server yourself.

## Use an external server

Start `llama-server` with the released model and `--jinja`; see the command in [model.md](model-download.md#run-the-model-without-unity).

If `LocalModelServer` remains assigned, use its configured port and let it detect the running service. Alternatively, remove that component, leave `SceneAgent.server` unassigned, and set `SceneAgent.endpoint` explicitly to:

```text
http://127.0.0.1:8091/v1/chat/completions
```

The standalone `SceneAgent.endpoint` field defaults to port 8081. Automatic scene setup assigns `LocalModelServer`, which supplies port 8091 instead. Explicit configuration avoids that mismatch.

Do not leave an unstarted `LocalModelServer` assigned to the agent. The agent waits for that component to become ready or report a failure.

## Build a Windows application

Unity copies StreamingAssets into the built application. Include the model, executable, and its dependencies before building. The application can then start the server without asking the player to install it separately.

Test a Windows x64 build on the target hardware. Verify server startup, a successful command, an unsupported command, a clarification reply, and shutdown. Confirm that the server process exits when the application closes. Keep the relevant third-party license files with any binaries and weights you redistribute.

This repository contains a UPM package, not a complete Unity project or a prebuilt game. Windows packaging does not establish compatibility with Android or standalone headsets.

## Rendering and context size

The model and renderer compete for GPU resources. The desktop default caps rendering at 30 FPS and disables VSync when the component enters GPU mode. The current implementation does not restore the previous frame-rate settings when the server stops; applications that switch modes should manage those settings themselves.

For XR, set `capFrameRate` to `0` and measure inference while the application runs at its intended headset refresh rate.

Start with fewer than about 60 interactive objects in a room at 4,096 tokens. Object names, optional fields, user text, and tool history all affect the actual token count. Larger contexts consume more memory. The model metadata's context capacity is separate from the context allocated by the server and from the contexts evaluated for this application.
