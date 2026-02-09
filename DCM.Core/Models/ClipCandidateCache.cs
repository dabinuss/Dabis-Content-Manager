namespace DCM.Core.Models;

/// <summary>
/// Cache für Clip-Kandidaten eines Drafts.
/// Ermöglicht Wiederverwendung ohne erneutes LLM-Scoring.
/// </summary>
public sealed class ClipCandidateCache
{
    /// <summary>
    /// ID des zugehörigen Drafts.
    /// </summary>
    public Guid DraftId { get; set; }

    /// <summary>
    /// Zeitpunkt der Generierung.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Hash des Transkripts zur Invalidierung bei Änderungen.
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// Die gecachten Kandidaten.
    /// </summary>
    public List<ClipCandidate> Candidates { get; set; } = new();

    /// <summary>
    /// Prüft, ob der Cache für das gegebene Transkript noch gültig ist.
    /// </summary>
    public bool IsValidFor(string? transcriptHash)
    {
        if (string.IsNullOrEmpty(ContentHash) || string.IsNullOrEmpty(transcriptHash))
        {
            return false;
        }

        return string.Equals(ContentHash, transcriptHash, StringComparison.Ordinal);
    }
}
