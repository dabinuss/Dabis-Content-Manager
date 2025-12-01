namespace DCM.Core.Services;

public interface ILlmService
{
    bool IsAvailable { get; }
    
    Task<string> AnalyzeSentimentAsync(string text, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<string>> ExtractTopicsAsync(string text, CancellationToken cancellationToken = default);
    
    Task<string> SummarizeAsync(string text, CancellationToken cancellationToken = default);
}