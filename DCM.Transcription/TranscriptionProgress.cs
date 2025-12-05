namespace DCM.Transcription;

/// <summary>
/// Enthält Fortschrittsinformationen für eine laufende Transkription.
/// </summary>
public sealed class TranscriptionProgress
{
    /// <summary>
    /// Aktuelle Phase der Transkription.
    /// </summary>
    public TranscriptionPhase Phase { get; init; }

    /// <summary>
    /// Fortschritt in Prozent (0-100).
    /// </summary>
    public double Percent { get; init; }

    /// <summary>
    /// Optionale Statusmeldung.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Geschätzte verbleibende Zeit (falls verfügbar).
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    /// <summary>
    /// Erstellt einen neuen Fortschrittsbericht.
    /// </summary>
    public TranscriptionProgress(
        TranscriptionPhase phase,
        double percent,
        string? message = null,
        TimeSpan? estimatedTimeRemaining = null)
    {
        Phase = phase;
        Percent = Math.Clamp(percent, 0, 100);
        Message = message;
        EstimatedTimeRemaining = estimatedTimeRemaining;
    }

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für die Initialisierungsphase.
    /// </summary>
    public static TranscriptionProgress Initializing(string? message = null)
        => new(TranscriptionPhase.Initializing, 0, message ?? "Initialisiere...");

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für die Audio-Extraktion.
    /// </summary>
    public static TranscriptionProgress ExtractingAudio(double percent)
        => new(TranscriptionPhase.ExtractingAudio, percent, "Audio wird extrahiert...");

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für die Transkription.
    /// </summary>
    public static TranscriptionProgress Transcribing(double percent, TimeSpan? estimatedRemaining = null)
        => new(TranscriptionPhase.Transcribing, percent, "Transkribiere...", estimatedRemaining);

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für den Abschluss.
    /// </summary>
    public static TranscriptionProgress Completed()
        => new(TranscriptionPhase.Completed, 100, "Transkription abgeschlossen.");

    /// <summary>
    /// Erstellt einen Fortschrittsbericht für einen Fehler.
    /// </summary>
    public static TranscriptionProgress Failed(string errorMessage)
        => new(TranscriptionPhase.Failed, 0, errorMessage);
}