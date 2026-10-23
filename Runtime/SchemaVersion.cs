namespace SceneAgent
{
    /// <summary>Which serialization the model was trained on. V0: Maverick 1.0 (scene, 11 tools, plain
    /// results). V1: Maverick 1.1 (schema 1.1: history and undo, attention, people, richer tool results).</summary>
    public enum SchemaVersion { V0, V1 }
}
