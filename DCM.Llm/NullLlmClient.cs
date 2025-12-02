using DCM.Core.Services;

namespace DCM.Llm;

/// <summary>
/// Null-Implementation des ILlmClient für den Fall, dass kein LLM konfiguriert ist.
/// </summary>
public sealed class NullLlmClient : ILlmClient
{
    public bool IsReady => false;

    public bool TryInitialize() => false;

    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("[LLM nicht konfiguriert]");
    }
}