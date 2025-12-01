namespace DCM.Core.Configuration;

public sealed class LlmSettings
{
    /// <summary>
    /// LLM-Modus: "None", "Local", oder zukünftig "Cloud".
    /// </summary>
    public string Mode { get; set; } = "None";

    /// <summary>
    /// Pfad zur lokalen GGUF-Modelldatei (für Mode == "Local").
    /// </summary>
    public string? LocalModelPath { get; set; }

    /// <summary>
    /// Maximale Anzahl an Tokens für die Generierung.
    /// </summary>
    public int MaxTokens { get; set; } = 256;

    /// <summary>
    /// Temperatur für die Generierung (0.0 = deterministisch, 1.0 = kreativ).
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Gibt an, ob der Local-Modus aktiv ist.
    /// </summary>
    public bool IsLocalMode => string.Equals(Mode, "Local", StringComparison.OrdinalIgnoreCase);
}