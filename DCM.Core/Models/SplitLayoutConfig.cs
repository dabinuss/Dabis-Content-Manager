namespace DCM.Core.Models;

/// <summary>
/// Konfiguration für Solo/Duo-Split-Layout.
/// </summary>
public sealed class SplitLayoutConfig
{
    /// <summary>
    /// Aktiviert das Split-Layout. `false` bedeutet Solo-Handling.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gewähltes Preset.
    /// </summary>
    public SplitLayoutPreset Preset { get; set; } = SplitLayoutPreset.Auto;

    /// <summary>
    /// Hauptbereich im 9:16-Canvas.
    /// </summary>
    public NormalizedRect PrimaryRegion { get; set; } = NormalizedRect.FullFrame();

    /// <summary>
    /// Zweitbereich im 9:16-Canvas (bei Duo-Modus).
    /// </summary>
    public NormalizedRect SecondaryRegion { get; set; } = new()
    {
        X = 0,
        Y = 0.55,
        Width = 1,
        Height = 0.45
    };

    /// <summary>
    /// Minimale Regionsgröße (normiert 0..1).
    /// </summary>
    public double MinRegionSize { get; set; } = 0.20;

    /// <summary>
    /// Maximale Regionsgröße (normiert 0..1).
    /// </summary>
    public double MaxRegionSize { get; set; } = 1.0;

    /// <summary>
    /// Versucht beim Auto-Preset Gesichter zu berücksichtigen.
    /// </summary>
    public bool AutoDetectFaces { get; set; } = true;

    public SplitLayoutConfig Clone()
    {
        return new SplitLayoutConfig
        {
            Enabled = Enabled,
            Preset = Preset,
            PrimaryRegion = (PrimaryRegion ?? NormalizedRect.FullFrame()).Clone().ClampToCanvas(MinRegionSize, MaxRegionSize),
            SecondaryRegion = (SecondaryRegion ?? NormalizedRect.FullFrame()).Clone().ClampToCanvas(MinRegionSize, MaxRegionSize),
            MinRegionSize = MinRegionSize,
            MaxRegionSize = MaxRegionSize,
            AutoDetectFaces = AutoDetectFaces
        };
    }
}
