namespace DCM.Transcription.PostProcessing;

/// <summary>
/// Repräsentiert ein einzelnes Transkriptions-Segment mit Zeitstempeln.
/// </summary>
public sealed class TranscriptionSegment
{
    /// <summary>
    /// Der transkribierte Text des Segments.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Startzeit des Segments im Audio/Video.
    /// </summary>
    public required TimeSpan Start { get; init; }

    /// <summary>
    /// Endzeit des Segments im Audio/Video.
    /// </summary>
    public required TimeSpan End { get; init; }

    /// <summary>
    /// Dauer des Segments.
    /// </summary>
    public TimeSpan Duration => End - Start;
}