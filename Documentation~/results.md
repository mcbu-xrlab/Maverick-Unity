# Training and evaluation

This page records the results published with Maverick-4B-Unity-XR-Agent. Sources are the [model card](https://huggingface.co/ErenAta00/Maverick-4B-Unity-XR-Agent-GGUF) and [project article](https://huggingface.co/blog/ErenAta00/maverick-4b-unity-xr-agent). Moving the package to GitHub does not constitute a new benchmark run.

## Training

The model was fine-tuned from Qwen3-4B on 20,091 English conversations generated from templates and a catalog of 209 object types. Twenty-eight object types were held back for testing. Each training conversation was executed in a simulator and checked for its intended result. The reported data checks removed 501 conversations that overlapped with a test set. Approximately 8% of the conversations included simulated speech-recognition noise.

| Setting | Reported configuration |
| --- | --- |
| Method | QLoRA supervised fine-tuning with Unsloth |
| Adapter | Rank 16, alpha 32, attention and MLP projections |
| Schedule | One epoch, 2,512 steps, effective batch size 8 |
| Learning rate | 2e-4, cosine decay |
| Training sequence length | 1,536 tokens |
| Hardware | One NVIDIA T4 |
| Export | Merge into the dequantized 4-bit base, convert to Q8_0 GGUF, then quantize to Q4_K_M |

The dataset covered scene actions, clarification replies, spatial references, object names, unsupported requests, and deciding when to act, ask, search, or decline. According to the published methodology, test sets were frozen before training and checkpoint selection used separate development sets.

## ALFRED-derived instructions

The evaluation adapts 499 steps from ALFRED's validation splits into single-command requests. The input consists of a human-written instruction and a structured scene description. The expected action refers to the object used in the recorded demonstration. The tested tools are `grab`, `place`, and `set_state`.

A response is counted as correct when its first action selects the expected tool and object under the reported scoring procedure. This is a task adaptation, not the original ALFRED evaluation of visual perception, navigation, and complete household task execution. See [ALFRED](https://askforalfred.com/) for the original benchmark.

| Model | Parameters | Overall | Pick up | Put down | Switch on | Held-out object types |
| --- | --- | --- | --- | --- | --- | --- |
| Maverick-4B-Unity-XR-Agent | 4B | 83.8% | 88.5% | 73.0% | 96.0% | 91.7% |
| Qwen3-1.7B | 1.7B | 35.9% | 49.0% | 9.0% | 63.6% | 23.3% |
| Qwen3-4B | 4B | 57.1% | 41.0% | 58.5% | 86.9% | 55.0% |
| Ministral 3 14B | 14B | 67.1% | 56.0% | 67.0% | 89.9% | 66.7% |
| gpt-oss-120b | 120B | 57.1% | 57.5% | 46.0% | 78.8% | 53.3% |
| Nemotron-3 Super | 120B | 58.9% | 60.0% | 46.0% | 82.8% | 55.0% |

There are 200 pick-up, 200 put-down, and 99 switch-on items. The held-out subset contains 60 items. Nineteen of those use “cup,” a word present in training as a synonym for “mug”; the subset should not be described as containing only previously unseen words.

The base-model comparison is a gain of 26.7 percentage points in this setting. The reported error analysis attributes much of the improvement to fewer unnecessary clarification questions and fewer text-only responses where an action was expected. These results describe performance under this scene representation and scoring protocol, rather than a general ranking of the models.

The authors report wording and demonstration-label mismatches in part of the dataset. Those mismatches complicate interpretation of wrong-object errors. The item-level adjudications and a corrected-label evaluation are not included here, so no corrected accuracy or strict attainable ceiling is claimed on this page.

## Capability tests

The capability evaluation contains 2,900 template-based commands in 22 categories, grouped below. The published description reports held-out object types and sentence patterns. Many categories pair a scene with one matching object, where the model should act, with a scene containing multiple matches, where it should ask.

| Capability group | Commands | Accuracy |
| --- | --- | --- |
| Object types, synonyms, and general terms | 600 | 97.8% |
| Spatial references | 750 | 93.9% |
| Details not recorded in the scene | 400 | 99.5% |
| Clarification when objects fit equally well | 150 | 100.0% |
| Searching other rooms | 100 | 98.0% |
| Reporting an absent object | 100 | 100.0% |
| Declining an impossible action before trying it | 100 | 75.0% |
| References and held objects | 100 | 98.0% |
| State-change verbs | 100 | 99.0% |
| Verbs with multiple meanings | 150 | 99.3% |
| Simulated speech-recognition errors | 150 | 97.3% |
| Out-of-scope requests | 100 | 98.0% |
| Unsupported edits | 100 | 93.0% |
| **Overall** | **2,900** | **96.4%** |

These are controlled capability tests. Simulated speech noise is not an evaluation of microphone input or a complete speech-recognition pipeline.

## Output checking

The reported scores include the output check. The model card reports that it changes approximately 0.5% of responses. On the ALFRED-derived evaluation, accuracy is reported as 83.8% with the check and 83.6% without it. On unsupported edits, the reported result increases from 68% to 93%.

No invented tools or IDs were reported for Maverick in the ALFRED-derived results with the check enabled. This describes the observed test outputs. It does not guarantee that every future output is valid or that a valid ID is the correct target. The implementation's current validation limits are documented in [troubleshooting](troubleshooting.md#known-implementation-issues).

## Unity execution

The published Unity evaluation ran 440 commands drawn from the capability tests through a live scene and reported 97.3% correct execution with no runtime errors during those runs. This is not a separate 440-command sample of natural user speech.

Package checks covered setup in a new project, a built Windows application, editor tools, the 11 tools, clarification replies, unusual inputs, multiple rooms, scene reloads, and process cleanup. The report lists 54 passes out of 55 checks. The failing case used 170 objects in a room and exceeded the 4,096-token context; the package returned a context error.

The full test definitions, expected scene states, and raw reports are not included in this repository.

## Hardware measurements

Measurements were taken on Windows 11 with an RTX 3050 Ti laptop GPU with 4 GB of VRAM. Unity rendered at 30 FPS on the same GPU.

| Measurement | Reported value |
| --- | --- |
| Model-server GPU memory | 3.06 GB |
| Shared system memory | 96 MiB |
| First answer, model only, median | 1.18 seconds |
| Complete command, GPU cool | 0.5–3.6 seconds |
| Complete command, GPU at its thermal limit, median | 5.9 seconds |

Most commands require an action call and a confirmation call. An uncapped renderer made commands approximately five times slower in the reported setup. These figures do not include a measured speech pipeline and should not be presented as standalone-headset or XR-refresh-rate measurements.

## Reproduction status

This repository provides the Unity runtime and editor source, the system prompt, and a reference to the released GGUF. It does not include the training data, data generator, training scripts, simulator, frozen evaluation examples, baseline invocation configurations, or raw model outputs.

The available materials are enough to integrate the model and run your own scenes. They are not a complete reproduction package for the published benchmark tables. A full reproduction would also need exact model revisions, quantization formats, llama.cpp versions, decoding and reasoning settings, scoring code, and the evaluation inputs.
