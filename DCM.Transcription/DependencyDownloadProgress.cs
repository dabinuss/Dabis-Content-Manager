namespace DCM.Transcription;

/// <summary>
/// Definiert den Typ der Abhängigkeit, die heruntergeladen wird.
/// </summary>
public enum DependencyType
{
    /// <summary>
    /// FFmpeg für Audio-Extraktion.
    /// </summary>
    FFmpeg,

    /// <summary>
    /// Whisper-Modell für Transkription.
    /// </summary>
    WhisperModel
}

/// <summary>
/// Enthält Fortschrittsinformationen für den Download einer Abhängigkeit.
/// </summary>
public sealed class DependencyDownloadProgress
{
    /// <summary>
    /// Typ der Abhängigkeit, die heruntergeladen wird.
    /// </summary>
    public DependencyType Type { get; init; }

    /// <summary>
    /// Fortschritt in Prozent (0-100).
    /// </summary>
    public double Percent { get; init; }

    /// <summary>
    /// Optionale Statusmeldung.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Anzahl der bereits heruntergeladenen Bytes.
    /// </summary>
    public long BytesDownloaded { get; init; }

    /// <summary>
    /// Gesamtgröße des Downloads in Bytes (0 wenn unbekannt).
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Erstellt einen neuen Download-Fortschrittsbericht.
    /// </summary>
    public DependencyDownloadProgress(
        DependencyType type,
        double percent,
        string? message = null,
        long bytesDownloaded = 0,
        long totalBytes = 0)
    {
        Type = type;
        Percent = Math.Clamp(percent, 0, 100);
        Message = message;
        BytesDownloaded = bytesDownloaded;
        TotalBytes = totalBytes;
    }

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für FFmpeg-Download.
    /// </summary>
    public static DependencyDownloadProgress FFmpegDownload(double percent, string? message = null)
        => new(DependencyType.FFmpeg, percent, message ?? "FFmpeg wird heruntergeladen...");

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für Whisper-Modell-Download.
    /// </summary>
    public static DependencyDownloadProgress WhisperModelDownload(
        double percent,
        long bytesDownloaded = 0,
        long totalBytes = 0)
        => new(
            DependencyType.WhisperModel,
            percent,
            "Whisper-Modell wird heruntergeladen...",
            bytesDownloaded,
            totalBytes);
}