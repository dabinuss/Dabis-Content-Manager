using DCM.Core.Configuration;

namespace DCM.Transcription;

/// <summary>
/// Interface für den Transkriptions-Service.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Gibt an, ob der Service bereit ist (alle Dependencies verfügbar).
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Gibt den aktuellen Status der Abhängigkeiten zurück.
    /// </summary>
    DependencyStatus GetDependencyStatus();

    /// <summary>
    /// Stellt sicher, dass alle Abhängigkeiten vorhanden sind.
    /// Lädt fehlende Abhängigkeiten herunter.
    /// </summary>
    /// <param name="modelSize">Gewünschte Modellgröße für Whisper.</param>
    /// <param name="progress">Optionaler Progress-Reporter für Downloads.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    /// <returns>True wenn alle Abhängigkeiten verfügbar sind.</returns>
    Task<bool> EnsureDependenciesAsync(
        WhisperModelSize modelSize,
        IProgress<DependencyDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Entfernt alle Whisper-Modelle außer dem angegebenen.
    /// </summary>
    void RemoveOtherModels(WhisperModelSize keepSize);

    /// <summary>
    /// Transkribiert eine Videodatei.
    /// </summary>
    /// <param name="videoFilePath">Pfad zur Videodatei.</param>
    /// <param name="language">Sprache für die Transkription (z.B. "de", null für Auto-Detect).</param>
    /// <param name="progress">Optionaler Progress-Reporter.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    /// <returns>Das Transkriptionsergebnis.</returns>
    Task<TranscriptionResult> TranscribeAsync(
        string videoFilePath,
        string? language = null,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
