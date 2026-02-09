namespace DCM.Core.Models;

/// <summary>
/// Berechnete Crop-Region für das Portrait-Format.
/// </summary>
public sealed class CropRegionResult
{
    /// <summary>
    /// Ergebnisrechteck des Crops.
    /// </summary>
    public CropRectangle Region { get; init; }

    /// <summary>
    /// Verwendete Crop-Strategie.
    /// </summary>
    public CropStrategy Strategy { get; init; }

    /// <summary>
    /// Anzahl der berücksichtigten Gesichter.
    /// </summary>
    public int FacesConsidered { get; init; }

    /// <summary>
    /// Debug-Informationen zur Berechnung.
    /// </summary>
    public string? DebugInfo { get; init; }

    /// <summary>
    /// Breite des Quell-Videos.
    /// </summary>
    public int SourceWidth { get; init; }

    /// <summary>
    /// Höhe des Quell-Videos.
    /// </summary>
    public int SourceHeight { get; init; }

    /// <summary>
    /// Ob die Crop-Region auf einer Gesichtserkennung basiert.
    /// </summary>
    public bool BasedOnFaceDetection { get; init; }

    public int CropX => Region.X;
    public int CropY => Region.Y;
    public int CropWidth => Region.Width;
    public int CropHeight => Region.Height;

    /// <summary>
    /// Normalisierte X-Position des Crop-Zentrums (0-1).
    /// </summary>
    public double NormalizedCenterX => SourceWidth > 0
        ? Region.CenterX / SourceWidth
        : 0.5;

    /// <summary>
    /// Generiert den FFmpeg Crop-Filter-String.
    /// </summary>
    public string ToFfmpegCropFilter()
    {
        return $"crop={Region.Width}:{Region.Height}:{Region.X}:{Region.Y}";
    }

    /// <summary>
    /// Erstellt eine Center-Crop-Region für ein 9:16 Portrait-Format.
    /// </summary>
    public static CropRegionResult CreateCenterCrop(int sourceWidth, int sourceHeight)
    {
        var cropWidth = (int)Math.Round(sourceHeight * 9.0 / 16.0);
        if (cropWidth > sourceWidth)
        {
            cropWidth = sourceWidth;
        }

        var cropX = Math.Max(0, (sourceWidth - cropWidth) / 2);

        return new CropRegionResult
        {
            Region = new CropRectangle(cropX, 0, cropWidth, sourceHeight),
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            Strategy = CropStrategy.CenterFallback,
            BasedOnFaceDetection = false
        };
    }

    /// <summary>
    /// Erstellt eine Crop-Region zentriert auf eine Gesichtsposition.
    /// </summary>
    public static CropRegionResult CreateFaceCenteredCrop(
        int sourceWidth,
        int sourceHeight,
        double normalizedFaceCenterX)
    {
        var cropWidth = (int)Math.Round(sourceHeight * 9.0 / 16.0);
        if (cropWidth > sourceWidth)
        {
            cropWidth = sourceWidth;
        }

        var faceCenterX = (int)Math.Round(normalizedFaceCenterX * sourceWidth);
        var cropX = Math.Max(0, Math.Min(faceCenterX - cropWidth / 2, sourceWidth - cropWidth));

        return new CropRegionResult
        {
            Region = new CropRectangle(cropX, 0, cropWidth, sourceHeight),
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            Strategy = CropStrategy.SingleFace,
            BasedOnFaceDetection = true
        };
    }
}
