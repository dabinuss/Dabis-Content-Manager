namespace DCM.Core.Services;

using DCM.Core.Models;

/// <summary>
/// Service für die Bewertung von Transkript-Segmenten zur Highlight-Erkennung.
/// </summary>
public interface IHighlightScoringService
{
    /// <summary>
    /// Bewertet eine Liste von Kandidaten-Fenstern und gibt die besten Highlights zurück.
    /// </summary>
    /// <param name="draftId">ID des Quell-Drafts.</param>
    /// <param name="windows">Die zu bewertenden Zeitfenster aus dem Transkript.</param>
    /// <param name="contentContext">Optionaler Kontext (z.B. Titel/Beschreibung).</param>
    /// <param name="cancellationToken">Cancellation-Token.</param>
    /// <returns>Liste der besten Highlight-Kandidaten, sortiert nach Score.</returns>
    Task<IReadOnlyList<ClipCandidate>> ScoreHighlightsAsync(
        Guid draftId,
        IReadOnlyList<CandidateWindow> windows,
        string? contentContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gibt an, ob der Service bereit ist (z.B. LLM geladen).
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Initialisiert den Service falls nötig.
    /// </summary>
    bool TryInitialize();
}
