using DCM.Core.Configuration;

namespace DCM.Transcription;

/// <summary>
/// Enthält den Status aller Abhängigkeiten für die Transkription.
/// </summary>
public sealed class DependencyStatus
{
    /// <summary>
    /// Gibt an, ob FFmpeg verfügbar ist.
    /// </summary>
    public bool FFmpegAvailable { get; init; }

    /// <summary>
    /// Pfad zur FFmpeg-Executable (falls verfügbar).
    /// </summary>
    public string? FFmpegPath { get; init; }

    /// <summary>
    /// Pfad zur FFprobe-Executable (falls verfügbar).
    /// </summary>
    public string? FFprobePath { get; init; }

    /// <summary>
    /// Gibt an, ob ein Whisper-Modell verfügbar ist.
    /// </summary>
    public bool WhisperModelAvailable { get; init; }

    /// <summary>
    /// Pfad zum Whisper-Modell (falls verfügbar).
    /// </summary>
    public string? WhisperModelPath { get; init; }

    /// <summary>
    /// Größe des installierten Whisper-Modells (falls verfügbar).
    /// </summary>
    public WhisperModelSize? InstalledModelSize { get; init; }

    /// <summary>
    /// Gibt an, ob alle Abhängigkeiten verfügbar sind.
    /// </summary>
    public bool AllAvailable => FFmpegAvailable && WhisperModelAvailable;

    /// <summary>
    /// Erstellt einen Status, bei dem nichts verfügbar ist.
    /// </summary>
    public static DependencyStatus None => new()
    {
        FFmpegAvailable = false,
        WhisperModelAvailable = false
    };

    /// <summary>
    /// Erstellt einen Status, bei dem alles verfügbar ist.
    /// </summary>
    public static DependencyStatus AllReady(
        string ffmpegPath,
        string? ffprobePath,
        string whisperModelPath,
        WhisperModelSize modelSize) => new()
    {
        FFmpegAvailable = true,
        FFmpegPath = ffmpegPath,
        FFprobePath = ffprobePath,
        WhisperModelAvailable = true,
        WhisperModelPath = whisperModelPath,
        InstalledModelSize = modelSize
    };
}
