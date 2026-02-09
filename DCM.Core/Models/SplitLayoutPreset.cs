namespace DCM.Core.Models;

/// <summary>
/// Vordefinierte Layout-Modi für Solo/Duo-Clips.
/// </summary>
public enum SplitLayoutPreset
{
    /// <summary>
    /// Automatische Auswahl anhand von Inhalt/Gesichtern.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Nur ein Bereich (klassischer Solo-Clip).
    /// </summary>
    Solo = 1,

    /// <summary>
    /// Video oben, Facecam unten.
    /// </summary>
    TopBottom = 2,

    /// <summary>
    /// Video links, Facecam rechts.
    /// </summary>
    LeftRight = 3,

    /// <summary>
    /// Frei definierte Regionen.
    /// </summary>
    Custom = 4
}
