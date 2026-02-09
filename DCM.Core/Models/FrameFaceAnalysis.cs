namespace DCM.Core.Models;

/// <summary>
/// Gesichtserkennungsergebnis für einen einzelnen Frame.
/// </summary>
public sealed class FrameFaceAnalysis
{
    /// <summary>
    /// Zeitstempel des Frames im Video.
    /// </summary>
    public TimeSpan Timestamp { get; init; }

    /// <summary>
    /// Erkannte Gesichter in diesem Frame.
    /// </summary>
    public IReadOnlyList<FaceDetectionResult> Faces { get; init; } = Array.Empty<FaceDetectionResult>();
}

/// <summary>
/// Strategie für das Portrait-Cropping.
/// </summary>
public enum CropStrategy
{
    /// <summary>
    /// Keine Gesichter erkannt -> Center-Fallback.
    /// </summary>
    CenterFallback = 0,

    /// <summary>
    /// Ein dominantes Gesicht.
    /// </summary>
    SingleFace = 1,

    /// <summary>
    /// Mehrere Gesichter, alle eingeschlossen.
    /// </summary>
    MultipleFaces = 2,

    /// <summary>
    /// Mehrere Gesichter, größtes priorisiert.
    /// </summary>
    DominantFace = 3
}
