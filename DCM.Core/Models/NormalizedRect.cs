namespace DCM.Core.Models;

/// <summary>
/// Rechteck im normierten Koordinatensystem (0..1).
/// </summary>
public sealed class NormalizedRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;

    public NormalizedRect Clone()
    {
        return new NormalizedRect
        {
            X = X,
            Y = Y,
            Width = Width,
            Height = Height
        };
    }

    public NormalizedRect ClampToCanvas(double minSize = 0.05, double maxSize = 1.0)
    {
        var clampedMin = Math.Clamp(minSize, 0.01, 1.0);
        var clampedMax = Math.Clamp(maxSize, clampedMin, 1.0);

        Width = Math.Clamp(Width, clampedMin, clampedMax);
        Height = Math.Clamp(Height, clampedMin, clampedMax);
        X = Math.Clamp(X, 0.0, 1.0 - Width);
        Y = Math.Clamp(Y, 0.0, 1.0 - Height);
        return this;
    }

    public static NormalizedRect FullFrame() => new() { X = 0, Y = 0, Width = 1, Height = 1 };
}
