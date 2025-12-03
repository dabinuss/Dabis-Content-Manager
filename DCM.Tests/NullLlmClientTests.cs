using DCM.Llm;
using Xunit;

namespace DCM.Tests;

public class NullLlmClientTests
{
    [Fact]
    public void IsReady_ReturnsFalse()
    {
        var client = new NullLlmClient();
        Assert.False(client.IsReady);
    }

    [Fact]
    public void TryInitialize_ReturnsFalse()
    {
        var client = new NullLlmClient();
        var result = client.TryInitialize();
        Assert.False(result);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsNotConfiguredMessage()
    {
        var client = new NullLlmClient();

        var result = await client.CompleteAsync("Test prompt");

        Assert.Equal("[LLM nicht konfiguriert]", result);
    }

    [Fact]
    public async Task CompleteAsync_WithCancellationToken_ReturnsNotConfiguredMessage()
    {
        var client = new NullLlmClient();
        var cts = new CancellationTokenSource();

        var result = await client.CompleteAsync("Test prompt", cts.Token);

        Assert.Equal("[LLM nicht konfiguriert]", result);
    }
}