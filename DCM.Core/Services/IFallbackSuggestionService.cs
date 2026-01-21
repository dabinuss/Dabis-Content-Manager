using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Fallback-Service für Content-Vorschläge, wenn das LLM nicht verfügbar ist.
/// </summary>
public interface IFallbackSuggestionService
{
    Task<IReadOnlyList<string>> SuggestTitlesAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SuggestDescriptionAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SuggestTagsAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChapterTopic>> SuggestChaptersAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default);
}
