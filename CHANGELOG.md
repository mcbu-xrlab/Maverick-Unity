# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-10-23

Schema 1.1 (V1) for models trained on it, such as Maverick 1.1. The default stays `V0`, which behaves exactly as in 1.0.1
and is what Maverick 1.0 needs (Maverick 1.1 also works in it). Use `V1` only with a model trained on schema 1.1.

### Added

- `SchemaVersion` and `SceneAgent.schemaVersion` (`V0` by default, or `V1`).
- V1 prompt: `Resources/SceneAgent/system_prompt_v1`, loaded automatically in V1. It is the Maverick 1.1 training prompt with the
  12 tools.
- V1 tool `undo(steps)`: reverses the agent's last 1 to 3 commands exactly, objects and player alike. It refuses, and
  changes nothing, when an object was changed since, when a command failed or cannot be reversed, or when there is
  nothing to undo. A command can be undone for 300 seconds.
- V1 scene fields, each only when it has content:
  - `people`, `speaker` and `actor`, when `SceneWorld.people` has more than one person;
  - `attention`: per person, the recent `gaze` and `point` targets in the player's room (at most 3 per list, at most
    30 seconds old);
  - `recent_actions`: the last 3 commands (at most 300 seconds old) with their calls, status and whether they can be
    undone;
  - `pending_clarification`: the open question, its request and candidates (at most 60 seconds old).
- V1 tool results list what changed: `changed` (the changed objects, at most 20, then `truncated` and `more`), and
  `player` when the room or the hands changed. A `teleport` result lists the new room's objects.
- `SceneWorld` V1 state: command history (10 entries kept), object versions, the open question, the attention ledger
  (`ReportAttention`, `ReleaseAttention`), `people`, `speaker`, `actor`, and an injectable `clock` for tests.
- `SceneWorld.ApplyExternalChange(item, update)`: changes made outside the agent (a hand in XR, physics, your own code)
  update the logical state and the object versions, so that undo never overwrites them. They are never history entries.
- `SceneWorld.syncFromTransforms` (off by default): before each command, an object that moved by more than 2 cm or
  turned by more than 5 degrees in the scene is taken as an external change.
- `SceneItem.reversiblePress`: a press of this button can be undone.
- `SceneAgentGaze` in V1: a target held for `dwellSeconds` is reported as the `person`'s `gaze` or `point` (`act`);
  looking away for as long releases it. With `source = RayFromTransform` it follows a hand or controller ray.
- `SceneAgentSession.Command(text, speaker)` and `Answer(itemId, speaker)`: the person who speaks or answers.
- `SceneAgentSession.ResetHistory()`: forgets the history, the open question and attention (a new task). It is refused
  while a command runs.
- `SceneAgentSession.Cancel()` and `SceneAgent.Cancel()`: stop the running command. The pending model request is
  cancelled, no further call runs, the calls already run stay done (and, in V1, recorded), `Busy` is released and
  `Finished` reports `cancelled`. Use them instead of `StopCoroutine`. Disabling the session cancels too.
- V1 context check: before each model call the prompt is measured with llama-server. If the scene and the request do
  not fit the context with room for the answer, the command stops with an error naming the token counts, and nothing
  is sent to the model. Nothing is cut from the scene.

### Changed

- V1: up to 6 model calls per command when `SceneAgent.maxTurns` is left at its default of 4.
- V1: a reply to a question is a new request, and the scene shows the open question. `Answer(itemId)` sends the pick
  in the forms Maverick 1.1 was trained on ("The red one.", "The one on the shelf.", or "The mug I'm looking at." with
  the speaker's gaze set on the object). For a question older than 60 seconds it sends the original request again with
  the pick named.
- V1 output check: knows `undo`, and also accepts ids from earlier tool results (including a teleport), from the V1
  scene fields and from `recent_actions`.
- V1: a wrong argument type or value gives `bad arguments: <message>`, with the training simulator's messages.

### Fixed

- A second `SceneAgent.Run` started while one was running let the first one keep acting; in V1 its calls were recorded
  in the second command, so undoing the second command also reversed the first. A new run now cancels the running one
  first.
- A `Cancel` issued from a `ToolExecuted` handler did not stop the remaining calls of the same model answer. Each call
  now checks the run's cancellation first.
- `SceneAgentSession.Cancel`: a `Line` handler that threw skipped `Finished` for the cancelled command. `Finished` now
  always fires.
- V1: `press` with `"hold_seconds": null` pressed the button for 0 seconds; it now fails as in the training simulator.
  V0 is unchanged.

### Compatibility

- V0 is unchanged: same prompt, same 11 tools, same tool results, same scene. Existing scenes and code work as before.
- V1 needs a model trained on schema 1.1. The released Maverick-4B-Unity-XR-Agent model is a V0 model: do not switch
  it to V1.
- New members only; nothing public was removed or renamed. `Command` and `Answer` gained an optional `speaker`
  argument, so existing calls compile unchanged.

### Known limitations

- A list or an object where a text argument is expected: Python's lookup raises its own error text, Unity answers
  "invalid direction or unit". The output check refuses such values before they reach the runtime.
- Numeric arguments given as strings are parsed by .NET, not by Python's `float()`: `"1,5"` and `"1_0"` parse differently,
  and `"inf"` is an error. Only reachable when the model's output check is off.
- An explicit `null` for `orientation` or `axis` takes the Unity default, while the Python reference rejects it. The
  output check refuses these values first, so the default setup does not reach this case.
- Very large distances (beyond about 1e15) are formatted in .NET style in some results. Positions are rounded to 0.1 m
  in a room, so no scene value reaches that range.

## [1.0.1]

### Fixed

- Strict argument checks (required fields, types, finite numbers) in the output check; an exception inside a tool
  becomes a failed tool result.
- Tool calls inside the model's `<think>` reasoning never run.
- The turn stops at `ask_clarification` even when more calls follow it; `AgentResult.turnLimitReached` reports a
  command stopped at `maxTurns`.
- `SceneAgentSession.Busy`: a command sent while another one runs is not started.
- A placement into the object's own contents is refused.
- Objects inside a container move with it on grab, place and teleport.

## [1.0.0]

Initial GitHub distribution of Maverick XR Agent for Unity. This release carries the runtime and editor code previously published on Hugging Face at revision `1bb8c83224fd470b1a25fac1ee74ce83ac489c44`.

### Included

- Unity Package Manager installation for the `com.mcbuxrlab.maverick` package.
- Local llama.cpp server management with a GPU-first launch path and CPU fallback.
- Eleven scene tools, output checking, clarification handling, gaze input, a desktop chat box, and a command script runner.
- Editor commands for scene setup, interactive object registration, validation, and access to the model folder.
- Expanded installation, integration, model, evaluation, and troubleshooting documentation.
- A fixed model download reference and SHA-256 checksum. Weights remain hosted on Hugging Face.

### Compatibility and known issues

The documented setup targets Unity 2022.3 or later on Windows x64. Voice recognition and XR interface behavior are supplied by the application. Runtime behavior is unchanged from the referenced Hugging Face package; see [known implementation issues](Documentation~/troubleshooting.md#known-implementation-issues).
