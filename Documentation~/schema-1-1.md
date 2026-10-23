# Maverick XR Agent: schema 1.1 (V1)

This page describes how a command runs when `SceneAgent.schemaVersion` is `V1`, what the model receives and what Unity
answers. Installation, the quick start and the V0 tools are in the package `README.md`. V1 is only for a model trained
on schema 1.1, such as Maverick 1.1; Maverick 1.0 uses `V0` (Maverick 1.1 also works in `V0`).

## How a command runs

1. If `SceneWorld.syncFromTransforms` is on, objects that moved in the scene without the agent are taken as external
   changes (`ApplyExternalChange`).
2. The scene is built at the request time, including the open question if there is one.
3. `SceneWorld.BeginCommand` opens the command. It clears the open question, the highlights and the offered choices.
4. The model is called with the V1 prompt (`Resources/SceneAgent/system_prompt_v1`). Before each call the prompt is
   measured with llama-server; if it does not fit the context with 192 tokens for the answer, the command stops with
   an error and nothing is sent. A server without llama-server's `/tokenize` and `/props` endpoints is not measured;
   its own overflow error is the check then.
5. Each tool call is checked (the output check, `fallbackDecoding`) and run by `SceneWorld.ExecuteV1`; `undo` is run by
   `SceneWorld.Undo`. The results go back to the model, up to 6 model calls while `maxTurns` is left at 4.
6. `SceneWorld.EndCommand` closes the command. If Unity accepted any call other than `find_objects`,
   `ask_clarification` and `undo`, the command becomes a `done` history entry with those calls (it cannot be undone if
   one of them is an irreversible press). If such calls were only refused, it becomes a `failed` entry. A question
   becomes the open question. The command
   is closed also on errors, at the turn limit and on `Cancel`, with the calls accepted until then.

## What the model receives

The user message is `Scene: <scene JSON>\nRequest: <text>`. The scene is the V0 scene plus the V1 fields, in this
order, each only when it has content. Shown here with line breaks; the package sends it on one line.

```json
{"player": {"room": "workshop", "gaze": "valve_2", "left_hand": null, "right_hand": null},
 "locations": ["workshop", "storage"],
 "visible_objects": [
   {"id": "valve_1", "label": "valve", "color": "red", "on": "workbench_1", "state": "open", "pos": [-0.5, 0.9, 0.4]},
   {"id": "valve_2", "label": "valve", "color": "blue", "on": "workbench_1", "state": "closed", "pos": [0.5, 0.9, 0.4]},
   {"id": "workbench_1", "label": "workbench", "on": "floor", "pos": [0.0, 0.0, 0.4]}],
 "people": {"user": "technician", "expert": "remote expert"},
 "speaker": "expert",
 "actor": "user",
 "attention": {"user": {"gaze": [{"id": "valve_2", "ago_s": 1, "active": true}]},
               "expert": {"point": [{"id": "valve_1", "ago_s": 4, "active": false}]}},
 "recent_actions": [{"ago_s": 12, "calls": ["set_state(valve_2, closed)"], "status": "done", "undoable": true}],
 "pending_clarification": {"ago_s": 3, "request": "highlight the valve", "question": "Which valve do you mean?",
                           "candidate_ids": ["valve_1", "valve_2"]}}
```

| Field | Rule |
|---|---|
| `people`, `speaker`, `actor` | Only when `SceneWorld.people` has more than one person. `speaker` is the `speaker` passed to `Command` or `Answer`. |
| `attention` | Per person and act (`gaze`, `point`): objects in the player's room, at most 30 s old, at most 3, newest first. `active` is false once the person looked or pointed elsewhere or released it. |
| `recent_actions` | The agent's commands, at most 3, at most 300 s old, newest first. `calls` are the accepted calls in the model's argument order. |
| `pending_clarification` | The open question, at most 60 s old. |
| `player.gaze` | Once any attention was reported: the user's active gaze, or null. Before that: `SceneWorld.gaze`. |

All ages are whole seconds, rounded half to even.

## What Unity answers

| Call | Result |
|---|---|
| A tool that changes the scene | `{"ok": true, "changed": [<object>, ...]}`, plus `"player": {"room", "left_hand", "right_hand"}` when the room or the hands changed |
| More than 20 changed objects | the first 20, then `"truncated": true, "more": <n>` |
| `teleport` | `{"ok": true, "room": ..., "visible_objects": [...], "player": {...}}` |
| `highlight`, `press` | `{"ok": true}` |
| `find_objects`, `ask_clarification` | as in V0 |
| `undo` | `{"ok": true, "undone": [<call>, ...], "changed": [...]}`, plus `player` when the player was restored |
| A refused call | `{"ok": false, "error": "<message>"}`; a wrong argument type or value gives `bad arguments: <message>` |

An object in `changed` is shown as in `visible_objects`.

## Undo

`undo(steps)` takes 1 to 3 (default 1) and reverses that many of the visible history entries, newest first. The whole
request is checked before anything changes:

| Error | Cause |
|---|---|
| `invalid steps '<v>'` | `steps` is not a whole number from 1 to 3 |
| `nothing to undo` | no command in the last 300 s |
| `cannot undo: only N commands to undo` | fewer entries than `steps` |
| `cannot undo: <call> did not happen` | the entry is a failed command |
| `cannot undo: <call> cannot be reversed` | a press of a button without `SceneItem.reversiblePress` |
| `cannot undo: <id> was changed since` | the object's version changed after the agent's command |

Undo restores the objects (room, support, position, rotation, state) and the player (room, hands), and their versions,
so a second undo still checks correctly. It is not itself a history entry.

**Versions.** Each object has a version. A tool call, `ApplyExternalChange` and `syncFromTransforms` raise it. Undo
compares versions to know whether something else changed the object. A change made in your code without
`ApplyExternalChange` does not raise the version, so undo cannot see it.

## API

| Member | Purpose |
|---|---|
| `SceneAgent.schemaVersion` | `V0` (default) or `V1` |
| `SceneAgent.Cancel()` | Stops the running command; a new `Run` also cancels the running one |
| `SceneAgentSession.Command(text, speaker = "user")` | Runs a command for that person |
| `SceneAgentSession.Answer(itemId, speaker = "user")` | Answers the open question with a picked object |
| `SceneAgentSession.Cancel()` | Stops the command, releases `Busy`, fires `Finished` with the error `cancelled; ...` |
| `SceneAgentSession.ResetHistory()` | Forgets history, the open question and attention; returns false while a command runs |
| `SceneWorld.people` | `List<ScenePerson>` (`id`, `role`); empty for a single user |
| `SceneWorld.ApplyExternalChange(item, update)` | Applies a change made outside the agent and raises the versions |
| `SceneWorld.syncFromTransforms` | Takes moves of more than 2 cm or 5 degrees as external changes, before each command |
| `SceneWorld.ReportAttention(person, act, itemId)`, `ReleaseAttention(person, act)` | Attention from other sources (networking) |
| `SceneWorld.clock` | The time source; tests set it |
| `SceneItem.reversiblePress` | A press of this button can be undone |
| `SceneAgentGaze.person`, `act`, `dwellSeconds` | Which person and act this component reports, and after how long |

## Limits

- History keeps 10 entries; the model sees at most 3, at most 300 s old.
- The open question is shown for 60 s. After that, `Answer` sends the original request again with the pick named.
- Attention keeps at most 3 entries per person and act in the scene, at most 30 s old.
- `SceneAgentScriptRunner`: `#look` sets `SceneWorld.gaze`, which the V1 scene uses only until attention is reported.
