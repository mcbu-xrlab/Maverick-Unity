# Unity integration

`SceneAgentSession` is the entry point for an application's input and interface. It submits commands, tracks pending clarification questions, and exposes completion events. `SceneWorld` owns the logical scene state and executes tools.

## Component reference

| Component | Responsibility | Main members |
| --- | --- | --- |
| `SceneItem` | Describes an interactive object | `itemId`, `label`, `color`, `room`, `on`, `pos`, `state`, `overrideAffordances` |
| `SceneWorld` | Builds the scene description and executes tools | `BuildSceneJson`, `Execute`, `ToolExecuted`, `playerRoom`, `rooms` |
| `SceneAgent` | Calls the model and feeds back tool results | `Run`, `maxTurns`, `fallbackDecoding`, `guardSubstitutes` |
| `LocalModelServer` | Starts, monitors startup, and stops llama.cpp | `Launch`, `Stop`, `Ready`, `Failed`, `LogPath` |
| `SceneAgentSession` | Handles commands and clarification replies | `Command`, `Answer`, `Busy`, `Candidates`, `Line`, `Finished` |
| `SceneAgentGaze` | Sets a mouse or camera-center gaze target | `source`, `viewCamera`, `maxDistance` |
| `SceneAgentChatBox` | Provides a desktop test interface | `toggleKey`, `visibleLines` |
| `SceneAgentScriptRunner` | Runs command files and writes reports | `script`, `outputDirectory`, `screenshots` |

The runtime and editor code are separate assemblies. There is no dependency on an XR interaction package or speech recognition library.

## Submit commands and receive results

Use one session for an interaction flow. The methods are coroutines and must be started on Unity's main thread. Check `Busy` before accepting another command; the session does not provide a queue or reject overlapping calls on its own.

```csharp
using SceneAgent;
using UnityEngine;

public sealed class MaverickSessionEvents : MonoBehaviour
{
    [SerializeField] private SceneAgentSession session;

    private void OnEnable()
    {
        if (session == null) return;
        session.Line += ShowLine;
        session.Finished += OnFinished;
    }

    private void OnDisable()
    {
        if (session == null) return;
        session.Line -= ShowLine;
        session.Finished -= OnFinished;
    }

    private void ShowLine(string text) => Debug.Log(text);

    private void OnFinished(AgentResult result)
    {
        if (!string.IsNullOrEmpty(result.error))
            Debug.LogWarning(result.error);

        if (result.clarificationQuestion != null)
        {
            Debug.Log(result.clarificationQuestion);
            foreach (string id in session.Candidates)
                Debug.Log("Candidate: " + id);
        }
    }
}
```

`AgentResult` contains the model turns, tool calls, tool results, final text, an optional clarification question, an optional error, and elapsed seconds. Inspect tool results when you need to determine whether an action happened. A text confirmation alone is not an execution receipt.

The `Line` event presents readable messages, but model tokens are not streamed to it. Tool lines are emitted after the agent's command loop returns.

## Clarification replies

When the model calls `ask_clarification`, display `session.Candidates` and the question from the result. After the user picks a candidate, call `session.Answer(itemId)`. Verify that the selected ID is in the offered list.

You can also send a short reply through `session.Command`, for example `the red one`. The session uses a heuristic to distinguish a clarification answer from a new command, then combines the answer with the pending request. A new command otherwise starts a fresh model history. The session does not maintain an unrestricted, persistent chat transcript for the model.

## Connect speech

Pass the recognizer's completed English transcript to `session.Command`. For fully offline voice input, use a local recognizer such as [whisper.cpp](https://github.com/ggml-org/whisper.cpp). Speech recognition, microphone handling, and speech synthesis are outside this package.

If a recognizer invokes callbacks on a background thread, dispatch the transcript to Unity's main thread before starting the coroutine. Decide whether to ignore or queue input received while `Busy` is true.

## Describe your objects

Keep `itemId` unique and stable. Labels are ordinary English object names. Color is optional. `on` identifies a supporting object or a special location such as `floor`, `left_hand`, `right_hand`, or `both_hands`.

The supplied catalog controls whether an object can be held, used as a surface or container, moved, pressed, or assigned a state. Enable `overrideAffordances` for a custom label or behavior. Its capability flags are enforced by Unity; they are not all serialized into the model's scene description, so a model may still attempt an action the engine rejects.

SceneWorld discovers items under its transform and under external hand anchors assigned to it. Merely adding `SceneItem` elsewhere in the scene does not expose it to this world.

“Visible objects” means objects assigned to `playerRoom`, not objects in the camera frustum. Inactive descendants can also be included. The scene description is a logical room inventory, not an occlusion or visibility calculation.

## Keep scene state current

When `initFromTransforms` is enabled, initial positions and rotations are read at startup. Later commands read the stored `SceneItem` fields. If physics, an XR grab system, or your own code changes an object, update its logical position, rotation, room, support, and hand ownership as appropriate.

Logical positions describe the bottom of an object in room-local meters. The default visual synchronization estimates the center offset using half of `transform.localScale.y`; custom meshes, pivots, and parent scales may require a different placement implementation.

The agent builds a scene snapshot at the start of each command. It then appends tool results without rebuilding the full snapshot after every action. Account for this when designing long commands or interactions that change the room while the command is running.

## Rooms and player movement

Set `SceneWorld.rooms` and each item's `room`. `playerRoom` determines which room is described to the model. `find_objects` searches labels across rooms using exact label matching in the engine; the model has to choose the appropriate label for a synonym.

`teleport` updates `playerRoom` and the room of held objects. It does not move the XR rig. Subscribe to `ToolExecuted` and use your locomotion system to move the rig to the destination room:

```csharp
using System.Collections.Generic;
using SceneAgent;
using UnityEngine;

public sealed class MaverickRoomEvents : MonoBehaviour
{
    [SerializeField] private SceneWorld world;

    private void OnEnable()
    {
        if (world != null) world.ToolExecuted += OnTool;
    }

    private void OnDisable()
    {
        if (world != null) world.ToolExecuted -= OnTool;
    }

    private void OnTool(string tool, Dictionary<string, object> args, object result)
    {
        if (tool == "teleport")
        {
            // Resolve world.playerRoom to a destination in your scene and
            // request movement through your application's locomotion system.
            Debug.Log("Move the player rig to room: " + world.playerRoom);
        }
    }
}
```

If rooms have different world-space origins, assign `SceneWorld.roomOrigin` from code. It is a function that maps each room name to its origin; the default is zero for all rooms.

## Gaze and hand anchors

`SceneAgentGaze` raycasts from the mouse pointer or the camera center. It finds a `SceneItem` through the hit collider's parents. Objects need colliders, and the referenced camera must be assigned or available as `Camera.main`.

For a camera-center XR gaze target, select `CameraCentre`. This is head-direction pointing, not eye tracking. Gaze updates pause while the session is busy so a command retains its selected target.

Assign `leftHandAnchor` and `rightHandAnchor` to show held items on the corresponding transforms. The built-in behavior parents objects to those anchors; it does not implement physical grasping or an XR Interaction Toolkit grab interaction.

## Apply scene-specific effects

`ToolExecuted` fires after the world accepts a call. Use it for button actions, sounds, animation state, and locomotion. Event handlers should avoid throwing exceptions into the command loop.

- `set_state` updates the logical state. The built-in synchronization also enables or disables a child `Light` for `on` and `off`; opening a door needs your animation or interaction code.
- `press` stores the requested duration in `world.pressed`. Your code implements the effect and timing.
- `highlight` sets an emission color on a renderer. Its appearance depends on the material and shader.
- `place` moves an object directly; it does not need a preceding `grab`. Placement orientation is stored but is not converted to a visual rotation by the current implementation.
- `move_by` uses fixed world-axis directions. Left and right are not relative to the user's current view.

## Scripted runs and logs

Assign a TextAsset to `SceneAgentScriptRunner`, or run a built Windows game with:

```powershell
.\YourGame.exe -sceneAgentScript commands.txt -sceneAgentOut reports
```

`YourGame.exe` is your own built application. A script has one command per line and can include:

```text
// The IDs and objects below must exist in your scene.
#look mug_1
highlight that
#wait 1
pick up the mug
#answer mug_1
```

Use `#answer` only when the preceding command actually left a clarification question pending. `#look none` clears the gaze target. Add `-sceneAgentShots` to request screenshots. A command-line run exits the game when finished.

The runner writes `report.json` with tool calls, results, scene signatures, errors, and timing. Relative output folders resolve beneath `Application.persistentDataPath`. Session logging is controlled by `logDirectory`; the editor setup enables it as `SceneAgentSessions`. Logs contain user commands and scene data, so review them before sharing a report.
