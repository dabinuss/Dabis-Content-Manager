namespace DCM.Core.Models;

/// <summary>
/// Fortschritt für Batch-Rendering mehrerer Clips.
/// </summary>
public sealed class ClipBatchRenderProgress
{
    /// <summary>
    /// Index des aktuellen Jobs (0-basiert).
    /// </summary>
    public int CurrentJobIndex { get; init; }

    /// <summary>
    /// Gesamtanzahl der Jobs.
    /// </summary>
    public int TotalJobs { get; init; }

    /// <summary>
    /// Fortschritt des aktuellen Jobs.
    /// </summary>
    public required ClipRenderProgress CurrentJobProgress { get; init; }

    /// <summary>
    /// Gesamtfortschritt in Prozent (0-100).
    /// </summary>
    public double OverallPercent => TotalJobs > 0
        ? (CurrentJobIndex * 100.0 + CurrentJobProgress.TotalProgress) / TotalJobs
        : 0;
}
