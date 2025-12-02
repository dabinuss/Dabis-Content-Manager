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
    /// Versucht den Client zu initialisieren. Gibt true bei Erfolg zurück.
    /// Bei bereits initialisierten Clients gibt dies ebenfalls true zurück.
    /// </summary>
    bool TryInitialize();

    /// <summary>
    /// Führt eine Completion-Anfrage durch und gibt die Antwort als String zurück.
    /// </summary>
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}