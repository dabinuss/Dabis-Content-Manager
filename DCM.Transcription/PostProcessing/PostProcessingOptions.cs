namespace DCM.Transcription.PostProcessing;

/// <summary>
/// Konfigurationsoptionen für die Transkriptions-Nachverarbeitung.
/// </summary>
public sealed class PostProcessingOptions
{
    /// <summary>
    /// Ob Wortdopplungen entfernt werden sollen (z.B. "und und" → "und").
    /// Standard: true
    /// </summary>
    public bool RemoveWordDuplications { get; init; } = true;

    /// <summary>
    /// Ob bei langen Pausen Absätze eingefügt werden sollen.
    /// Standard: true
    /// </summary>
    public bool InsertParagraphs { get; init; } = true;

    /// <summary>
    /// Pausenlänge ab der ein neuer Absatz eingefügt wird.
    /// Standard: 1.5 Sekunden
    /// </summary>
    public TimeSpan ParagraphPauseThreshold { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Standard-Optionen.
    /// </summary>
    public static PostProcessingOptions Default => new();

    /// <summary>
    /// Minimale Verarbeitung - nur Whitespace-Normalisierung.
    /// </summary>
    public static PostProcessingOptions Minimal => new()
    {
        RemoveWordDuplications = false,
        InsertParagraphs = false
    };
}