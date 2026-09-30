# Model download and direct use

Maverick-4B-Unity-XR-Agent is a fine-tune of Qwen3-4B for selecting tools and objects in structured Unity scenes. The released weights are a Q4_K_M GGUF for llama.cpp.

## Download

[Download Maverick-4B-Unity-XR-Agent-Q4_K_M.gguf](https://huggingface.co/ErenAta00/Maverick-4B-Unity-XR-Agent-GGUF/resolve/856bd7b345f9b74ff871fa6c0bfe0d2432b1247f/Maverick-4B-Unity-XR-Agent-Q4_K_M.gguf?download=true)

This link selects a fixed Hugging Face revision. The [model repository](https://huggingface.co/ErenAta00/Maverick-4B-Unity-XR-Agent-GGUF) lists the current files and full model card.

| Property | Value |
| --- | --- |
| File | `Maverick-4B-Unity-XR-Agent-Q4_K_M.gguf` |
| Size | 2,497,280,928 bytes, approximately 2.50 GB / 2.33 GiB |
| Hugging Face repository | `ErenAta00/Maverick-4B-Unity-XR-Agent-GGUF` |
| Pinned revision | `856bd7b345f9b74ff871fa6c0bfe0d2432b1247f` |
| Base model | `Qwen/Qwen3-4B` |
| Language | English |
| Quantization | Q4_K_M |
| License | Apache-2.0, distributed with the model |

Expected SHA-256, as recorded by Hugging Face for this file:

```text
7588d0783e22ea3b4d87baf8730fb72272d7bda461c8593cffb7c264872d1a36
```

After downloading, verify it in PowerShell from the folder containing the file:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '.\Maverick-4B-Unity-XR-Agent-Q4_K_M.gguf'
```

Letter case in the printed hash does not matter. For Unity, put the file in `Assets/StreamingAssets/SceneAgent/` beside `llama-server.exe`. Set `LocalModelServer.modelPath` explicitly if that folder contains several models.

## Why the weights are hosted separately

Keeping the weights on Hugging Face makes the Unity package small and leaves a single download location for the model. A package update does not need to transfer the weights again.

The released GGUF exceeds GitHub's 100 MiB regular Git file limit and the 2 GiB per-asset limit stated in its release documentation. Git LFS also has plan-specific limits. See GitHub's documentation on [large files](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github), [release assets](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases), and [Git LFS](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-git-large-file-storage).

## Run the model without Unity

From a folder containing a compatible Windows `llama-server.exe` and the downloaded model:

```powershell
.\llama-server.exe -m .\Maverick-4B-Unity-XR-Agent-Q4_K_M.gguf --jinja -c 4096 -ngl 99 --host 127.0.0.1 --port 8091
```

For a CPU configuration, select a suitable runtime and use its documented CPU options. The Unity launcher uses `--device none --no-op-offload -ngl 0 -t 8` for its CPU attempt.

Use the system prompt from [`Runtime/Resources/SceneAgent/system_prompt.txt`](../Runtime/Resources/SceneAgent/system_prompt.txt). Preserve it when evaluating the released model. The model was trained to answer with text or `<tool_call>` blocks containing a tool name and arguments.

This Python example sends the first request. Run it from the root of this repository with Python 3 and a server already running on port 8091:

```python
import json
from pathlib import Path
from urllib.request import Request, urlopen

system = Path("Runtime/Resources/SceneAgent/system_prompt.txt").read_text(
    encoding="utf-8"
)
scene = {
    "player": {
        "room": "kitchen", "gaze": None,
        "left_hand": None, "right_hand": None,
    },
    "locations": ["kitchen"],
    "visible_objects": [
        {"id": "mug_1", "label": "mug", "color": "red",
         "on": "table_1", "pos": [0.4, 0.8, 1.2]},
        {"id": "table_1", "label": "table",
         "on": "floor", "pos": [0.5, 0.0, 1.2]},
        {"id": "counter_1", "label": "counter",
         "on": "floor", "pos": [2.0, 0.0, 0.3]},
    ],
}
body = {
    "messages": [
        {"role": "system", "content": system},
        {"role": "user", "content": "Scene: " + json.dumps(scene)
         + "\nRequest: put the red mug on the counter"},
    ],
    "temperature": 0,
    "max_tokens": 192,
}
request = Request(
    "http://127.0.0.1:8091/v1/chat/completions",
    data=json.dumps(body).encode("utf-8"),
    headers={"Content-Type": "application/json"},
)
with urlopen(request, timeout=120) as response:
    message = json.load(response)["choices"][0]["message"]
print(message.get("content", ""))
```

This example prints a response; it does not execute an action or implement the complete agent loop. A client must parse and validate the calls, execute accepted actions, return each result as a `tool` message, and call the model again. Stop when the model replies in text, asks for clarification, or reaches your configured turn limit. The Unity package implements that loop.

The package reads calls from `message.content`. A different server configuration that moves calls into a separate `tool_calls` field may require a client adaptation. “OpenAI-compatible API” describes the HTTP interface; it does not guarantee identical tool-call formatting across servers.

## Model behavior and scope

The model uses the supplied object IDs. It can resolve descriptions and synonyms, ask which object the user means, search other rooms, and explain unsupported requests. It sometimes attempts an impossible action before the engine rejects it. Keep execution checks in place.

The output check is part of the reported evaluation setup. It checks known tools, enumerated values, and scene IDs, then uses constrained decoding selectively. It is not a complete type checker or a guarantee of semantic correctness. See [known issues](troubleshooting.md#known-implementation-issues).

The published model is intended for virtual scene interactions. It was not evaluated for robot control, safety-critical decisions, or irreversible real-world actions. Scene-question answers and standalone headset performance have not been evaluated.

See [training and evaluation](results.md) for the dataset construction, reported measurements, and limits of the available evidence.
