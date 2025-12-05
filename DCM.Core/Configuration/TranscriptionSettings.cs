namespace DCM.Core.Configuration;

/// <summary>
/// Einstellungen für die automatische Video-Transkription.
/// </summary>
public sealed class TranscriptionSettings
{
    /// <summary>
    /// Gibt an, ob bei Video-Auswahl automatisch transkribiert werden soll.
    /// </summary>
    public bool AutoTranscribeOnVideoSelect { get; set; } = false;

    /// <summary>
    /// Gewünschte Whisper-Modellgröße.
    /// </summary>
    public WhisperModelSize ModelSize { get; set; } = WhisperModelSize.Small;

    /// <summary>
    /// Sprache für die Transkription (z.B. "de" für Deutsch).
    /// Null bedeutet automatische Erkennung.
    /// </summary>
    public string? Language { get; set; } = "de";

    /// <summary>
    /// Gibt an, ob die Transkription konfiguriert und einsatzbereit ist.
    /// </summary>
    public bool IsConfigured => true; // Wird später erweitert
}