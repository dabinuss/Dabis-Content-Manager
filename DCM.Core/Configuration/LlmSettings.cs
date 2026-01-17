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

/// <summary>
/// Definiert die unterstützten Modelltypen für automatische Prompt-Formatierung.
/// </summary>
public enum LlmModelType
{
    /// <summary>
    /// Automatische Erkennung basierend auf dem Dateinamen.
    /// </summary>
    Auto,

    /// <summary>
    /// Microsoft Phi-3 Modelle.
    /// </summary>
    Phi3,

    /// <summary>
    /// Mistral / Ministral Modelle (v7 Tekken Template).
    /// </summary>
    Mistral3
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
    /// Modelltyp für Prompt-Formatierung. Auto erkennt anhand des Dateinamens.
    /// </summary>
    public LlmModelType ModelType { get; set; } = LlmModelType.Auto;

    /// <summary>
    /// System-Prompt, der dem Modell als Kontext übergeben wird.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Maximale Anzahl an Tokens für die Generierung.
    /// </summary>
    public int MaxTokens { get; set; } = 256;

    /// <summary>
    /// Temperatur für die Generierung (0.0 = deterministisch, 1.0 = kreativ).
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Kontext-Größe in Tokens.
    /// </summary>
    public int ContextSize { get; set; } = 4096;

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