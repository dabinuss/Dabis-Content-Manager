namespace DCM.Core.Configuration;

/// <summary>
/// Definiert die verfügbaren LLM-Modell-Presets für automatischen Download.
/// </summary>
public enum LlmModelPreset
{
    /// <summary>
    /// Benutzerdefiniertes Modell (manueller Pfad).
    /// </summary>
    Custom,

    /// <summary>
    /// Phi-3 Mini 4K Instruct Q4 (~2.2 GB).
    /// Kompaktes, schnelles Modell mit guter Qualität.
    /// </summary>
    Phi3Mini4kQ4
}

/// <summary>
/// Hilfsmethoden für LlmModelPreset.
/// </summary>
public static class LlmModelPresetExtensions
{
    /// <summary>
    /// Gibt den Dateinamen des Modells zurück.
    /// </summary>
    public static string? GetFileName(this LlmModelPreset preset)
    {
        return preset switch
        {
            LlmModelPreset.Phi3Mini4kQ4 => "Phi-3-mini-4k-instruct-q4.gguf",
            LlmModelPreset.Custom => null,
            _ => null
        };
    }

    /// <summary>
    /// Gibt die Download-URL für das Modell zurück (Hugging Face).
    /// </summary>
    public static string? GetDownloadUrl(this LlmModelPreset preset)
    {
        return preset switch
        {
            LlmModelPreset.Phi3Mini4kQ4 =>
                "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf/resolve/main/Phi-3-mini-4k-instruct-q4.gguf",
            LlmModelPreset.Custom => null,
            _ => null
        };
    }

    /// <summary>
    /// Gibt die ungefähre Dateigröße in Bytes zurück.
    /// </summary>
    public static long GetApproximateSizeBytes(this LlmModelPreset preset)
    {
        return preset switch
        {
            LlmModelPreset.Phi3Mini4kQ4 => 2200L * 1024 * 1024, // ~2.2 GB
            LlmModelPreset.Custom => 0,
            _ => 0
        };
    }

    /// <summary>
    /// Gibt eine benutzerfreundliche Größenbeschreibung zurück.
    /// </summary>
    public static string GetSizeDescription(this LlmModelPreset preset)
    {
        return preset switch
        {
            LlmModelPreset.Phi3Mini4kQ4 => "~2.2 GB",
            LlmModelPreset.Custom => "",
            _ => ""
        };
    }

    /// <summary>
    /// Gibt den Anzeigenamen für das UI zurück.
    /// </summary>
    public static string GetDisplayName(this LlmModelPreset preset)
    {
        return preset switch
        {
            LlmModelPreset.Phi3Mini4kQ4 => "Phi-3 Mini 4K (Q4, ~2.2 GB)",
            LlmModelPreset.Custom => "Eigenes Modell",
            _ => "Unbekannt"
        };
    }

    /// <summary>
    /// Gibt den zugehörigen LlmModelType für automatische Prompt-Formatierung zurück.
    /// </summary>
    public static LlmModelType GetModelType(this LlmModelPreset preset)
    {
        return preset switch
        {
            LlmModelPreset.Phi3Mini4kQ4 => LlmModelType.Phi3,
            LlmModelPreset.Custom => LlmModelType.Auto,
            _ => LlmModelType.Auto
        };
    }

    /// <summary>
    /// Gibt an, ob dieses Preset einen Download erfordert.
    /// </summary>
    public static bool RequiresDownload(this LlmModelPreset preset)
    {
        return preset != LlmModelPreset.Custom;
    }
}
