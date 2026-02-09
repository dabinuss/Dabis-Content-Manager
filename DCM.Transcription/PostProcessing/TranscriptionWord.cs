namespace DCM.Transcription.PostProcessing;

/// <summary>
/// Repräsentiert ein einzelnes Wort mit Zeitstempeln aus der Transkription.
/// Ermöglicht präzises Word-Level-Timing für Untertitel.
/// </summary>
public sealed class TranscriptionWord
{
    /// <summary>
    /// Das transkribierte Wort.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Startzeit des Wortes im Audio/Video.
    /// </summary>
    public required TimeSpan Start { get; init; }

    /// <summary>
    /// Endzeit des Wortes im Audio/Video.
    /// </summary>
    public required TimeSpan End { get; init; }

    /// <summary>
    /// Konfidenz/Wahrscheinlichkeit der Erkennung (0.0 - 1.0).
    /// </summary>
    public float Probability { get; init; } = 1.0f;

    /// <summary>
    /// Dauer des Wortes.
    /// </summary>
    public TimeSpan Duration => End - Start;
}
