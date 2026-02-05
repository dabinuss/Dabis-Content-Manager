namespace DCM.Core.Models;

/// <summary>
/// Ergebnis einer Gesichtserkennung.
/// </summary>
public sealed class FaceDetectionResult
{
    /// <summary>
    /// Bounding Box des Gesichts.
    /// </summary>
    public FaceRect BoundingBox { get; init; }

    /// <summary>
    /// Landmarks (5 Punkte: Augen, Nase, Mundwinkel).
    /// </summary>
    public FacePoint[] Landmarks { get; init; } = Array.Empty<FacePoint>();

    /// <summary>
    /// Konfidenz der Erkennung (0.0 - 1.0).
    /// </summary>
    public float Confidence { get; init; }
}
