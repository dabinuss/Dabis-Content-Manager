namespace DCM.Core.Models;

/// <summary>
/// Repräsentiert einen Highlight-Kandidaten für einen Clip.
/// Wird vom LLM-Scoring generiert.
/// </summary>
public sealed class ClipCandidate
{
    /// <summary>
    /// Eindeutige ID des Kandidaten.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// ID des Quell-Drafts.
    /// </summary>
    public Guid SourceDraftId { get; set; }

    /// <summary>
    /// Startzeit des Clips im Quellvideo.
    /// </summary>
    public TimeSpan Start { get; set; }

    /// <summary>
    /// Endzeit des Clips im Quellvideo.
    /// </summary>
    public TimeSpan End { get; set; }

    /// <summary>
    /// Dauer des Clips.
    /// </summary>
    public TimeSpan Duration => End - Start;

    /// <summary>
    /// Bewertungsscore (0-100) vom LLM.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Kurze Begründung vom LLM für die Bewertung.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Vorschau-Text aus dem Transkript (max. 200 Zeichen).
    /// </summary>
    public string PreviewText { get; set; } = string.Empty;

    /// <summary>
    /// Ob der Kandidat vom Benutzer ausgewählt wurde.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Zeitpunkt der Erstellung.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Formatierte Startzeit für die UI (MM:SS oder HH:MM:SS).
    /// </summary>
    public string StartFormatted => FormatTimeSpan(Start);

    /// <summary>
    /// Formatierte Endzeit für die UI (MM:SS oder HH:MM:SS).
    /// </summary>
    public string EndFormatted => FormatTimeSpan(End);

    /// <summary>
    /// Formatierte Dauer für die UI (z.B. "45s" oder "1:30").
    /// </summary>
    public string DurationFormatted
    {
        get
        {
            var d = Duration;
            if (d.TotalMinutes < 1)
            {
                return $"{(int)d.TotalSeconds}s";
            }
            return $"{(int)d.TotalMinutes}:{d.Seconds:D2}";
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
        return $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
