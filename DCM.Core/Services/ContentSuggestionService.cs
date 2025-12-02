using System.IO;
using System.Text;
using DCM.Core.Configuration;
using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Content-Suggestion-Service mit LLM-Unterstützung.
/// Nutzt ILlmClient für lokale LLM-Inferenz und fällt auf IFallbackSuggestionService zurück.
/// </summary>
public sealed class ContentSuggestionService : IContentSuggestionService
{
    private readonly ILlmClient _llmClient;
    private readonly IFallbackSuggestionService _fallbackSuggestionService;
    private readonly LlmSettings _settings;

    /// <summary>
    /// Maximale Zeichenanzahl für Transkript im Beschreibungs-Prompt.
    /// Basiert auf ContextSize 2048 Tokens (~6000-8000 Zeichen) minus Prompt-Overhead.
    /// </summary>
    private const int MaxTranscriptCharsForDescription = 6000;

    /// <summary>
    /// Maximale Zeichenanzahl für Transkript im Tags-Prompt.
    /// Kürzer, da Tags weniger Kontext benötigen.
    /// </summary>
    private const int MaxTranscriptCharsForTags = 3000;

    /// <summary>
    /// Maximale Zeichenanzahl für Transkript im Titel-Prompt.
    /// </summary>
    private const int MaxTranscriptCharsForTitles = 2000;

    public ContentSuggestionService(
        ILlmClient llmClient,
        IFallbackSuggestionService fallbackSuggestionService,
        LlmSettings settings)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _fallbackSuggestionService = fallbackSuggestionService ?? throw new ArgumentNullException(nameof(fallbackSuggestionService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    #region SuggestTitlesAsync

    public async Task<IReadOnlyList<string>> SuggestTitlesAsync(UploadProject project, ChannelPersona persona)
    {
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

            var titles = ParseTitleResponse(response);

            if (titles.Count == 0)
            {
                return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona);
            }

            return titles;
        }
        catch
        {
            return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona);
        }
    }

    private static string BuildTitlePrompt(UploadProject project, ChannelPersona persona)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Generiere 3 kurze, prägnante YouTube-Videotitel für folgendes Video.");
        sb.AppendLine("Jeder Titel sollte in einer eigenen Zeile stehen.");
        sb.AppendLine("Keine Nummerierung, keine Aufzählungszeichen.");
        sb.AppendLine();

        AppendFileNameInfo(sb, project);
        AppendPersonaInfo(sb, persona);

        // Transkript-Auszug für Titel (kürzerer Auszug reicht)
        if (!string.IsNullOrWhiteSpace(project.TranscriptText))
        {
            var transcript = SmartTruncateTranscript(project.TranscriptText, MaxTranscriptCharsForTitles);
            sb.AppendLine();
            sb.AppendLine("Transkript-Auszug:");
            sb.AppendLine(transcript);
        }

        sb.AppendLine();
        sb.AppendLine("Titel:");

        return sb.ToString();
    }

    private static List<string> ParseTitleResponse(string response)
    {
        return response
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith('['))
            .Where(line => !line.StartsWith('#'))
            .Select(CleanTitleLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct()
            .Take(5)
            .ToList();
    }

    #endregion

    #region SuggestDescriptionAsync

    public async Task<string?> SuggestDescriptionAsync(UploadProject project, ChannelPersona persona)
    {
        if (!IsLocalMode() || !_llmClient.IsReady)
        {
            return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona);
        }

        try
        {
            var prompt = BuildDescriptionPrompt(project, persona);
            var response = await _llmClient.CompleteAsync(prompt);

            if (string.IsNullOrWhiteSpace(response) || response.StartsWith("["))
            {
                return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona);
            }

            var cleaned = CleanDescriptionResponse(response);

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona);
            }

            return cleaned;
        }
        catch
        {
            return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona);
        }
    }

    private static string BuildDescriptionPrompt(UploadProject project, ChannelPersona persona)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Schreibe eine YouTube-Videobeschreibung als Fließtext.");
        sb.AppendLine("Die Beschreibung soll informativ und ansprechend sein.");
        sb.AppendLine("Keine Aufzählungen, keine Markdown-Formatierung.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(project.Title))
        {
            sb.AppendLine($"Videotitel: {project.Title}");
        }

        AppendFileNameInfo(sb, project);
        AppendPersonaInfoForDescription(sb, persona);

        // Volles Transkript mit Smart-Truncation
        if (!string.IsNullOrWhiteSpace(project.TranscriptText))
        {
            var transcript = SmartTruncateTranscript(project.TranscriptText, MaxTranscriptCharsForDescription);
            sb.AppendLine();
            sb.AppendLine("Transkript des Videos:");
            sb.AppendLine(transcript);
        }

        sb.AppendLine();
        sb.AppendLine("Beschreibung:");

        return sb.ToString();
    }

    private static void AppendPersonaInfoForDescription(StringBuilder sb, ChannelPersona persona)
    {
        if (!string.IsNullOrWhiteSpace(persona.ChannelName))
        {
            sb.AppendLine($"Kanal: {persona.ChannelName}");
        }

        if (!string.IsNullOrWhiteSpace(persona.ContentType))
        {
            sb.AppendLine($"Content-Typ: {persona.ContentType}");
        }

        if (!string.IsNullOrWhiteSpace(persona.ToneOfVoice))
        {
            sb.AppendLine($"Schreibstil/Tonfall: {persona.ToneOfVoice}");
        }

        if (!string.IsNullOrWhiteSpace(persona.TargetAudience))
        {
            sb.AppendLine($"Zielgruppe: {persona.TargetAudience}");
        }

        if (!string.IsNullOrWhiteSpace(persona.Language))
        {
            sb.AppendLine($"Sprache: {persona.Language}");
        }

        if (!string.IsNullOrWhiteSpace(persona.AdditionalInstructions))
        {
            sb.AppendLine($"Zusätzliche Anweisungen: {persona.AdditionalInstructions}");
        }
    }

    private static string CleanDescriptionResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return string.Empty;
        }

        var lines = response
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith('['))
            .Where(l => !l.StartsWith("Beschreibung:"))
            .ToList();

        return string.Join("\n", lines).Trim();
    }

    #endregion

    #region SuggestTagsAsync

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(UploadProject project, ChannelPersona persona)
    {
        if (!IsLocalMode() || !_llmClient.IsReady)
        {
            return await _fallbackSuggestionService.SuggestTagsAsync(project, persona);
        }

        try
        {
            var prompt = BuildTagsPrompt(project, persona);
            var response = await _llmClient.CompleteAsync(prompt);

            if (string.IsNullOrWhiteSpace(response) || response.StartsWith("["))
            {
                return await _fallbackSuggestionService.SuggestTagsAsync(project, persona);
            }

            var tags = ParseTagsResponse(response);

            if (tags.Count == 0)
            {
                return await _fallbackSuggestionService.SuggestTagsAsync(project, persona);
            }

            return tags;
        }
        catch
        {
            return await _fallbackSuggestionService.SuggestTagsAsync(project, persona);
        }
    }

    private static string BuildTagsPrompt(UploadProject project, ChannelPersona persona)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Generiere 10-15 relevante YouTube-Tags für folgendes Video.");
        sb.AppendLine("Die Tags sollten kommasepariert in einer Zeile stehen.");
        sb.AppendLine("Keine Hashtags, keine Nummerierung.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(project.Title))
        {
            sb.AppendLine($"Videotitel: {project.Title}");
        }

        AppendFileNameInfo(sb, project);
        AppendPersonaInfo(sb, persona);

        // Transkript-Zusammenfassung mit Smart-Truncation
        if (!string.IsNullOrWhiteSpace(project.TranscriptText))
        {
            var transcript = SmartTruncateTranscript(project.TranscriptText, MaxTranscriptCharsForTags);
            sb.AppendLine();
            sb.AppendLine("Transkript-Auszug:");
            sb.AppendLine(transcript);
        }

        sb.AppendLine();
        sb.AppendLine("Tags:");

        return sb.ToString();
    }

    private static List<string> ParseTagsResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return new List<string>();
        }

        var separators = new[] { ',', '\n', '\r' };

        var tags = response
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanTag)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Where(t => !t.StartsWith('['))
            .Where(t => !t.StartsWith("Tags:"))
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return tags;
    }

    private static string CleanTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var cleaned = tag.Trim();

        // Nummerierung entfernen
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

        // Hashtags entfernen
        if (cleaned.StartsWith('#'))
        {
            cleaned = cleaned[1..];
        }

        cleaned = RemoveQuotes(cleaned);
        cleaned = cleaned.TrimStart('*', '-').TrimStart();

        return cleaned.Trim();
    }

    #endregion

    #region Transcript Truncation

    /// <summary>
    /// Kürzt ein Transkript intelligent, wenn es das Limit überschreitet.
    /// Strategie: Anfang (40%) + Mitte (20%) + Ende (40%)
    /// So bleiben Intro, Hauptteil und Outro erhalten.
    /// </summary>
    private static string SmartTruncateTranscript(string transcript, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var trimmed = transcript.Trim();

        // Passt komplett? Dann direkt zurückgeben.
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        // Berechnung der Abschnitte
        var beginLength = (int)(maxLength * 0.40);
        var middleLength = (int)(maxLength * 0.20);
        var endLength = (int)(maxLength * 0.40);

        // Sicherheitscheck: Mindestlängen
        if (beginLength < 100) beginLength = 100;
        if (middleLength < 50) middleLength = 50;
        if (endLength < 100) endLength = 100;

        // Positionen berechnen
        var middleStart = (trimmed.Length - middleLength) / 2;

        // Abschnitte extrahieren
        var beginPart = ExtractAtWordBoundary(trimmed, 0, beginLength);
        var middlePart = ExtractAtWordBoundary(trimmed, middleStart, middleLength);
        var endPart = ExtractAtWordBoundary(trimmed, trimmed.Length - endLength, endLength);

        // Zusammenbauen mit Markierungen
        var sb = new StringBuilder();
        sb.Append(beginPart);
        sb.Append("\n\n[...]\n\n");
        sb.Append(middlePart);
        sb.Append("\n\n[...]\n\n");
        sb.Append(endPart);

        return sb.ToString();
    }

    /// <summary>
    /// Extrahiert einen Textabschnitt und versucht, an Wortgrenzen zu schneiden.
    /// </summary>
    private static string ExtractAtWordBoundary(string text, int startIndex, int length)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Startindex begrenzen
        if (startIndex < 0) startIndex = 0;
        if (startIndex >= text.Length) return string.Empty;

        // Endindex berechnen und begrenzen
        var endIndex = startIndex + length;
        if (endIndex > text.Length) endIndex = text.Length;

        // Rohextrakt
        var extract = text[startIndex..endIndex];

        // Am Anfang: Zum nächsten Wortanfang springen (falls mitten im Wort)
        if (startIndex > 0 && extract.Length > 0 && !char.IsWhiteSpace(extract[0]))
        {
            var firstSpace = extract.IndexOf(' ');
            if (firstSpace > 0 && firstSpace < extract.Length / 4)
            {
                extract = extract[(firstSpace + 1)..];
            }
        }

        // Am Ende: Zum letzten Wortende zurückgehen
        if (endIndex < text.Length && extract.Length > 0 && !char.IsWhiteSpace(extract[^1]))
        {
            var lastSpace = extract.LastIndexOf(' ');
            if (lastSpace > extract.Length * 3 / 4)
            {
                extract = extract[..lastSpace];
            }
        }

        return extract.Trim();
    }

    #endregion

    #region Shared Helper Methods

    private bool IsLocalMode()
    {
        return string.Equals(_settings.Mode, "Local", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendFileNameInfo(StringBuilder sb, UploadProject project)
    {
        if (string.IsNullOrWhiteSpace(project.VideoFilePath))
        {
            return;
        }

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(project.VideoFilePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                sb.AppendLine($"Dateiname: {fileName}");
            }
        }
        catch
        {
            // Ignorieren
        }
    }

    private static void AppendPersonaInfo(StringBuilder sb, ChannelPersona persona)
    {
        if (!string.IsNullOrWhiteSpace(persona.ChannelName))
        {
            sb.AppendLine($"Kanal: {persona.ChannelName}");
        }

        if (!string.IsNullOrWhiteSpace(persona.ContentType))
        {
            sb.AppendLine($"Content-Typ: {persona.ContentType}");
        }

        if (!string.IsNullOrWhiteSpace(persona.ToneOfVoice))
        {
            sb.AppendLine($"Tonfall: {persona.ToneOfVoice}");
        }

        if (!string.IsNullOrWhiteSpace(persona.Language))
        {
            sb.AppendLine($"Sprache: {persona.Language}");
        }
    }

    private static string CleanTitleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var cleaned = line.Trim();

        // Nummerierung entfernen
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

        cleaned = RemoveQuotes(cleaned);
        cleaned = cleaned.TrimStart('*', '-').TrimStart();

        if (cleaned.StartsWith('\u2022'))
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

        var quoteChars = new[] { '"', '\'', '\u201E', '\u201C', '\u201A', '\u2018', '\u201D', '\u2019' };

        var result = text;

        while (result.Length > 0 && quoteChars.Contains(result[0]))
        {
            result = result[1..];
        }

        while (result.Length > 0 && quoteChars.Contains(result[^1]))
        {
            result = result[..^1];
        }

        return result;
    }

    #endregion
}