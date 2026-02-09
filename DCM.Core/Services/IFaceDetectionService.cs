using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Service für Gesichtserkennung und Crop-Berechnung.
/// </summary>
public interface IFaceDetectionService
{
    /// <summary>
    /// Gibt an, ob der Service einsatzbereit ist.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Analysiert ein Video und liefert Face-Detections pro Frame.
    /// </summary>
    Task<IReadOnlyList<FrameFaceAnalysis>> AnalyzeVideoAsync(
        string videoPath,
        TimeSpan sampleInterval,
        TimeSpan? startTime,
        TimeSpan? endTime,
        CancellationToken ct);

    /// <summary>
    /// Berechnet eine Crop-Region basierend auf Face-Analysen.
    /// </summary>
    CropRegionResult CalculateCropRegion(
        IReadOnlyList<FrameFaceAnalysis> analyses,
        PixelSize sourceSize,
        PixelSize targetSize,
        CropStrategy preferredStrategy = CropStrategy.MultipleFaces);
}
