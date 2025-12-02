using System.Text;
using System.Text.RegularExpressions;
using DCM.Core.Configuration;
using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Content-Suggestion-Service mit LLM-Unterstützung.
/// Nutzt ILlmClient für lokale LLM-Inferenz und fällt auf IFallbackSuggestionService zurück.
/// WICHTIG: Ohne Transkript wird IMMER der Fallback verwendet - das LLM darf nicht halluzinieren.
/// </summary>
public sealed partial class ContentSuggestionService : IContentSuggestionService
{
    private readonly ILlmClient _llmClient;
    private readonly IFallbackSuggestionService _fallbackSuggestionService;
    private readonly LlmSettings _settings;
    private readonly Random _random = new();
    private readonly object _initLock = new();

    private const int MaxTranscriptCharsForDescription = 4000;
    private const int MaxTranscriptCharsForTags = 2000;
    private const int MaxTranscriptCharsForTitles = 1500;
    private const int MinimumTranscriptLength = 50;

    private static readonly string[] TitleStyles =
    {
        "kreativ und aufmerksamkeitsstark",
        "informativ und klar",
        "neugierig machend",
        "direkt und prägnant",
        "unterhaltsam und locker",
        "professionell und seriös",
        "spannend und fesselnd",
        "einladend und freundlich"
    };

    private static readonly string[] DescriptionStyles =
    {
        "einladend und neugierig machend",
        "informativ und sachlich",
        "locker und unterhaltsam",
        "professionell und überzeugend",
        "persönlich und nahbar",
        "kurz und knackig",
        "detailliert und umfassend",
        "spannend und fesselnd"
    };

    private static readonly string[] TagFocuses =
    {
        "Fokus auf Hauptthemen",
        "Fokus auf Zielgruppe",
        "Fokus auf Emotionen und Stimmung",
        "Fokus auf Aktionen und Handlungen",
        "Fokus auf Fachbegriffe",
        "Fokus auf allgemeine Suchbegriffe",
        "Fokus auf spezifische Details",
        "Fokus auf verwandte Themen"
    };

    public ContentSuggestionService(
        ILlmClient llmClient,
        IFallbackSuggestionService fallbackSuggestionService,
        LlmSettings settings)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _fallbackSuggestionService = fallbackSuggestionService ?? throw new ArgumentNullException(nameof(fallbackSuggestionService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    private bool EnsureLlmReady()
    {
        if (!_settings.IsLocalMode)
        {
            return false;
        }

        if (_llmClient.IsReady)
        {
            return true;
        }

        lock (_initLock)
        {
            if (_llmClient.IsReady)
            {
                return true;
            }

            return _llmClient.TryInitialize();
        }
    }

    private static bool HasTranscript(UploadProject project)
    {
        if (string.IsNullOrWhiteSpace(project.TranscriptText))
        {
            return false;
        }

        var trimmed = project.TranscriptText.Trim();
        return trimmed.Length >= MinimumTranscriptLength;
    }

    private string GetRandomVariation(string[] options)
    {
        return options[_random.Next(options.Length)];
    }

    private static string GetSessionId()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    #region SuggestTitlesAsync

    public async Task<IReadOnlyList<string>> SuggestTitlesAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        if (!HasTranscript(project))
        {
            System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Titel: KEIN Transkript vorhanden -> Fallback");
            return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
        }

        if (!EnsureLlmReady())
        {
            System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Titel: LLM nicht bereit -> Fallback");
            return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
        }

        try
        {
            var prompt = BuildTitlePrompt(project);
            System.Diagnostics.Debug.WriteLine($"[ContentSuggestion] Titel-Prompt Länge: {prompt.Length}");

            var response = await _llmClient.CompleteAsync(prompt, cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[ContentSuggestion] Titel-Response: '{response}'");

            if (string.IsNullOrWhiteSpace(response) || TextCleaningUtility.IsErrorResponse(response))
            {
                System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Titel: Ungültige Response -> Fallback");
                return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
            }

            var titles = ParseTitleResponse(response);

            if (titles.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Titel: Keine Titel geparsed -> Fallback");
                return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
            }

            return titles;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ContentSuggestion] Titel-Exception: {ex.Message}");
            return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
        }
    }

    private string BuildTitlePrompt(UploadProject project)
    {
        var sb = new StringBuilder();
        var style = GetRandomVariation(TitleStyles);
        var sessionId = GetSessionId();

        sb.AppendLine("<|system|>");
        sb.AppendLine("Du generierst YouTube-Videotitel auf Deutsch.");
        sb.AppendLine("Ausgabe: Genau 3 Titel, jeder in einer eigenen Zeile.");
        sb.AppendLine("Format: Nur die Titel, nichts anderes.");
        sb.AppendLine("Verboten: Nummerierung, Anführungszeichen, Aufzählungszeichen, Erklärungen.");
        sb.AppendLine("<|end|>");

        sb.AppendLine("<|user|>");
        sb.AppendLine($"[Session: {sessionId}]");
        sb.AppendLine($"Stil: {style}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(_settings.TitleCustomPrompt))
        {
            sb.AppendLine($"Beachte: {TextCleaningUtility.SanitizeCustomPrompt(_settings.TitleCustomPrompt)}");
            sb.AppendLine();
        }

        sb.AppendLine("Generiere 3 neue, unterschiedliche Titel basierend auf diesem Transkript:");
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT START---");
        sb.Append(TextCleaningUtility.TruncateTranscript(project.TranscriptText!, MaxTranscriptCharsForTitles));
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT ENDE---");
        sb.AppendLine("<|end|>");
        sb.AppendLine("<|assistant|>");

        return sb.ToString();
    }

    private List<string> ParseTitleResponse(string response)
    {
        var customPromptWords = ExtractFilterWords(_settings.TitleCustomPrompt);

        return response
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !TextCleaningUtility.IsMetaLine(line))
            .Where(line => !ContainsPromptLeakage(line, customPromptWords))
            .Where(line => line.Length > 5 && line.Length < 150)
            .Select(TextCleaningUtility.CleanTitleLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => line.Length > 5)
            .Distinct()
            .Take(5)
            .ToList();
    }

    #endregion

    #region SuggestDescriptionAsync

    public async Task<string?> SuggestDescriptionAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        if (!HasTranscript(project))
        {
            System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Beschreibung: KEIN Transkript vorhanden -> Fallback");
            return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
        }

        if (!EnsureLlmReady())
        {
            System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Beschreibung: LLM nicht bereit -> Fallback");
            return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
        }

        try
        {
            var prompt = BuildDescriptionPrompt(project);
            System.Diagnostics.Debug.WriteLine($"[ContentSuggestion] Beschreibung-Prompt Länge: {prompt.Length}");

            var response = await _llmClient.CompleteAsync(prompt, cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[ContentSuggestion] Beschreibung-Response Länge: {response?.Length ?? 0}");

            if (string.IsNullOrWhiteSpace(response) || TextCleaningUtility.IsErrorResponse(response))
            {
                System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Beschreibung: Ungültige Response -> Fallback");
                return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
            }

            var cleaned = CleanDescriptionResponse(response, project);

            if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length < 20)
            {
                System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Beschreibung: Zu kurz -> Fallback");
                return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
            }

            var customPromptWords = ExtractFilterWords(_settings.DescriptionCustomPrompt);
            if (ContainsPromptLeakage(cleaned, customPromptWords))
            {
                System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Beschreibung: Prompt-Leakage erkannt -> Fallback");
                return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
            }

            if (IsDescriptionJustTitle(cleaned, project))
            {
                System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Beschreibung: Ist nur Titel-Kopie -> Fallback");
                return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
            }

            return cleaned;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ContentSuggestion] Beschreibung-Exception: {ex.Message}");
            return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
        }
    }

    private string BuildDescriptionPrompt(UploadProject project)
    {
        var sb = new StringBuilder();
        var style = GetRandomVariation(DescriptionStyles);
        var sessionId = GetSessionId();

        sb.AppendLine("<|system|>");
        sb.AppendLine("Du schreibst YouTube-Videobeschreibungen auf Deutsch.");
        sb.AppendLine("WICHTIG: Die Beschreibung muss den VIDEO-INHALT zusammenfassen, NICHT den Titel wiederholen!");
        sb.AppendLine("Ausgabe: Nur die fertige Beschreibung, 2-4 Sätze Fließtext.");
        sb.AppendLine("Verboten: Aufzählungen, Emojis, Hashtags, Überschriften, Erklärungen, Titel-Wiederholung.");
        sb.AppendLine("<|end|>");

        sb.AppendLine("<|user|>");
        sb.AppendLine($"[Session: {sessionId}]");
        sb.AppendLine($"Stil: {style}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(_settings.DescriptionCustomPrompt))
        {
            sb.AppendLine($"Beachte: {TextCleaningUtility.SanitizeCustomPrompt(_settings.DescriptionCustomPrompt)}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(project.Title))
        {
            sb.AppendLine($"Der Videotitel lautet: \"{project.Title}\"");
            sb.AppendLine("WIEDERHOLE DIESEN TITEL NICHT in der Beschreibung!");
            sb.AppendLine();
        }

        sb.AppendLine("Schreibe eine neue Beschreibung die den VIDEO-INHALT zusammenfasst basierend auf diesem Transkript:");
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT START---");
        sb.Append(TextCleaningUtility.TruncateTranscript(project.TranscriptText!, MaxTranscriptCharsForDescription));
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT ENDE---");
        sb.AppendLine("<|end|>");
        sb.AppendLine("<|assistant|>");

        return sb.ToString();
    }

    private static bool IsDescriptionJustTitle(string description, UploadProject project)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(project.Title))
        {
            return false;
        }

        var descClean = TextCleaningUtility.NormalizeForComparison(description);
        var titleClean = TextCleaningUtility.NormalizeForComparison(project.Title);

        if (descClean == titleClean)
        {
            return true;
        }

        if (descClean.Length <= titleClean.Length * 1.3)
        {
            var similarity = TextCleaningUtility.CalculateSimilarity(descClean, titleClean);
            if (similarity > 0.7)
            {
                return true;
            }
        }

        if (descClean.StartsWith(titleClean))
        {
            var remainder = descClean[titleClean.Length..].Trim();
            if (remainder.Length < 50)
            {
                return true;
            }
        }

        if (descClean.Contains(titleClean))
        {
            var ratio = (double)titleClean.Length / descClean.Length;
            if (ratio > 0.5)
            {
                return true;
            }
        }

        var descLower = description.ToLowerInvariant();
        if (descLower.StartsWith("in diesem video") ||
            descLower.StartsWith("dieses video") ||
            descLower.StartsWith("video über"))
        {
            var afterPhrase = PhrasePatternRegex().Replace(descLower, "").Trim();
            var titleLower = project.Title.ToLowerInvariant();

            if (TextCleaningUtility.CalculateSimilarity(afterPhrase, titleLower) > 0.7)
            {
                return true;
            }
        }

        return false;
    }

    private static string CleanDescriptionResponse(string response, UploadProject project)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return string.Empty;
        }

        var lines = response
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !TextCleaningUtility.IsMetaLine(l))
            .Where(l => !l.StartsWith("Beschreibung:", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("YouTube-Beschreibung:", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("Hier ist", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("Hier die", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("Die Beschreibung", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = string.Join("\n", lines).Trim();
        result = TextCleaningUtility.RemoveQuotes(result);

        if (!string.IsNullOrWhiteSpace(project.Title))
        {
            result = RemoveTitleFromStart(result, project.Title);
        }

        return result;
    }

    private static string RemoveTitleFromStart(string description, string title)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(title))
        {
            return description;
        }

        var descNorm = TextCleaningUtility.NormalizeForComparison(description);
        var titleNorm = TextCleaningUtility.NormalizeForComparison(title);

        if (!descNorm.StartsWith(titleNorm))
        {
            return description;
        }

        var lines = description.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > 0)
        {
            var firstLineNorm = TextCleaningUtility.NormalizeForComparison(lines[0]);
            var similarity = TextCleaningUtility.CalculateSimilarity(firstLineNorm, titleNorm);

            if (similarity > 0.8)
            {
                var remainingLines = lines.Skip(1).ToArray();
                var result = string.Join("\n", remainingLines).Trim();

                if (result.Length >= 30)
                {
                    return result;
                }
            }
        }

        return description;
    }

    #endregion

    #region SuggestTagsAsync

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        if (!HasTranscript(project))
        {
            System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Tags: KEIN Transkript vorhanden -> Fallback");
            return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
        }

        if (!EnsureLlmReady())
        {
            System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Tags: LLM nicht bereit -> Fallback");
            return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
        }

        try
        {
            var prompt = BuildTagsPrompt(project);
            System.Diagnostics.Debug.WriteLine($"[ContentSuggestion] Tags-Prompt Länge: {prompt.Length}");

            var response = await _llmClient.CompleteAsync(prompt, cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[ContentSuggestion] Tags-Response: '{response}'");

            if (string.IsNullOrWhiteSpace(response) || TextCleaningUtility.IsErrorResponse(response))
            {
                System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Tags: Ungültige Response -> Fallback");
                return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
            }

            var tags = ParseTagsResponse(response);

            if (tags.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Tags: Keine Tags geparsed -> Fallback");
                return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
            }

            return tags;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ContentSuggestion] Tags-Exception: {ex.Message}");
            return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
        }
    }

    private string BuildTagsPrompt(UploadProject project)
    {
        var sb = new StringBuilder();
        var focus = GetRandomVariation(TagFocuses);
        var sessionId = GetSessionId();

        sb.AppendLine("<|system|>");
        sb.AppendLine("Du generierst YouTube-Tags auf Deutsch.");
        sb.AppendLine("WICHTIG: Jeder Tag ist EIN EINZELNES WORT.");
        sb.AppendLine("Ausgabe: 15-20 Wörter, kommasepariert, eine Zeile.");
        sb.AppendLine("Verboten: Mehrworttags, Unterstriche, Bindestriche, Hashtags, Nummerierung.");
        sb.AppendLine("<|end|>");

        sb.AppendLine("<|user|>");
        sb.AppendLine($"[Session: {sessionId}]");
        sb.AppendLine($"{focus}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(_settings.TagsCustomPrompt))
        {
            sb.AppendLine($"Beachte: {TextCleaningUtility.SanitizeCustomPrompt(_settings.TagsCustomPrompt)}");
            sb.AppendLine();
        }

        sb.AppendLine("Generiere neue, einzelne Wörter als Tags basierend auf diesem Transkript:");
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT START---");
        sb.Append(TextCleaningUtility.TruncateTranscript(project.TranscriptText!, MaxTranscriptCharsForTags));
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT ENDE---");
        sb.AppendLine("<|end|>");
        sb.AppendLine("<|assistant|>");

        return sb.ToString();
    }

    private List<string> ParseTagsResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return new List<string>();
        }

        var customPromptWords = ExtractFilterWords(_settings.TagsCustomPrompt);
        var separators = new[] { ',', '\n', '\r', ';', '|' };

        var rawTags = response
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var cleanedTags = new List<string>();

        foreach (var rawTag in rawTags)
        {
            var singleWords = TextCleaningUtility.ExtractSingleWords(rawTag);
            cleanedTags.AddRange(singleWords);
        }

        return cleanedTags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Where(t => !TextCleaningUtility.IsMetaLine(t))
            .Where(t => !IsPromptLeakageWord(t, customPromptWords))
            .Where(t => !t.StartsWith("Tags:", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Equals("Keine", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Equals("Session", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Equals("Fokus", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Contains("ableitbar", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Contains("vorhanden", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Contains("transkript", StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Length >= 2 && t.Length <= 30)
            .Where(TextCleaningUtility.IsValidSingleWordTag)
            .Select(t => t.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
    }

    #endregion

    #region Prompt Leakage Prevention

    private static HashSet<string> ExtractFilterWords(string? customPrompt)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var instructionWords = new[]
        {
            "verwende", "benutze", "nutze", "schreibe", "generiere", "erstelle",
            "achte", "beachte", "wichtig", "immer", "niemals", "vermeiden",
            "sollen", "müssen", "können", "bitte", "format", "stil",
            "anweisung", "instruktion", "prompt", "session"
        };

        foreach (var word in instructionWords)
        {
            words.Add(word);
        }

        if (!string.IsNullOrWhiteSpace(customPrompt))
        {
            var promptWords = customPrompt
                .Split(new[] { ' ', ',', '.', ':', ';', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 5)
                .Where(w => char.IsLetter(w[0]));

            foreach (var word in promptWords)
            {
                words.Add(word.ToLowerInvariant());
            }
        }

        return words;
    }

    private static bool ContainsPromptLeakage(string text, HashSet<string> filterWords)
    {
        if (string.IsNullOrWhiteSpace(text) || filterWords.Count == 0)
        {
            return false;
        }

        var lower = text.ToLowerInvariant();

        if (lower.Contains("hier ist") && lower.Contains("beschreibung"))
        {
            return true;
        }

        if (lower.Contains("basierend auf") && lower.Contains("anweisung"))
        {
            return true;
        }

        if (lower.Contains("gemäß") || lower.Contains("laut anweisung"))
        {
            return true;
        }

        if (lower.Contains("wie gewünscht") || lower.Contains("wie angefordert"))
        {
            return true;
        }

        if (SessionIdLeakageRegex().IsMatch(lower))
        {
            return true;
        }

        return false;
    }

    private static bool IsPromptLeakageWord(string word, HashSet<string> filterWords)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        return filterWords.Contains(word.ToLowerInvariant());
    }

    #endregion

    [GeneratedRegex(@"^(in diesem video|dieses video|video über)[:\s]*")]
    private static partial Regex PhrasePatternRegex();

    [GeneratedRegex(@"session[:\s]*[a-f0-9]{8}")]
    private static partial Regex SessionIdLeakageRegex();
}