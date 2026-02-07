namespace DCM.Core.Models;

/// <summary>
/// Modus für das Portrait-Cropping beim Clip-Rendering.
/// </summary>
public enum CropMode
{
    /// <summary>
    /// Kein Cropping - Original-Seitenverhältnis beibehalten.
    /// </summary>
    None = -1,

    /// <summary>
    /// Automatische Gesichtserkennung zur Positionierung.
    /// Fällt auf Center zurück, wenn kein Gesicht erkannt wird.
    /// </summary>
    AutoDetect = 0,

    /// <summary>
    /// Einfaches Center-Crop ohne Gesichtserkennung.
    /// </summary>
    Center = 1,

    /// <summary>
    /// Manuell definierter Crop-Bereich.
    /// </summary>
    Manual = 2,

    /// <summary>
    /// Split-Layout mit zwei Bereichen (z.B. Top/Bottom oder Left/Right).
    /// </summary>
    SplitLayout = 3
}
