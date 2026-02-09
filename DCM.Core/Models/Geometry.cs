namespace DCM.Core.Models;

/// <summary>
/// Repräsentiert eine Größe in Pixeln.
/// </summary>
public readonly record struct PixelSize(int Width, int Height);

/// <summary>
/// 2D-Punkt (float) in Pixelkoordinaten.
/// </summary>
public readonly record struct FacePoint(float X, float Y);

/// <summary>
/// Rechteck (float) in Pixelkoordinaten.
/// </summary>
public readonly record struct FaceRect(float X, float Y, float Width, float Height)
{
    public float CenterX => X + (Width / 2f);
    public float CenterY => Y + (Height / 2f);
    public float Area => Width * Height;
}

/// <summary>
/// Rechteck (int) für Crop-Berechnungen.
/// </summary>
public readonly record struct CropRectangle(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public double CenterX => X + (Width / 2.0);
    public double CenterY => Y + (Height / 2.0);
}
