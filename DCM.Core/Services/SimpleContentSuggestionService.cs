using System.IO;
using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Einfache Implementierung des IContentSuggestionService ohne LLM.
/// Delegiert an ILlmService für Abwärtskompatibilität.
/// </summary>
public sealed class SimpleContentSuggestionService : IContentSuggestionService
{
    private readonly ILlmService _llmService;

    public SimpleContentSuggestionService(ILlmService llmService)
    {
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
    }

    public async Task<IReadOnlyList<string>> SuggestTitlesAsync(UploadProject project, ChannelPersona persona)
    {
        if (!_llmService.IsAvailable)
        {
            return new[] { "[LLM nicht konfiguriert]" };
        }

        var context = BuildContext(project, persona);
        var prompt = $"Generiere 3 YouTube-Titel für folgendes Video:\n{context}";
        var result = await _llmService.SummarizeAsync(prompt);

        if (string.IsNullOrWhiteSpace(result))
        {
            return new[] { "[Keine Antwort vom LLM]" };
        }

        var titles = result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Take(4)
            .ToList();

        return titles.Count > 0 ? titles : new[] { result };
    }

    public async Task<string?> SuggestDescriptionAsync(UploadProject project, ChannelPersona persona)
    {
        if (!_llmService.IsAvailable)
        {
            return "[LLM nicht konfiguriert]";
        }

        var context = BuildContext(project, persona);
        var prompt = $"Schreibe eine YouTube-Beschreibung für folgendes Video:\n{context}";
        var result = await _llmService.SummarizeAsync(prompt);

        return string.IsNullOrWhiteSpace(result) ? "[Keine Antwort vom LLM]" : result;
    }

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(UploadProject project, ChannelPersona persona)
    {
        if (!_llmService.IsAvailable)
        {
            return new[] { "[LLM nicht konfiguriert]" };
        }

        var context = BuildContext(project, persona);
        var prompt = $"Generiere 10-15 relevante YouTube-Tags für folgendes Video:\n{context}";
        var topics = await _llmService.ExtractTopicsAsync(prompt);

        return topics.Count > 0 ? topics : new[] { "[Keine Antwort vom LLM]" };
    }

    private static string BuildContext(UploadProject project, ChannelPersona persona)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(project.Title))
            parts.Add($"Titel: {project.Title}");

        if (!string.IsNullOrWhiteSpace(project.VideoFilePath))
            parts.Add($"Dateiname: {Path.GetFileNameWithoutExtension(project.VideoFilePath)}");

        if (!string.IsNullOrWhiteSpace(project.TranscriptText))
        {
            var snippet = project.TranscriptText.Length > 500
                ? project.TranscriptText[..500] + "..."
                : project.TranscriptText;
            parts.Add($"Transkript: {snippet}");
        }

        if (!string.IsNullOrWhiteSpace(persona.ContentType))
            parts.Add($"Content-Typ: {persona.ContentType}");

        if (!string.IsNullOrWhiteSpace(persona.ToneOfVoice))
            parts.Add($"Tonfall: {persona.ToneOfVoice}");

        if (!string.IsNullOrWhiteSpace(persona.TargetAudience))
            parts.Add($"Zielgruppe: {persona.TargetAudience}");

        if (!string.IsNullOrWhiteSpace(persona.Language))
            parts.Add($"Sprache: {persona.Language}");

        return string.Join("\n", parts);
    }
}