using System.IO;
using DCM.Core.Configuration;
using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Content-Suggestion-Service mit LLM-Unterstützung für den Titel-Flow.
/// Nutzt ILlmClient für lokale LLM-Inferenz und fällt auf IFallbackSuggestionService zurück.
/// </summary>
public sealed class ContentSuggestionService : IContentSuggestionService
{
    private readonly ILlmClient _llmClient;
    private readonly IFallbackSuggestionService _fallbackSuggestionService;
    private readonly LlmSettings _settings;

    public ContentSuggestionService(
        ILlmClient llmClient,
        IFallbackSuggestionService fallbackSuggestionService,
        LlmSettings settings)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _fallbackSuggestionService = fallbackSuggestionService ?? throw new ArgumentNullException(nameof(fallbackSuggestionService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<IReadOnlyList<string>> SuggestTitlesAsync(UploadProject project, ChannelPersona persona)
    {
        // Nur bei Mode == "Local" das LLM verwenden
        if (!IsLocalMode() || !_llmClient.IsReady)
        {
            return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona);
        }

        try
        {
            var prompt = BuildTitlePrompt(project, persona);
            var response = await _llmClient.CompleteAsync(prompt);

            if (string.IsNullOrWhiteSpace(response))
            {
                return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona);
            }

            // Response in Zeilen splitten
            var titles = response
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !line.StartsWith('[')) // Fehlermeldungen filtern
                .Where(line => !line.StartsWith('#')) // Markdown-Header filtern
                .Select(line => CleanTitleLine(line))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct()
                .Take(5)
                .ToList();

            if (titles.Count == 0)
            {
                return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona);
            }

            return titles;
        }
        catch
        {
            // Bei Fehlern auf Fallback zurückgreifen
            return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona);
        }
    }

    public async Task<string?> SuggestDescriptionAsync(UploadProject project, ChannelPersona persona)
    {
        // Beschreibung nutzt vorerst nur den Fallback
        // (kann später erweitert werden)
        return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona);
    }

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(UploadProject project, ChannelPersona persona)
    {
        // Tags nutzen vorerst nur den Fallback
        // (kann später erweitert werden)
        return await _fallbackSuggestionService.SuggestTagsAsync(project, persona);
    }

    private bool IsLocalMode()
    {
        return string.Equals(_settings.Mode, "Local", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTitlePrompt(UploadProject project, ChannelPersona persona)
    {
        var parts = new List<string>
        {
            "Generiere 3 kurze, prägnante YouTube-Videotitel für folgendes Video.",
            "Jeder Titel sollte in einer eigenen Zeile stehen.",
            "Keine Nummerierung, keine Aufzählungszeichen.",
            ""
        };

        // Dateiname
        if (!string.IsNullOrWhiteSpace(project.VideoFilePath))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(project.VideoFilePath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    parts.Add($"Dateiname: {fileName}");
                }
            }
            catch
            {
                // Ignorieren
            }
        }

        // Persona-Infos
        if (!string.IsNullOrWhiteSpace(persona.ChannelName))
        {
            parts.Add($"Kanal: {persona.ChannelName}");
        }

        if (!string.IsNullOrWhiteSpace(persona.ContentType))
        {
            parts.Add($"Content-Typ: {persona.ContentType}");
        }

        if (!string.IsNullOrWhiteSpace(persona.ToneOfVoice))
        {
            parts.Add($"Tonfall: {persona.ToneOfVoice}");
        }

        if (!string.IsNullOrWhiteSpace(persona.Language))
        {
            parts.Add($"Sprache: {persona.Language}");
        }

        // Transkript-Kurzfassung (max. 300 Zeichen)
        if (!string.IsNullOrWhiteSpace(project.TranscriptText))
        {
            var transcript = project.TranscriptText.Trim();
            if (transcript.Length > 300)
            {
                transcript = transcript[..300] + "...";
            }
            parts.Add($"Transkript-Auszug: {transcript}");
        }

        parts.Add("");
        parts.Add("Titel:");

        return string.Join("\n", parts);
    }

    private static string CleanTitleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var cleaned = line.Trim();

        // Nummerierung entfernen (z.B. "1.", "1)", "1:")
        if (cleaned.Length > 2 && char.IsDigit(cleaned[0]))
        {
            var idx = 0;
            while (idx < cleaned.Length && char.IsDigit(cleaned[idx]))
            {
                idx++;
            }

            if (idx < cleaned.Length && (cleaned[idx] == '.' || cleaned[idx] == ')' || cleaned[idx] == ':'))
            {
                cleaned = cleaned[(idx + 1)..].TrimStart();
            }
        }

        // Anführungszeichen entfernen (normale und typographische)
        cleaned = RemoveQuotes(cleaned);

        // Markdown-Formatierung entfernen
        cleaned = cleaned.TrimStart('*', '-').TrimStart();

        // Bullet-Point separat entfernen (Unicode-Zeichen)
        if (cleaned.StartsWith('\u2022')) // •
        {
            cleaned = cleaned[1..].TrimStart();
        }

        return cleaned;
    }

    private static string RemoveQuotes(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Liste der zu entfernenden Anführungszeichen (als Strings)
        var quoteChars = new[] { '"', '\'', '\u201E', '\u201C', '\u201A', '\u2018', '\u201D', '\u2019' };
        // " ' „ " ‚ ' " '

        var result = text;

        // Führende Quotes entfernen
        while (result.Length > 0 && quoteChars.Contains(result[0]))
        {
            result = result[1..];
        }

        // Nachfolgende Quotes entfernen
        while (result.Length > 0 && quoteChars.Contains(result[^1]))
        {
            result = result[..^1];
        }

        return result;
    }
}