namespace DCM.Core.Models;

/// <summary>
/// Fortschrittsinformationen für einen Render-Job.
/// </summary>
public sealed class ClipRenderProgress
{
    /// <summary>
    /// ID des zugehörigen Render-Jobs.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Aktuelle Phase des Render-Prozesses.
    /// </summary>
    public ClipRenderPhase Phase { get; init; }

    /// <summary>
    /// Fortschritt in Prozent (0-100) für die aktuelle Phase.
    /// </summary>
    public double PhaseProgress { get; init; }

    /// <summary>
    /// Gesamtfortschritt in Prozent (0-100).
    /// </summary>
    public double TotalProgress { get; init; }

    /// <summary>
    /// Beschreibung des aktuellen Schritts.
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Geschätzte verbleibende Zeit.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    /// <summary>
    /// Aktuell verarbeiteter Frame (bei VideoRendering).
    /// </summary>
    public int? CurrentFrame { get; init; }

    /// <summary>
    /// Gesamtanzahl der Frames (bei VideoRendering).
    /// </summary>
    public int? TotalFrames { get; init; }

    /// <summary>
    /// Erstellt einen Progress für die Pending-Phase.
    /// </summary>
    public static ClipRenderProgress Pending(Guid jobId) => new()
    {
        JobId = jobId,
        Phase = ClipRenderPhase.Pending,
        PhaseProgress = 0,
        TotalProgress = 0,
        StatusMessage = "Warte auf Start..."
    };

    /// <summary>
    /// Erstellt einen Progress für die Completed-Phase.
    /// </summary>
    public static ClipRenderProgress Completed(Guid jobId) => new()
    {
        JobId = jobId,
        Phase = ClipRenderPhase.Completed,
        PhaseProgress = 100,
        TotalProgress = 100,
        StatusMessage = "Abgeschlossen"
    };

    /// <summary>
    /// Erstellt einen Progress für einen Fehler.
    /// </summary>
    public static ClipRenderProgress Failed(Guid jobId, string errorMessage) => new()
    {
        JobId = jobId,
        Phase = ClipRenderPhase.Failed,
        PhaseProgress = 0,
        TotalProgress = 0,
        StatusMessage = errorMessage
    };
}
