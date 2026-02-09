namespace DCM.Core.Models;

/// <summary>
/// Ergebnis eines Clip-Render-Jobs.
/// </summary>
public sealed class ClipRenderResult
{
    /// <summary>
    /// Ob das Rendering erfolgreich war.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// ID des Render-Jobs.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Pfad zum gerenderten Video (nur bei Erfolg).
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Fehlermeldung (nur bei Misserfolg).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Dauer des Render-Prozesses.
    /// </summary>
    public TimeSpan RenderDuration { get; init; }

    /// <summary>
    /// Größe der Ausgabedatei in Bytes.
    /// </summary>
    public long OutputFileSize { get; init; }

    /// <summary>
    /// ID des erstellten Draft (wenn automatisch erstellt).
    /// </summary>
    public Guid? CreatedDraftId { get; init; }

    /// <summary>
    /// Informationen zur angewandten Crop-Region.
    /// </summary>
    public CropRegionResult? AppliedCrop { get; init; }

    /// <summary>
    /// Erstellt ein erfolgreiches Ergebnis.
    /// </summary>
    public static ClipRenderResult Ok(
        Guid jobId,
        string outputPath,
        TimeSpan renderDuration,
        long fileSize,
        Guid? createdDraftId = null,
        CropRegionResult? appliedCrop = null) => new()
        {
            Success = true,
            JobId = jobId,
            OutputPath = outputPath,
            RenderDuration = renderDuration,
            OutputFileSize = fileSize,
            CreatedDraftId = createdDraftId,
            AppliedCrop = appliedCrop
        };

    /// <summary>
    /// Erstellt ein fehlgeschlagenes Ergebnis.
    /// </summary>
    public static ClipRenderResult Fail(Guid jobId, string errorMessage, TimeSpan renderDuration) => new()
    {
        Success = false,
        JobId = jobId,
        ErrorMessage = errorMessage,
        RenderDuration = renderDuration
    };
}
