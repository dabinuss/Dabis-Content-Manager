namespace DCM.Core.Services;

/// <summary>
/// Legacy-Interface für LLM-Dienste.
/// Wird durch ILlmClient ersetzt, bleibt aber für Abwärtskompatibilität.
/// </summary>
public interface ILlmService
{
    bool IsAvailable { get; }

    Task<string> AnalyzeSentimentAsync(string text, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ExtractTopicsAsync(string text, CancellationToken cancellationToken = default);

    Task<string> SummarizeAsync(string text, CancellationToken cancellationToken = default);
}