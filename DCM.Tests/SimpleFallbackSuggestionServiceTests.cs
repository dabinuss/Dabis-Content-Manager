using DCM.Core.Models;
using DCM.Core.Services;
using Xunit;

namespace DCM.Tests;

public class SimpleFallbackSuggestionServiceTests
{
    private readonly SimpleFallbackSuggestionService _service;

    public SimpleFallbackSuggestionServiceTests()
    {
        _service = new SimpleFallbackSuggestionService();
    }

    #region SuggestTitlesAsync Tests

    [Fact]
    public async Task SuggestTitlesAsync_WithVideoPath_ReturnsTitleFromFilename()
    {
        var project = new UploadProject
        {
            VideoFilePath = @"C:\Videos\my_awesome_video.mp4"
        };
        var persona = new ChannelPersona();

        var result = await _service.SuggestTitlesAsync(project, persona);

        Assert.NotEmpty(result);
        Assert.Contains(result, t => t.Contains("my") || t.Contains("awesome") || t.Contains("video"));
    }

    [Fact]
    public async Task SuggestTitlesAsync_WithGamingPersona_AddsEmoji()
    {
        var project = new UploadProject
        {
            VideoFilePath = @"C:\Videos\test_video.mp4"
        };
        var persona = new ChannelPersona
        {
            ContentType = "Gaming Highlights"
        };

        var result = await _service.SuggestTitlesAsync(project, persona);

        Assert.NotEmpty(result);
        Assert.Contains(result, t => t.Contains("🎮"));
    }

    [Fact]
    public async Task SuggestTitlesAsync_WithChannelName_IncludesChannelName()
    {
        var project = new UploadProject
        {
            VideoFilePath = @"C:\Videos\test.mp4"
        };
        var persona = new ChannelPersona
        {
            ChannelName = "MyChannel"
        };

        var result = await _service.SuggestTitlesAsync(project, persona);

        Assert.NotEmpty(result);
        Assert.Contains(result, t => t.Contains("MyChannel"));
    }

    [Fact]
    public async Task SuggestTitlesAsync_EmptyVideoPath_ReturnsFallbackMessage()
    {
        var project = new UploadProject
        {
            VideoFilePath = ""
        };
        var persona = new ChannelPersona();

        var result = await _service.SuggestTitlesAsync(project, persona);

        Assert.NotEmpty(result);
        Assert.Contains(result, t => t.Contains("Kein Titel") || t.Contains("ableitbar"));
    }

    [Fact]
    public async Task SuggestTitlesAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var project = new UploadProject();
        var persona = new ChannelPersona();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.SuggestTitlesAsync(project, persona, cts.Token));
    }

    #endregion

    #region SuggestDescriptionAsync Tests

    [Fact]
    public async Task SuggestDescriptionAsync_WithTitle_IncludesTitleInDescription()
    {
        var project = new UploadProject
        {
            Title = "Mein Video Titel"
        };
        var persona = new ChannelPersona();

        var result = await _service.SuggestDescriptionAsync(project, persona);

        Assert.NotEmpty(result);
        Assert.Contains(result, d => d.Contains("Mein Video Titel"));
    }

    [Fact]
    public async Task SuggestDescriptionAsync_WithChannelName_ReturnsResult()
    {
        var project = new UploadProject();
        var persona = new ChannelPersona
        {
            ChannelName = "TestChannel"
        };

        var result = await _service.SuggestDescriptionAsync(project, persona);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task SuggestDescriptionAsync_WithTitle_IncludesTitle()
    {
        var project = new UploadProject { Title = "Mein Test Video" };
        var persona = new ChannelPersona();

        var result = await _service.SuggestDescriptionAsync(project, persona);

        Assert.NotEmpty(result);
        Assert.Contains(result, d => d.Contains("Mein Test Video"));
    }

    [Fact]
    public async Task SuggestDescriptionAsync_ReturnsNonNullResult()
    {
        var project = new UploadProject();
        var persona = new ChannelPersona();

        var result = await _service.SuggestDescriptionAsync(project, persona);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task SuggestDescriptionAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var project = new UploadProject();
        var persona = new ChannelPersona();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.SuggestDescriptionAsync(project, persona, cts.Token));
    }

    #endregion

    #region SuggestTagsAsync Tests

    [Fact]
    public async Task SuggestTagsAsync_WithContentType_ExtractsTagsFromContentType()
    {
        var project = new UploadProject();
        var persona = new ChannelPersona
        {
            ContentType = "Gaming Highlights"
        };

        var result = await _service.SuggestTagsAsync(project, persona);

        Assert.NotEmpty(result);
        Assert.Contains(result, t => 
            t.Equals("Gaming", StringComparison.OrdinalIgnoreCase) || 
            t.Equals("Highlights", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuggestTagsAsync_WithGermanLanguage_AddsGermanTags()
    {
        var project = new UploadProject();
        var persona = new ChannelPersona
        {
            Language = "de-DE"
        };

        var result = await _service.SuggestTagsAsync(project, persona);

        Assert.NotEmpty(result);
        Assert.Contains(result, t => 
            t.Equals("deutsch", StringComparison.OrdinalIgnoreCase) || 
            t.Equals("german", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuggestTagsAsync_WithVideoPath_ExtractsTagsFromFilename()
    {
        var project = new UploadProject
        {
            VideoFilePath = @"C:\Videos\minecraft_tutorial_part1.mp4"
        };
        var persona = new ChannelPersona();

        var result = await _service.SuggestTagsAsync(project, persona);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task SuggestTagsAsync_EmptyInputs_ReturnsFallbackMessage()
    {
        var project = new UploadProject();
        var persona = new ChannelPersona();

        var result = await _service.SuggestTagsAsync(project, persona);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task SuggestTagsAsync_NoDuplicates()
    {
        var project = new UploadProject
        {
            VideoFilePath = @"C:\test_test_test.mp4"
        };
        var persona = new ChannelPersona
        {
            ChannelName = "test"
        };

        var result = await _service.SuggestTagsAsync(project, persona);

        var distinctCount = result.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(result.Count, distinctCount);
    }

    [Fact]
    public async Task SuggestTagsAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var project = new UploadProject();
        var persona = new ChannelPersona();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.SuggestTagsAsync(project, persona, cts.Token));
    }

    #endregion
}
