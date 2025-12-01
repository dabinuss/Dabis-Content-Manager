using DCM.Core.Configuration;

namespace DCM.Core.Services;

public sealed class DummyLlmService : ILlmService
{
    private readonly LlmSettings _settings;

    public DummyLlmService(LlmSettings? settings = null)
    {
        _settings = settings ?? new LlmSettings();
    }

    public bool IsAvailable => false;

    public Task<string> AnalyzeSentimentAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("[LLM nicht konfiguriert]");
    }

    public Task<IReadOnlyList<string>> ExtractTopicsAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    public Task<string> SummarizeAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("[LLM nicht konfiguriert]");
    }
}