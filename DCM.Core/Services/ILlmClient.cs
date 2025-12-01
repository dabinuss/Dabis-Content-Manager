namespace DCM.Core.Services;

/// <summary>
/// Abstraktes Interface für LLM-Clients (lokal oder remote).
/// Wird für Titel-/Beschreibungs-/Tag-Generierung verwendet.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Gibt an, ob der Client bereit ist und Anfragen bearbeiten kann.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Führt eine Completion-Anfrage durch und gibt die Antwort als String zurück.
    /// </summary>
    /// <param name="prompt">Der Eingabe-Prompt.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    /// <returns>Die generierte Antwort.</returns>
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}