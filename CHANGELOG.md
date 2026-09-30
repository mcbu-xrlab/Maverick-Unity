# Changelog

## 1.0.0

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
