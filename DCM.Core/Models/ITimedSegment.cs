namespace DCM.Core.Models;

/// <summary>
/// Interface für zeitgestempelte Segmente.
/// Ermöglicht Verwendung von Transkript-Segmenten ohne direkte Abhängigkeit zu DCM.Transcription.
/// </summary>
public interface ITimedSegment
{
    /// <summary>
    /// Der Text des Segments.
    /// </summary>
    string Text { get; }

    /// <summary>
    /// Startzeit des Segments.
    /// </summary>
    TimeSpan Start { get; }

    /// <summary>
    /// Endzeit des Segments.
    /// </summary>
    TimeSpan End { get; }
}

/// <summary>
/// Einfache Implementierung eines zeitgestempelten Segments für den Clipper.
/// </summary>
public sealed class TimedSegment : ITimedSegment
{
    /// <summary>
    /// Der Text des Segments.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Startzeit des Segments.
    /// </summary>
    public required TimeSpan Start { get; init; }

    /// <summary>
    /// Endzeit des Segments.
    /// </summary>
    public required TimeSpan End { get; init; }

    /// <summary>
    /// Dauer des Segments.
    /// </summary>
    public TimeSpan Duration => End - Start;
}
