namespace DCM.Core.Models;

/// <summary>
/// Einstellungen für TikTok-Style Untertitel in Clips.
/// </summary>
public sealed class ClipSubtitleSettings
{
    /// <summary>
    /// Schriftart für die Untertitel.
    /// </summary>
    public string FontFamily { get; set; } = "Arial Black";

    /// <summary>
    /// Schriftgröße in Pixeln.
    /// </summary>
    public int FontSize { get; set; } = 72;

    /// <summary>
    /// Füllfarbe der Schrift (Hex, z.B. "#FFFFFF").
    /// </summary>
    public string FillColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// Farbe der Outline (Hex, z.B. "#000000").
    /// </summary>
    public string OutlineColor { get; set; } = "#000000";

    /// <summary>
    /// Breite der Outline in Pixeln.
    /// </summary>
    public int OutlineWidth { get; set; } = 4;

    /// <summary>
    /// Schattenfarbe (Hex mit Alpha, z.B. "#80000000"). Null für keinen Schatten.
    /// </summary>
    public string? ShadowColor { get; set; } = "#80000000";

    /// <summary>
    /// Tiefe des Schattens in Pixeln.
    /// </summary>
    public int ShadowDepth { get; set; } = 2;

    /// <summary>
    /// Vertikale Position (0.0 = oben, 1.0 = unten).
    /// Standard: 0.70 (unteres Drittel).
    /// </summary>
    public double PositionY { get; set; } = 0.70;

    /// <summary>
    /// Highlight-Farbe für das aktive Wort (Hex, z.B. "#FFFF00").
    /// </summary>
    public string HighlightColor { get; set; } = "#FFFF00";

    /// <summary>
    /// Ob Wort-für-Wort Highlighting aktiviert ist.
    /// </summary>
    public bool WordByWordHighlight { get; set; } = true;

    /// <summary>
    /// Erstellt eine Kopie der Einstellungen.
    /// </summary>
    public ClipSubtitleSettings Clone()
    {
        return new ClipSubtitleSettings
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            FillColor = FillColor,
            OutlineColor = OutlineColor,
            OutlineWidth = OutlineWidth,
            ShadowColor = ShadowColor,
            ShadowDepth = ShadowDepth,
            PositionY = PositionY,
            HighlightColor = HighlightColor,
            WordByWordHighlight = WordByWordHighlight
        };
    }
}
