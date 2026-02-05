using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Service für das Rendern von Video-Clips mit FFmpeg.
/// </summary>
public interface IClipRenderService
{
    /// <summary>
    /// Gibt an, ob der Service bereit ist (FFmpeg verfügbar).
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Versucht den Service zu initialisieren.
    /// </summary>
    /// <returns>True wenn erfolgreich, sonst false.</returns>
    bool TryInitialize();

    /// <summary>
    /// Rendert einen einzelnen Clip.
    /// </summary>
    /// <param name="job">Der Render-Job mit allen Parametern.</param>
    /// <param name="progress">Optionaler Fortschritts-Callback.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    /// <returns>Das Render-Ergebnis.</returns>
    Task<ClipRenderResult> RenderClipAsync(
        ClipRenderJob job,
        IProgress<ClipRenderProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rendert mehrere Clips nacheinander.
    /// </summary>
    /// <param name="jobs">Die Render-Jobs.</param>
    /// <param name="progress">Optionaler Fortschritts-Callback.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    /// <returns>Die Render-Ergebnisse für jeden Job.</returns>
    Task<IReadOnlyList<ClipRenderResult>> RenderClipsAsync(
        IReadOnlyList<ClipRenderJob> jobs,
        IProgress<ClipBatchRenderProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Erstellt einen Render-Job aus einem ClipCandidate.
    /// </summary>
    /// <param name="candidate">Der Clip-Kandidat.</param>
    /// <param name="sourceVideoPath">Pfad zum Quell-Video.</param>
    /// <param name="outputFolder">Zielordner für das gerenderte Video.</param>
    /// <param name="convertToPortrait">Ob in 9:16 Portrait konvertiert werden soll.</param>
    /// <returns>Der erstellte Render-Job.</returns>
    ClipRenderJob CreateJobFromCandidate(
        ClipCandidate candidate,
        string sourceVideoPath,
        string outputFolder,
        bool convertToPortrait = false);
}
