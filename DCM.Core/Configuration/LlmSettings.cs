namespace DCM.Core.Configuration;

/// <summary>
/// Definiert die verfügbaren LLM-Modi.
/// </summary>
public enum LlmMode
{
    /// <summary>
    /// LLM-Funktionen sind deaktiviert.
    /// </summary>
    Off,

    /// <summary>
    /// Lokales GGUF-Modell via LLamaSharp.
    /// </summary>
    Local

    // Zukünftig: Api
}

public sealed class LlmSettings
{
    /// <summary>
    /// LLM-Modus: Off oder Local.
    /// </summary>
    public LlmMode Mode { get; set; } = LlmMode.Off;

    /// <summary>
    /// Pfad zur lokalen GGUF-Modelldatei (für Mode == Local).
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
    /// Optionale Custom-Anweisung für Titel-Generierung.
    /// Wird dem Standard-Prompt vorangestellt.
    /// </summary>
    public string? TitleCustomPrompt { get; set; }

    /// <summary>
    /// Optionale Custom-Anweisung für Beschreibungs-Generierung.
    /// Wird dem Standard-Prompt vorangestellt.
    /// </summary>
    public string? DescriptionCustomPrompt { get; set; }

    /// <summary>
    /// Optionale Custom-Anweisung für Tag-Generierung.
    /// Wird dem Standard-Prompt vorangestellt.
    /// </summary>
    public string? TagsCustomPrompt { get; set; }

    /// <summary>
    /// Gibt an, ob der Local-Modus aktiv ist.
    /// </summary>
    public bool IsLocalMode => Mode == LlmMode.Local;

    /// <summary>
    /// Gibt an, ob LLM-Funktionen deaktiviert sind.
    /// </summary>
    public bool IsOff => Mode == LlmMode.Off;
}