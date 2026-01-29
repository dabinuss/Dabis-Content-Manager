namespace DCM.Llm;

/// <summary>
/// Enthält Fortschrittsinformationen für den Download eines LLM-Modells.
/// </summary>
public sealed class LlmModelDownloadProgress
{
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
    /// Gibt an, ob der Download abgeschlossen ist.
    /// </summary>
    public bool IsCompleted => Percent >= 100;

    /// <summary>
    /// Gibt an, ob ein Fehler aufgetreten ist.
    /// </summary>
    public bool IsError { get; init; }

    /// <summary>
    /// Erstellt einen neuen Download-Fortschrittsbericht.
    /// </summary>
    public LlmModelDownloadProgress(
        double percent,
        string? message = null,
        long bytesDownloaded = 0,
        long totalBytes = 0,
        bool isError = false)
    {
        Percent = Math.Clamp(percent, 0, 100);
        Message = message;
        BytesDownloaded = bytesDownloaded;
        TotalBytes = totalBytes;
        IsError = isError;
    }

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für laufenden Download.
    /// </summary>
    public static LlmModelDownloadProgress Downloading(
        double percent,
        string modelName,
        long bytesDownloaded = 0,
        long totalBytes = 0)
        => new(
            percent,
            $"LLM-Modell ({modelName}) wird heruntergeladen...",
            bytesDownloaded,
            totalBytes);

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für abgeschlossenen Download.
    /// </summary>
    public static LlmModelDownloadProgress Completed(long totalBytes)
        => new(100, "Download abgeschlossen", totalBytes, totalBytes);

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für einen Fehler.
    /// </summary>
    public static LlmModelDownloadProgress Error(string message)
        => new(0, message, isError: true);
}
