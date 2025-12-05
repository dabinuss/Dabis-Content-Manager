namespace DCM.Transcription;

/// <summary>
/// Definiert die Phasen des Transkriptionsprozesses.
/// </summary>
public enum TranscriptionPhase
{
    /// <summary>
    /// Service wird initialisiert.
    /// </summary>
    Initializing,

    /// <summary>
    /// Dependencies werden heruntergeladen (FFmpeg, Whisper-Modell).
    /// </summary>
    DownloadingDependencies,

    /// <summary>
    /// Audio wird aus dem Video extrahiert.
    /// </summary>
    ExtractingAudio,

    /// <summary>
    /// Audio wird transkribiert.
    /// </summary>
    Transcribing,

    /// <summary>
    /// Transkription erfolgreich abgeschlossen.
    /// </summary>
    Completed,

    /// <summary>
    /// Transkription fehlgeschlagen.
    /// </summary>
    Failed
}