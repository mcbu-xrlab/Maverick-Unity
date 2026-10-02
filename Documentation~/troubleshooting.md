# Troubleshooting and known issues

Start with **Tools > Scene Agent > Validate Scene**. Then inspect the Unity Console and the local server log, available through `LocalModelServer.LogPath` under `Application.persistentDataPath`.

## Installation and startup

| Symptom | What to check |
| --- | --- |
| Unity cannot add the package | Confirm Git is installed and the URL includes `.git`. Try the tagged URL from the README. |
| `llama-server not found` | Put the executable directly in `Assets/StreamingAssets/SceneAgent/`, or set `serverExe` explicitly. |
| No GGUF found | Place the model beside the executable, or set `modelPath`. Check that the downloaded file is the full model. |
| Server exits immediately | Read the server log for missing DLLs, unsupported flags, driver errors, or memory allocation failures. |
| Unexpected model responses | Check which service is on port 8091. An existing healthy server is reused without checking its model identity. |
| Request goes to port 8081 | A standalone SceneAgent uses its own endpoint. Assign LocalModelServer or set the endpoint to the server's actual port. |
| Agent keeps waiting for readiness | Confirm the assigned LocalModelServer was launched. Disabling automatic launch requires calling `Launch()` yourself. |

The package expects response text in `choices[0].message.content`. If you substitute another inference server, confirm that it preserves the trained `<tool_call>` format there.

## Slow responses or memory pressure

Check the server log for actual GPU offloading. The launcher's GPU status alone is not a measurement of where every layer runs.

On desktop, cap rendering so the scene and model can share the GPU. In XR, leave `capFrameRate` at `0` and assess performance at the headset's intended refresh rate. Monitor thermal throttling, scene memory usage, and context size.

A context error means the scene description, request, and accumulated history exceed the allocated token budget. Reduce interactive objects per room or raise `contextSize` if memory permits. The approximate 60-object guidance is a starting point, not a fixed capacity.

## Calls are rejected

Check the object's exact ID, label, room, and capabilities. A known object in another room is not available to local actions until the room changes. An unknown label may need `overrideAffordances`. Containers may have to be open before placement is accepted.

The model sometimes attempts an impossible action before explaining the engine's rejection. This can add a model call. An accepted call with the wrong object is a separate problem: report the command and scene description so the selection can be inspected.

## The logical result differs from the scene

Room changes, button presses, and door states need application-specific behavior. Subscribe to `ToolExecuted` and implement the relevant locomotion, animation, or action.

If an object snaps back after an unrelated command, check whether another system moved its Transform without updating its SceneItem state. If placement looks wrong, review surface heights, object pivots, scale, and center-based placement. Multiple objects placed on the same target can overlap.

## Known implementation issues

The initial GitHub release preserves the runtime code from the published Unity package. The following limitations are documented rather than presented as fixes:

- **Argument validation is incomplete.** The output check does not enforce all required arguments or numeric types. Missing required arguments are rejected later by SceneWorld, but a value such as `distance: "abc"` can pass the check and throw during numeric conversion. The execution loop catches `ToolError`, not every possible exception. A valid call followed by a truncated call can also pass the checker because complete blocks are checked without rejecting the trailing fragment.
- **Placement orientation is metadata only.** `place` records values such as `upside_down`, but `Sync()` derives visual rotation from pitch, yaw, and roll without applying that metadata.
- **External object movement is not tracked continuously.** Initial Transform values can be imported at startup; later physics or XR interactions need to update the logical fields explicitly.
- **Commands are not queued.** The built-in chat checks `Busy`, but the public session methods do not prevent overlapping coroutine calls. Custom input sources need a shared admission or queue policy.
- **The scene snapshot stays fixed within one command.** Tool results are added to the conversation, but the full scene is not rebuilt after each action or room change.
- **The turn limit can end without a final reply.** The default is four model turns. Inspect the result's tool calls and errors when no final text is present; silence is not proof that nothing happened.
- **Rendering settings are not restored automatically.** Entering GPU mode can change VSync and the target frame rate. Applications that stop the server or switch scenes may need to restore their own settings.

These issues describe the current code paths. They do not change the scope of the historical test results, which covered the inputs listed in the published evaluation.

## Report a problem

Open a [GitHub issue](https://github.com/mcbu-xrlab/Maverick-Unity/issues) with the package version, Unity version, operating system, GPU and VRAM, llama.cpp build/backend, model filename, and the smallest command and scene that reproduce the problem. Include the relevant log excerpt and whether it occurred in the Editor or a built application.

Review command logs before posting them. A minimal scene description is often more useful than a large project archive.
