namespace DCM.Core.Services;

/// <summary>
/// Standardwerte für Face Detection.
/// </summary>
public static class FaceDetectionDefaults
{
    public const double SampleIntervalSeconds = 2.0;
    public const int MaxSamples = 30;
    public const float MinConfidence = 0.7f;
    public const int ClusterDistancePixels = 100;
}
