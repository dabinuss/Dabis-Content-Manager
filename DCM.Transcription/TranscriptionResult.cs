using DCM.Transcription.PostProcessing;

namespace DCM.Transcription;

/// <summary>
/// Enthält das Ergebnis einer Transkription.
/// </summary>
public sealed class TranscriptionResult
{
    /// <summary>
    /// Gibt an, ob die Transkription erfolgreich war.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Der transkribierte Text (bei Erfolg).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Die einzelnen Transkriptions-Segmente mit Zeitstempeln (bei Erfolg).
    /// Enthält präzisere Timing-Informationen als der reine Text.
    /// </summary>
    public IReadOnlyList<TranscriptionSegment>? Segments { get; init; }

    /// <summary>
    /// Dauer der Transkription.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Fehlermeldung (bei Misserfolg).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Erstellt ein erfolgreiches Ergebnis.
    /// </summary>
    public static TranscriptionResult Ok(string text, TimeSpan duration)
        => new()
        {
            Success = true,
            Text = text,
            Duration = duration
        };

    /// <summary>
    /// Erstellt ein erfolgreiches Ergebnis mit Segmenten.
    /// </summary>
    public static TranscriptionResult Ok(string text, IReadOnlyList<TranscriptionSegment> segments, TimeSpan duration)
        => new()
        {
            Success = true,
            Text = text,
            Segments = segments,
            Duration = duration
        };

    /// <summary>
    /// Erstellt ein fehlgeschlagenes Ergebnis.
    /// </summary>
    public static TranscriptionResult Failed(string errorMessage, TimeSpan duration = default)
        => new()
        {
            Success = false,
            ErrorMessage = errorMessage,
            Duration = duration
        };

    /// <summary>
    /// Erstellt ein Ergebnis für eine abgebrochene Transkription.
    /// </summary>
    public static TranscriptionResult Cancelled(TimeSpan duration = default)
        => new()
        {
            Success = false,
            ErrorMessage = "Transkription wurde abgebrochen.",
            Duration = duration
        };
}
