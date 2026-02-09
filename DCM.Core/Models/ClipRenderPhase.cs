namespace DCM.Core.Models;

/// <summary>
/// Phasen des Clip-Render-Prozesses.
/// </summary>
public enum ClipRenderPhase
{
    /// <summary>
    /// Warten auf Start.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Gesichtserkennung läuft.
    /// </summary>
    FaceDetection = 1,

    /// <summary>
    /// Crop-Region wird berechnet.
    /// </summary>
    CropCalculation = 2,

    /// <summary>
    /// Untertitel werden generiert.
    /// </summary>
    SubtitleGeneration = 3,

    /// <summary>
    /// Video wird geschnitten und gerendert.
    /// </summary>
    VideoRendering = 4,

    /// <summary>
    /// Nachbearbeitung (Metadata, Thumbnail, etc.).
    /// </summary>
    PostProcessing = 5,

    /// <summary>
    /// Erfolgreich abgeschlossen.
    /// </summary>
    Completed = 6,

    /// <summary>
    /// Fehlgeschlagen.
    /// </summary>
    Failed = 7,

    /// <summary>
    /// Abgebrochen.
    /// </summary>
    Cancelled = 8
}
