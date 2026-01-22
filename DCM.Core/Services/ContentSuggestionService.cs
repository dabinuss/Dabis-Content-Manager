using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using DCM.Core.Configuration;
using DCM.Core.Logging;
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
    private readonly IAppLogger _logger;
    private readonly object _initLock = new();

    private const int MaxTranscriptCharsForDescription = 4000;
    private const int MaxTranscriptCharsForTags = 2000;
    private const int MaxTranscriptCharsForTitles = 1500;
    private const int MaxTranscriptCharsForChapters = 6000;
    private const int ChapterChunkSize = 3500;
    private const int ChapterChunkOverlap = 400;
    private const int MinimumTranscriptLength = 50;
    private const int StringBuilderPoolLimit = 8;
    private const int StringBuilderMaxCapacity = 8192;
    private static readonly ConcurrentBag<StringBuilder> StringBuilderPool = new();

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
        LlmSettings settings,
        IAppLogger? logger = null)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _fallbackSuggestionService = fallbackSuggestionService ?? throw new ArgumentNullException(nameof(fallbackSuggestionService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? AppLogger.Instance;
    }

    private async Task<bool> EnsureLlmReadyAsync(CancellationToken cancellationToken)
    {
        if (!_settings.IsLocalMode)
        {
            return false;
        }

        if (_llmClient.IsReady)
        {
            return true;
        }

        return await Task.Run(() =>
        {
            lock (_initLock)
            {
                if (_llmClient.IsReady)
                {
                    return true;
                }

                cancellationToken.ThrowIfCancellationRequested();

                _logger.Debug("Initialisiere LLM-Client...", "ContentSuggestion");
                var result = _llmClient.TryInitialize();

                if (result)
                {
                    _logger.Info("LLM-Client erfolgreich initialisiert", "ContentSuggestion");
                }
                else
                {
                    _logger.Warning("LLM-Client konnte nicht initialisiert werden", "ContentSuggestion");
                }

                return result;
            }
        }, cancellationToken);
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
        if (options.Length == 0)
        {
            throw new ArgumentException("Options must not be empty.", nameof(options));
        }

        return options[Random.Shared.Next(options.Length)];
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
            _logger.Debug("Titel: Kein Transkript vorhanden -> Fallback", "ContentSuggestion");
            return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
        }

        if (!await EnsureLlmReadyAsync(cancellationToken))
        {
            _logger.Debug("Titel: LLM nicht bereit -> Fallback", "ContentSuggestion");
            return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
        }

        try
        {
            var prompt = BuildTitlePrompt(project);
            _logger.Debug($"Titel-Prompt erstellt, Länge: {prompt.Length} Zeichen", "ContentSuggestion");

            var response = await _llmClient.CompleteAsync(prompt, cancellationToken);
            _logger.Debug($"Titel-Response erhalten, Länge: {response?.Length ?? 0} Zeichen", "ContentSuggestion");

            if (string.IsNullOrWhiteSpace(response) || TextCleaningUtility.IsErrorResponse(response))
            {
                _logger.Warning("Titel: Ungültige LLM-Response -> Fallback", "ContentSuggestion");
                return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
            }

            var titles = ParseTitleResponse(response);

            if (titles.Count == 0)
            {
                _logger.Warning("Titel: Keine Titel aus Response geparsed -> Fallback", "ContentSuggestion");
                return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
            }

            _logger.Info($"Titel: {titles.Count} Vorschläge generiert", "ContentSuggestion");
            return titles;
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Titel-Generierung abgebrochen", "ContentSuggestion");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Titel-Generierung fehlgeschlagen: {ex.Message}", "ContentSuggestion", ex);
            return await _fallbackSuggestionService.SuggestTitlesAsync(project, persona, cancellationToken);
        }
    }

    private string BuildTitlePrompt(UploadProject project)
    {
        var sb = AcquireBuilder();
        var style = GetRandomVariation(TitleStyles);
        var sessionId = GetSessionId();

        try
        {
            sb.AppendLine("<|system|>");
            sb.AppendLine("Du generierst YouTube-Videotitel auf Deutsch.");
            sb.AppendLine("Ausgabe: Bis zu 5 Titel, jeder in einer eigenen Zeile.");
            sb.AppendLine("Format: Nur die Titel, nichts anderes; kein Satz davor oder danach.");
            sb.AppendLine("Jeder Titel ist ein einzelner kurzer Satz (5-12 Wörter), keine Nummerierung oder Aufzählungszeichen.");
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

            sb.AppendLine("Generiere bis zu 5 neue, unterschiedliche Titel basierend auf diesem Transkript:");
            sb.AppendLine();
            sb.AppendLine("---TRANSKRIPT START---");
            sb.Append(TextCleaningUtility.TruncateTranscript(project.TranscriptText!, MaxTranscriptCharsForTitles));
            sb.AppendLine();
            sb.AppendLine("---TRANSKRIPT ENDE---");
            sb.AppendLine("<|end|>");
            sb.AppendLine("<|assistant|>");

            return sb.ToString();
        }
        finally
        {
            ReleaseBuilder(sb);
        }
    }

    private List<string> ParseTitleResponse(string response)
    {
        var customPromptWords = ExtractFilterWords(_settings.TitleCustomPrompt);

        return response
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !TextCleaningUtility.IsMetaLine(line))
            .Select(StripListPrefix)
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

    public async Task<IReadOnlyList<string>> SuggestDescriptionAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        if (!HasTranscript(project))
        {
            _logger.Debug("Beschreibung: Kein Transkript vorhanden -> Fallback", "ContentSuggestion");
            return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
        }

        if (!await EnsureLlmReadyAsync(cancellationToken))
        {
            _logger.Debug("Beschreibung: LLM nicht bereit -> Fallback", "ContentSuggestion");
            return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
        }

        try
        {
            var prompt = BuildDescriptionPrompt(project);
            _logger.Debug($"Beschreibung-Prompt erstellt, Länge: {prompt.Length} Zeichen", "ContentSuggestion");

            var response = await _llmClient.CompleteAsync(prompt, cancellationToken);
            _logger.Debug($"Beschreibung-Response erhalten, Länge: {response?.Length ?? 0} Zeichen", "ContentSuggestion");

            if (string.IsNullOrWhiteSpace(response) || TextCleaningUtility.IsErrorResponse(response))
            {
                _logger.Warning("Beschreibung: Ungültige LLM-Response -> Fallback", "ContentSuggestion");
                return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
            }

            var descriptions = ParseDescriptionResponses(response, project);

            if (descriptions.Count == 0)
            {
                _logger.Warning("Beschreibung: Bereinigte Response zu kurz -> Fallback", "ContentSuggestion");
                return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
            }

            _logger.Info($"Beschreibung generiert, Anzahl: {descriptions.Count}", "ContentSuggestion");
            return descriptions;
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Beschreibung-Generierung abgebrochen", "ContentSuggestion");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Beschreibung-Generierung fehlgeschlagen: {ex.Message}", "ContentSuggestion", ex);
            return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
        }
    }

    private string BuildDescriptionPrompt(UploadProject project)
    {
        var sb = AcquireBuilder();
        var style = GetRandomVariation(DescriptionStyles);
        var sessionId = GetSessionId();

        try
        {
            sb.AppendLine("<|system|>");
            sb.AppendLine("Du schreibst YouTube-Videobeschreibungen auf Deutsch.");
            sb.AppendLine("WICHTIG: Die Beschreibung muss den VIDEO-INHALT zusammenfassen, NICHT den Titel wiederholen!");
            sb.AppendLine("Ausgabe: Bis zu 5 unterschiedliche Beschreibungen, jeweils als eigener Absatz, getrennt durch eine Leerzeile.");
            sb.AppendLine("Jeder Absatz umfasst 2-4 Sätze, klarer Fließtext, mindestens 80 Zeichen.");
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

            sb.AppendLine("Schreibe bis zu 5 neue, verschiedene Beschreibungen, die den VIDEO-INHALT zusammenfassen, basierend auf diesem Transkript:");
            sb.AppendLine();
            sb.AppendLine("---TRANSKRIPT START---");
            sb.Append(TextCleaningUtility.TruncateTranscript(project.TranscriptText!, MaxTranscriptCharsForDescription));
            sb.AppendLine();
            sb.AppendLine("---TRANSKRIPT ENDE---");
            sb.AppendLine("<|end|>");
            sb.AppendLine("<|assistant|>");

            return sb.ToString();
        }
        finally
        {
            ReleaseBuilder(sb);
        }
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

    private List<string> ParseDescriptionResponses(string response, UploadProject project)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return new List<string>();
        }

        var normalized = response.Replace("\r\n", "\n");
        var parts = normalized
            .Split(new[] { "\n\n", "---" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (parts.Count == 0)
        {
            parts.Add(normalized);
        }

        var customPromptWords = ExtractFilterWords(_settings.DescriptionCustomPrompt);

        var cleaned = parts
            .Select(p => CleanDescriptionBlock(p, project))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Where(p => p.Length >= 20)
            .Where(p => !ContainsPromptLeakage(p, customPromptWords))
            .Where(p => !IsDescriptionJustTitle(p, project))
            .Select(p => p.Trim())
            .Distinct()
            .Take(5)
            .ToList();

        return cleaned;
    }

    private static string CleanDescriptionBlock(string response, UploadProject project)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return string.Empty;
        }

        var lines = response
            .Split('\n')
            .Select(l => l.Trim())
            .Select(StripListPrefix)
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
            _logger.Debug("Tags: Kein Transkript vorhanden -> Fallback", "ContentSuggestion");
            return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
        }

        if (!await EnsureLlmReadyAsync(cancellationToken))
        {
            _logger.Debug("Tags: LLM nicht bereit -> Fallback", "ContentSuggestion");
            return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
        }

        try
        {
            var prompt = BuildTagsPrompt(project);
            _logger.Debug($"Tags-Prompt erstellt, Länge: {prompt.Length} Zeichen", "ContentSuggestion");

            var response = await _llmClient.CompleteAsync(prompt, cancellationToken);
            _logger.Debug($"Tags-Response erhalten, Länge: {response?.Length ?? 0} Zeichen", "ContentSuggestion");

            if (string.IsNullOrWhiteSpace(response) || TextCleaningUtility.IsErrorResponse(response))
            {
                _logger.Warning("Tags: Ungültige LLM-Response -> Fallback", "ContentSuggestion");
                return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
            }

            var tags = ParseTagsResponse(response);

            if (tags.Count == 0)
            {
                _logger.Warning("Tags: Keine Tags aus Response geparsed -> Fallback", "ContentSuggestion");
                return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
            }

            _logger.Info($"Tags: {tags.Count} Vorschläge generiert", "ContentSuggestion");
            return tags;
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Tags-Generierung abgebrochen", "ContentSuggestion");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Tags-Generierung fehlgeschlagen: {ex.Message}", "ContentSuggestion", ex);
            return await _fallbackSuggestionService.SuggestTagsAsync(project, persona, cancellationToken);
        }
    }

    private string BuildTagsPrompt(UploadProject project)
    {
        var sb = AcquireBuilder();
        var focus = GetRandomVariation(TagFocuses);
        var sessionId = GetSessionId();

        try
        {
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
        finally
        {
            ReleaseBuilder(sb);
        }
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

    #region SuggestChaptersAsync

    public async Task<IReadOnlyList<ChapterTopic>> SuggestChaptersAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        var originalMaxTokens = _settings.MaxTokens;

        if (!HasTranscript(project))
        {
            _logger.Debug("Kapitel: Kein Transkript vorhanden -> Fallback", "ContentSuggestion");
            return await _fallbackSuggestionService.SuggestChaptersAsync(project, persona, cancellationToken);
        }

        if (!await EnsureLlmReadyAsync(cancellationToken))
        {
            _logger.Debug("Kapitel: LLM nicht bereit -> Fallback", "ContentSuggestion");
            return await _fallbackSuggestionService.SuggestChaptersAsync(project, persona, cancellationToken);
        }

        try
        {
            var maxTokensOverride = Math.Max(originalMaxTokens, 1024);
            if (maxTokensOverride != originalMaxTokens)
            {
                _settings.MaxTokens = maxTokensOverride;
            }

            var normalizedTranscript = NormalizeTranscriptForPrompt(project.TranscriptText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedTranscript))
            {
                _logger.Warning("Kapitel: Transkript leer nach Normalisierung -> Fallback", "ContentSuggestion");
                return await _fallbackSuggestionService.SuggestChaptersAsync(project, persona, cancellationToken);
            }

            IReadOnlyList<ChapterTopic> chapters;

            var normalizedTranscriptForMatch = NormalizeForMatch(normalizedTranscript);

            if (normalizedTranscript.Length <= MaxTranscriptCharsForChapters)
            {
                var prompt = BuildChaptersPrompt(
                    normalizedTranscript,
                    strictJson: false,
                    minChapters: 6,
                    maxChapters: 10,
                    chunkHint: null);
                _logger.Debug($"Kapitel-Prompt erstellt, Laenge: {prompt.Length} Zeichen", "ContentSuggestion");

                var response = await _llmClient.CompleteAsync(prompt, cancellationToken);
                _logger.Debug($"Kapitel-Response erhalten, Laenge: {response?.Length ?? 0} Zeichen", "ContentSuggestion");

                var parsed = ParseChaptersResponse(
                    response,
                    normalizedTranscript,
                    minExpected: 6,
                    normalizedTranscriptOverride: normalizedTranscriptForMatch);
                if (parsed.Count < 6)
                {
                    _logger.Warning("Kapitel: Zu wenige valide Anker -> Retry", "ContentSuggestion");

                    var retryPrompt = BuildChaptersPrompt(
                        normalizedTranscript,
                        strictJson: true,
                        minChapters: 6,
                        maxChapters: 10,
                        chunkHint: null);
                    _logger.Debug($"Kapitel-Retry-Prompt erstellt, Laenge: {retryPrompt.Length} Zeichen", "ContentSuggestion");

                    var retryResponse = await _llmClient.CompleteAsync(retryPrompt, cancellationToken);
                    _logger.Debug($"Kapitel-Retry-Response erhalten, Laenge: {retryResponse?.Length ?? 0} Zeichen", "ContentSuggestion");

                    parsed = ParseChaptersResponse(
                        retryResponse,
                        normalizedTranscript,
                        minExpected: 6,
                        normalizedTranscriptOverride: normalizedTranscriptForMatch);
                }

                chapters = parsed;
                if (chapters.Count < 6)
                {
                    _logger.Warning("Kapitel: Fallback auf Segmentierung", "ContentSuggestion");
                    chapters = await SuggestChaptersChunkedAsync(
                        normalizedTranscript,
                        cancellationToken,
                        forcedChunkSize: GetFallbackChunkSize(normalizedTranscript),
                        normalizedTranscriptForMatch: normalizedTranscriptForMatch);
                }
            }
            else
            {
                chapters = await SuggestChaptersChunkedAsync(
                    normalizedTranscript,
                    cancellationToken,
                    normalizedTranscriptForMatch: normalizedTranscriptForMatch);
            }

            if (chapters.Count == 0)
            {
                _logger.Warning("Kapitel: Keine Kapitel aus Response geparsed -> Fallback", "ContentSuggestion");
                return await _fallbackSuggestionService.SuggestChaptersAsync(project, persona, cancellationToken);
            }

            chapters = await EnsureChapterTitlesAsync(chapters, normalizedTranscript, cancellationToken);
            if (chapters.Count == 0)
            {
                _logger.Warning("Kapitel: Keine gueltigen Titel generiert", "ContentSuggestion");
                return Array.Empty<ChapterTopic>();
            }

            _logger.Info($"Kapitel: {chapters.Count} Themenbereiche generiert", "ContentSuggestion");
            return chapters;
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Kapitel-Generierung abgebrochen", "ContentSuggestion");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Kapitel-Generierung fehlgeschlagen: {ex.Message}", "ContentSuggestion", ex);
            return await _fallbackSuggestionService.SuggestChaptersAsync(project, persona, cancellationToken);
        }
        finally
        {
            _settings.MaxTokens = originalMaxTokens;
        }
    }

    private async Task<IReadOnlyList<ChapterTopic>> SuggestChaptersChunkedAsync(
        string normalizedTranscript,
        CancellationToken cancellationToken,
        int? forcedChunkSize = null,
        string? normalizedTranscriptForMatch = null)
    {
        var chunkSize = forcedChunkSize ?? ChapterChunkSize;
        var overlap = Math.Min(ChapterChunkOverlap, Math.Max(100, chunkSize / 4));
        var chunks = SplitTranscriptIntoChunks(normalizedTranscript, chunkSize, overlap);
        if (chunks.Count == 0)
        {
            return Array.Empty<ChapterTopic>();
        }

        var totalChunks = chunks.Count;
        var minPerChunk = totalChunks <= 3 ? 3 : totalChunks <= 6 ? 2 : 1;
        var maxPerChunk = totalChunks <= 3 ? 5 : totalChunks <= 6 ? 4 : 3;

        var normalizedMatch = !string.IsNullOrWhiteSpace(normalizedTranscriptForMatch)
            ? normalizedTranscriptForMatch
            : NormalizeForMatch(normalizedTranscript);

        var chunkedTopics = new List<ChunkTopic>();
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = chunks[i];

            var prompt = BuildChaptersPrompt(
                chunk.Text,
                strictJson: false,
                minChapters: minPerChunk,
                maxChapters: maxPerChunk,
                chunkHint: $"{i + 1}/{totalChunks}");

            _logger.Debug($"Kapitel-Chunk-Prompt erstellt ({i + 1}/{totalChunks}), Laenge: {prompt.Length} Zeichen", "ContentSuggestion");

            var response = await _llmClient.CompleteAsync(prompt, cancellationToken);
            _logger.Debug($"Kapitel-Chunk-Response erhalten ({i + 1}/{totalChunks}), Laenge: {response?.Length ?? 0} Zeichen", "ContentSuggestion");

            var parsed = ParseChaptersResponse(
                response,
                normalizedTranscript,
                minExpected: minPerChunk,
                normalizedTranscriptOverride: normalizedMatch);
            if (parsed.Count < minPerChunk)
            {
                _logger.Warning($"Kapitel-Chunk {i + 1}/{totalChunks}: Zu wenige valide Anker -> Retry", "ContentSuggestion");

                var retryPrompt = BuildChaptersPrompt(
                    chunk.Text,
                    strictJson: true,
                    minChapters: minPerChunk,
                    maxChapters: maxPerChunk,
                    chunkHint: $"{i + 1}/{totalChunks}");

                _logger.Debug($"Kapitel-Chunk-Retry-Prompt erstellt ({i + 1}/{totalChunks}), Laenge: {retryPrompt.Length} Zeichen", "ContentSuggestion");

                var retryResponse = await _llmClient.CompleteAsync(retryPrompt, cancellationToken);
                _logger.Debug($"Kapitel-Chunk-Retry-Response erhalten ({i + 1}/{totalChunks}), Laenge: {retryResponse?.Length ?? 0} Zeichen", "ContentSuggestion");

                parsed = ParseChaptersResponse(
                    retryResponse,
                    normalizedTranscript,
                    minExpected: minPerChunk,
                    normalizedTranscriptOverride: normalizedMatch);
            }

            foreach (var topic in parsed)
            {
                chunkedTopics.Add(new ChunkTopic(topic, chunk.StartIndex));
            }
        }

        if (chunkedTopics.Count == 0)
        {
            return Array.Empty<ChapterTopic>();
        }

        var lowerTranscript = normalizedTranscript.ToLowerInvariant();
        var ordered = chunkedTopics
            .Select((entry, index) => new ChapterOrder(
                entry.Topic,
                GetTopicPosition(lowerTranscript, entry.Topic, entry.StartIndex),
                index))
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Order)
            .ToList();

        var maxDesired = totalChunks * maxPerChunk;
        var minDesired = Math.Min(8, maxDesired);
        var desiredMin = Math.Clamp(normalizedTranscript.Length / 1500, minDesired, maxDesired);

        var merged = MergeChapters(ordered, aggressive: true);
        if (merged.Count < desiredMin)
        {
            merged = MergeChapters(ordered, aggressive: false);
        }

        return merged.Select(m => m.Topic).ToList();
    }

    private string BuildChaptersPrompt(
        string transcript,
        bool strictJson,
        int minChapters,
        int maxChapters,
        string? chunkHint)
    {
        var sb = AcquireBuilder();
        var sessionId = GetSessionId();
        var normalizedTranscript = NormalizeTranscriptForPrompt(transcript);
        var truncated = TextCleaningUtility.TruncateTranscript(normalizedTranscript, MaxTranscriptCharsForChapters);

        try
        {
            sb.AppendLine("<|system|>");
            sb.AppendLine("Du segmentierst ein Transkript in thematische Kapitel.");
            sb.AppendLine("Antworte als JSON-Array, keine Markdown-Fences, keine Zusatztexte.");
            sb.AppendLine("Schema je Kapitel:");
            sb.AppendLine("{\"anchor\":\"<exakte Wortfolge aus dem Transkript>\",\"keywords\":[\"...\",\"...\"]}");
            sb.AppendLine("Anchor-Regeln:");
            sb.AppendLine("- MUSS exakte Wortfolge aus dem Transkript sein (2-12 Woerter, 1:1 kopieren).");
            sb.AppendLine("- Bevorzuge 3-8 Woerter; 2 Woerter nur wenn sehr eindeutig.");
            sb.AppendLine("- MUSS eindeutig sein (kommt idealerweise nur 1x vor, keine generischen Phrasen).");
            sb.AppendLine("- Waehle informative Phrasen, keine Standardfloskeln.");
            sb.AppendLine("- Keine Zeitangaben, keine Nummerierung, keine Ueberschriften.");
            sb.AppendLine("Keywords optional, leeres Array wenn unsicher.");
            sb.AppendLine("WICHTIG: Keine Titel liefern. Titel werden spaeter aus dem Kontext erzeugt.");
            sb.AppendLine("Diversitaet: Kapitel muessen klar unterschiedliche Schwerpunkte haben, keine wiederholten Oberthemen.");
            sb.AppendLine($"Gib {minChapters}-{maxChapters} Kapitel zurueck, lieber etwas mehr als zu wenig, wenn klar trennbar.");
            sb.AppendLine("Ausgabe muss mit '[' beginnen und mit ']' enden.");
            if (strictJson)
            {
                sb.AppendLine("WICHTIG: Ausgabe muss gueltiges JSON sein, sonst [].");
                sb.AppendLine("Vorige Antwort hatte zu wenige valide Anker. Waehle eindeutigere Anker.");
            }
            sb.AppendLine("<|end|>");

            sb.AppendLine("<|user|>");
            sb.AppendLine($"[Session: {sessionId}]");
            if (!string.IsNullOrWhiteSpace(chunkHint))
            {
                sb.AppendLine($"Ausschnitt: {chunkHint}");
            }
            sb.AppendLine("Ordne die Themen so, wie sie im Transkript vorkommen.");
            sb.AppendLine("Anker nur fuer diesen Ausschnitt, keine generischen Wiederholungen.");
            sb.AppendLine();
            sb.AppendLine("---TRANSKRIPT START---");
            sb.Append(truncated);
            sb.AppendLine();
            sb.AppendLine("---TRANSKRIPT ENDE---");
            sb.AppendLine("<|end|>");
            sb.AppendLine("<|assistant|>");

            return sb.ToString();
        }
        finally
        {
            ReleaseBuilder(sb);
        }
    }

    private List<ChapterTopic> ParseChaptersResponse(
        string? response,
        string transcript,
        int minExpected = 0,
        string? normalizedTranscriptOverride = null)
    {
        if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(transcript))
        {
            return new List<ChapterTopic>();
        }

        var json = ExtractJsonPayload(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ChapterTopic>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var elements = new List<JsonElement>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                elements.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetArrayProperty(root, new[] { "chapters", "topics", "items" }, out var array))
                {
                    elements.AddRange(array);
                }
                else
                {
                    elements.Add(root);
                }
            }

            var normalizedTranscript = !string.IsNullOrWhiteSpace(normalizedTranscriptOverride)
                ? normalizedTranscriptOverride
                : NormalizeForMatch(transcript);
            var strictTopics = new List<ChapterTopic>();
            var relaxedTopics = new List<ChapterTopic>();
            var looseTopics = new List<ChapterTopic>();

            foreach (var element in elements)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var anchor = GetStringProperty(element, "anchor", "anker", "anchorText", "ankertext", "quote");

                if (string.IsNullOrWhiteSpace(anchor))
                {
                    continue;
                }

                anchor = TextCleaningUtility.RemoveQuotes(anchor).Trim();

                var normalizedAnchor = NormalizeForMatch(anchor);
                if (!string.IsNullOrWhiteSpace(normalizedAnchor))
                {
                    var wordCount = CountWords(normalizedAnchor);
                    var occurrences = 0;
                    if (normalizedTranscript.Contains(normalizedAnchor, StringComparison.Ordinal))
                    {
                        occurrences = CountOccurrences(normalizedTranscript, normalizedAnchor);
                    }

                    if (occurrences == 0)
                    {
                        continue;
                    }

                    var keywords = GetKeywordsProperty(element, "keywords", "stichwoerter", "schluesselwoerter");
                    if (keywords.Count == 0)
                    {
                        keywords = ExtractKeywords(anchor);
                    }

                    var topic = new ChapterTopic(string.Empty, keywords, anchor);

                    if (wordCount >= 3
                        && wordCount <= 14
                        && occurrences <= 6)
                    {
                        strictTopics.Add(topic);
                    }

                    if (wordCount >= 2
                        && wordCount <= 16
                        && occurrences <= 10)
                    {
                        relaxedTopics.Add(topic);
                    }

                    if (wordCount >= 2
                        && wordCount <= 20
                        && occurrences <= 14)
                    {
                        looseTopics.Add(topic);
                    }
                }
            }

            var strictDistinct = strictTopics
                .DistinctBy(t => t.AnchorText ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var relaxedDistinct = relaxedTopics
                .DistinctBy(t => t.AnchorText ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var looseDistinct = looseTopics
                .DistinctBy(t => t.AnchorText ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var minCount = Math.Max(1, minExpected);
            var best = new[] { strictDistinct, relaxedDistinct, looseDistinct }
                .Where(list => list.Count > 0)
                .OrderByDescending(list => list.Count)
                .FirstOrDefault();

            var bestMeetingMin = new[] { strictDistinct, relaxedDistinct, looseDistinct }
                .Where(list => list.Count >= minCount)
                .OrderByDescending(list => list.Count)
                .FirstOrDefault();

            return bestMeetingMin ?? best ?? new List<ChapterTopic>();
        }
        catch
        {
            return new List<ChapterTopic>();
        }
    }

    private async Task<IReadOnlyList<ChapterTopic>> EnsureChapterTitlesAsync(
        IReadOnlyList<ChapterTopic> topics,
        string transcript,
        CancellationToken cancellationToken)
    {
        if (topics.Count == 0)
        {
            return topics;
        }

        var needsTitles = topics.Any(t => string.IsNullOrWhiteSpace(t.Title));
        if (!needsTitles)
        {
            return topics;
        }

        try
        {
            var missing = topics.Where(t => string.IsNullOrWhiteSpace(t.Title)).ToList();
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await GenerateTitlesForMissingAsync(missing, transcript, strict: false, resolved, cancellationToken);
            missing = missing
                .Where(t => !resolved.ContainsKey(NormalizeForMatch(t.AnchorText ?? string.Empty)))
                .ToList();

            if (missing.Count > 0)
            {
                await GenerateTitlesForMissingAsync(missing, transcript, strict: true, resolved, cancellationToken);
            }

            return topics
                .Select(topic =>
                {
                    if (!string.IsNullOrWhiteSpace(topic.Title))
                    {
                        return topic;
                    }

                    var key = NormalizeForMatch(topic.AnchorText ?? string.Empty);
                    if (resolved.TryGetValue(key, out var title) && IsValidChapterTitle(title))
                    {
                        return topic with { Title = title };
                    }

                    return topic;
                })
                .Where(t => IsValidChapterTitle(t.Title))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Kapitel: Titel-Generierung fehlgeschlagen ({ex.Message})", "ContentSuggestion");
            return topics
                .Where(t => IsValidChapterTitle(t.Title))
                .ToList();
        }
    }

    private async Task GenerateTitlesForMissingAsync(
        IReadOnlyList<ChapterTopic> missing,
        string transcript,
        bool strict,
        Dictionary<string, string> resolved,
        CancellationToken cancellationToken)
    {
        const int batchSize = 8;
        for (var i = 0; i < missing.Count; i += batchSize)
        {
            var batch = missing.Skip(i).Take(batchSize).ToList();
            var prompt = BuildChapterTitlesPrompt(batch, transcript, strict);
            var response = await _llmClient.CompleteAsync(prompt, cancellationToken);
            var titleMap = ParseChapterTitlesResponse(response);

            foreach (var pair in titleMap)
            {
                if (!resolved.ContainsKey(pair.Key))
                {
                    resolved[pair.Key] = pair.Value;
                }
            }
        }
    }

    private string BuildChapterTitlesPrompt(IReadOnlyList<ChapterTopic> topics, string transcript, bool strict)
    {
        var sb = AcquireBuilder();
        var sessionId = GetSessionId();

        try
        {
            sb.AppendLine("<|system|>");
            sb.AppendLine("Du erstellst kurze YouTube-Kapitel-Titel auf Deutsch.");
            sb.AppendLine("Ausgabe: JSON-Array, keine Markdown-Fences, keine Zusatztexte.");
            sb.AppendLine("Schema je Eintrag: {\"anchor\":\"<exakte Wortfolge>\",\"title\":\"<kurzer Titel>\"}");
            sb.AppendLine("Titel-Regeln: 2-8 Woerter, keine Nummerierung, keine generischen Titel.");
            sb.AppendLine("Titel darf KEIN direktes Zitat oder Teilstring des Anchors sein.");
            sb.AppendLine("Nutze nur den Kontext, keine neuen Fakten erfinden.");
            if (strict)
            {
                sb.AppendLine("WICHTIG: Titel muessen klar anders formuliert sein als der Anchor.");
            }
            sb.AppendLine("<|end|>");

            sb.AppendLine("<|user|>");
            sb.AppendLine($"[Session: {sessionId}]");
            sb.AppendLine("Erzeuge fuer jeden Anchor einen passenden Titel.");
            sb.AppendLine("Anchor muss im Output exakt gleich sein.");
            sb.AppendLine();

            foreach (var topic in topics)
            {
                if (string.IsNullOrWhiteSpace(topic.AnchorText))
                {
                    continue;
                }

                var context = BuildAnchorContext(transcript, topic.AnchorText, 320);
                if (string.IsNullOrWhiteSpace(context))
                {
                    context = topic.AnchorText;
                }

                sb.AppendLine($"Anchor: {topic.AnchorText}");
                sb.AppendLine($"Kontext: {context}");
                sb.AppendLine("---");
            }

            sb.AppendLine("<|end|>");
            sb.AppendLine("<|assistant|>");

            return sb.ToString();
        }
        finally
        {
            ReleaseBuilder(sb);
        }
    }

    private Dictionary<string, string> ParseChapterTitlesResponse(string? response)
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(response))
        {
            return titles;
        }

        var json = ExtractJsonPayload(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            return titles;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var elements = new List<JsonElement>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                elements.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetArrayProperty(root, new[] { "chapters", "titles", "items" }, out var array))
                {
                    elements.AddRange(array);
                }
                else
                {
                    elements.Add(root);
                }
            }

            foreach (var element in elements)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var anchor = GetStringProperty(element, "anchor", "anker", "anchorText", "ankertext", "quote");
                var title = GetStringProperty(element, "title", "titel", "name");

                if (string.IsNullOrWhiteSpace(anchor) || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var cleanedTitle = CleanChapterTitle(title);
                if (!IsValidChapterTitle(cleanedTitle))
                {
                    continue;
                }

                var key = NormalizeForMatch(anchor);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (IsTitleTooCloseToAnchor(cleanedTitle, anchor))
                {
                    continue;
                }

                titles[key] = cleanedTitle;
            }
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return titles;
    }

    private static string CleanChapterTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var cleaned = TextCleaningUtility.CleanTitleLine(title);
        cleaned = TextCleaningUtility.RemoveQuotes(cleaned);
        return cleaned.Trim();
    }

    private static bool IsValidChapterTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (title.Length < 3 || title.Length > 160)
        {
            return false;
        }

        return !IsChapterHeaderLine(title);
    }


    private static bool IsTitleTooCloseToAnchor(string title, string anchor)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(anchor))
        {
            return false;
        }

        var normalizedTitle = TextCleaningUtility.NormalizeForComparison(title);
        var normalizedAnchor = TextCleaningUtility.NormalizeForComparison(anchor);

        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(normalizedAnchor))
        {
            return false;
        }

        if (normalizedAnchor.Contains(normalizedTitle, StringComparison.Ordinal))
        {
            var ratio = (double)normalizedTitle.Length / normalizedAnchor.Length;
            if (ratio >= 0.7)
            {
                return true;
            }
        }

        var similarity = TextCleaningUtility.CalculateSimilarity(normalizedTitle, normalizedAnchor);
        return similarity >= 0.9;
    }

    private static string BuildAnchorContext(string transcript, string anchor, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(transcript) || string.IsNullOrWhiteSpace(anchor))
        {
            return string.Empty;
        }

        var lowerTranscript = transcript.ToLowerInvariant();
        var anchorLower = anchor.ToLowerInvariant();
        var anchorIndex = lowerTranscript.IndexOf(anchorLower, StringComparison.Ordinal);
        if (anchorIndex < 0)
        {
            return string.Empty;
        }

        var left = FindSentenceBoundaryLeft(transcript, anchorIndex, 2);
        var right = FindSentenceBoundaryRight(transcript, anchorIndex + anchor.Length, 2);
        if (right <= left)
        {
            return string.Empty;
        }

        var context = transcript[left..right].Trim();
        if (context.Length > maxChars)
        {
            var half = maxChars / 2;
            var start = Math.Max(0, anchorIndex - half);
            var end = Math.Min(transcript.Length, anchorIndex + anchor.Length + half);
            context = transcript[start..end].Trim();
        }

        return context.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    private static int FindSentenceBoundaryLeft(string text, int index, int count)
    {
        if (index <= 0)
        {
            return 0;
        }

        var pos = Math.Min(index, text.Length - 1);
        for (var i = 0; i < count; i++)
        {
            var boundary = text.LastIndexOfAny(new[] { '.', '!', '?', '\n' }, pos);
            if (boundary < 0)
            {
                return 0;
            }

            pos = Math.Max(0, boundary - 1);
        }

        return Math.Min(text.Length, pos + 2);
    }

    private static int FindSentenceBoundaryRight(string text, int index, int count)
    {
        if (index >= text.Length)
        {
            return text.Length;
        }

        var pos = Math.Max(0, index);
        for (var i = 0; i < count; i++)
        {
            var boundary = text.IndexOfAny(new[] { '.', '!', '?', '\n' }, pos);
            if (boundary < 0)
            {
                return text.Length;
            }

            pos = Math.Min(text.Length, boundary + 1);
        }

        return Math.Min(text.Length, pos + 1);
    }

    private static string ExtractJsonPayload(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return string.Empty;
        }

        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd > 0)
            {
                trimmed = trimmed[3..fenceEnd].Trim();
            }
        }

        var arrayStart = trimmed.IndexOf('[', StringComparison.Ordinal);
        var objectStart = trimmed.IndexOf('{', StringComparison.Ordinal);
        if (arrayStart >= 0 && (objectStart < 0 || arrayStart < objectStart))
        {
            var arrayEnd = trimmed.LastIndexOf(']');
            if (arrayEnd > arrayStart)
            {
                return trimmed[arrayStart..(arrayEnd + 1)];
            }
        }

        if (objectStart >= 0)
        {
            var objectEnd = trimmed.LastIndexOf('}');
            if (objectEnd > objectStart)
            {
                return trimmed[objectStart..(objectEnd + 1)];
            }
        }

        return string.Empty;
    }

    private static bool TryGetArrayProperty(JsonElement element, IReadOnlyList<string> names, out List<JsonElement> array)
    {
        array = new List<JsonElement>();
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    array.AddRange(property.Value.EnumerateArray());
                    return true;
                }
            }
        }

        return false;
    }

    private static string? GetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }

        return null;
    }

    private static List<string> GetKeywordsProperty(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value
                        .EnumerateArray()
                        .Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString() ?? string.Empty)
                        .Select(TextCleaningUtility.RemoveQuotes)
                        .Select(TextCleaningUtility.RemoveNumberPrefix)
                        .Select(k => k.Trim())
                        .Where(k => k.Length >= 3)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(12)
                        .ToList();
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    return ExtractKeywords(property.Value.GetString() ?? string.Empty);
                }
            }
        }

        return new List<string>();
    }

    private static string NormalizeForMatch(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(text, @"[^\p{L}\p{N}]+", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim().ToLowerInvariant();
    }

    private static string NormalizeTranscriptForPrompt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static StringBuilder AcquireBuilder(int capacity = 512)
    {
        if (StringBuilderPool.TryTake(out var builder))
        {
            builder.Clear();
            if (capacity > builder.Capacity)
            {
                builder.Capacity = capacity;
            }

            return builder;
        }

        return new StringBuilder(capacity);
    }

    private static void ReleaseBuilder(StringBuilder builder)
    {
        if (builder is null)
        {
            return;
        }

        builder.Clear();

        if (builder.Capacity > StringBuilderMaxCapacity)
        {
            builder.Capacity = StringBuilderMaxCapacity;
        }

        if (StringBuilderPool.Count < StringBuilderPoolLimit)
        {
            StringBuilderPool.Add(builder);
        }
    }

    private static int CountWords(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return 0;
        }

        return normalizedText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while (true)
        {
            var found = text.IndexOf(value, index, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            count++;
            index = found + value.Length;
            if (index >= text.Length)
            {
                break;
            }
        }

        return count;
    }

    private static List<TranscriptChunk> SplitTranscriptIntoChunks(string transcript, int chunkSize, int overlap)
    {
        var chunks = new List<TranscriptChunk>();
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return chunks;
        }

        var length = transcript.Length;
        if (length <= chunkSize)
        {
            chunks.Add(new TranscriptChunk(transcript, 0));
            return chunks;
        }

        var start = 0;
        while (start < length)
        {
            var end = Math.Min(start + chunkSize, length);
            if (end < length)
            {
                var split = transcript.LastIndexOf(' ', end);
                if (split > start + (chunkSize / 2))
                {
                    end = split;
                }
            }

            if (end <= start)
            {
                end = Math.Min(start + chunkSize, length);
            }

            var chunkText = transcript[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(new TranscriptChunk(chunkText, start));
            }

            if (end >= length)
            {
                break;
            }

            start = Math.Max(0, end - overlap);
        }

        return chunks;
    }

    private static int GetFallbackChunkSize(string normalizedTranscript)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript))
        {
            return ChapterChunkSize;
        }

        var length = normalizedTranscript.Length;
        var target = Math.Max(900, length / 3);
        return Math.Clamp(target, 900, ChapterChunkSize);
    }

    private static int GetTopicPosition(string lowerTranscript, ChapterTopic topic, int fallbackPosition)
    {
        if (!string.IsNullOrWhiteSpace(topic.AnchorText))
        {
            var anchorIdx = lowerTranscript.IndexOf(topic.AnchorText.Trim().ToLowerInvariant(), StringComparison.Ordinal);
            if (anchorIdx >= 0)
            {
                return anchorIdx;
            }
        }

        var keywords = topic.Keywords ?? Array.Empty<string>();
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            var idx = lowerTranscript.IndexOf(keyword.Trim().ToLowerInvariant(), StringComparison.Ordinal);
            if (idx >= 0)
            {
                return idx;
            }
        }

        if (!string.IsNullOrWhiteSpace(topic.Title))
        {
            var titleIdx = lowerTranscript.IndexOf(topic.Title.Trim().ToLowerInvariant(), StringComparison.Ordinal);
            if (titleIdx >= 0)
            {
                return titleIdx;
            }
        }

        return fallbackPosition;
    }

    private static bool IsDuplicateChapter(
        IReadOnlyList<ChapterWithPosition> existing,
        ChapterTopic candidate,
        int candidatePosition)
    {
        if (existing.Count == 0)
        {
            return false;
        }

        var title = candidate.Title?.Trim();
        var normalizedTitle = NormalizeTitleForComparison(title);
        var candidateKeywords = NormalizeKeywords(candidate.Keywords);
        var proximityThreshold = Math.Max(ChapterChunkOverlap * 3, 800);

        foreach (var item in existing)
        {
            var distance = Math.Abs(candidatePosition - item.Position);
            if (distance > proximityThreshold)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                var existingTitle = item.Topic.Title?.Trim();
                if (string.Equals(existingTitle, title, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var existingNormalized = NormalizeTitleForComparison(existingTitle);
                if (!string.IsNullOrWhiteSpace(normalizedTitle)
                    && !string.IsNullOrWhiteSpace(existingNormalized))
                {
                    var similarity = TextCleaningUtility.CalculateSimilarity(normalizedTitle, existingNormalized);
                    if (similarity >= 0.78)
                    {
                        return true;
                    }
                }
            }

            if (candidateKeywords.Count > 0)
            {
                var existingKeywords = NormalizeKeywords(item.Topic.Keywords);
                if (existingKeywords.Count > 0)
                {
                    var overlap = KeywordOverlapRatio(candidateKeywords, existingKeywords);
                    if (overlap >= 0.6)
                    {
                        return true;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.AnchorText))
        {
            var anchor = NormalizeForMatch(candidate.AnchorText);
            foreach (var item in existing)
            {
                if (string.IsNullOrWhiteSpace(item.Topic.AnchorText))
                {
                    continue;
                }

                var distance = Math.Abs(candidatePosition - item.Position);
                if (distance > proximityThreshold)
                {
                    continue;
                }

                if (NormalizeForMatch(item.Topic.AnchorText) == anchor)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<ChapterWithPosition> MergeChapters(
        IReadOnlyList<ChapterOrder> ordered,
        bool aggressive)
    {
        var merged = new List<ChapterWithPosition>();

        foreach (var item in ordered)
        {
            var current = item.Topic;
            var position = item.Position;

            var isDuplicate = aggressive
                ? IsDuplicateChapter(merged, current, position)
                : IsExactTitleDuplicate(merged, current);

            if (isDuplicate)
            {
                continue;
            }

            merged.Add(new ChapterWithPosition(current, position));
        }

        return merged;
    }

    private static bool IsExactTitleDuplicate(IReadOnlyList<ChapterWithPosition> existing, ChapterTopic candidate)
    {
        if (existing.Count == 0)
        {
            return false;
        }

        var title = candidate.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        foreach (var item in existing)
        {
            if (string.Equals(item.Topic.Title, title, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTitleForComparison(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        return TextCleaningUtility.NormalizeForComparison(title);
    }

    private static List<string> NormalizeKeywords(IReadOnlyList<string>? keywords)
    {
        if (keywords is null || keywords.Count == 0)
        {
            return new List<string>();
        }

        return keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => TextCleaningUtility.NormalizeForComparison(k))
            .Where(k => k.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static double KeywordOverlapRatio(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0d;
        }

        var setA = a.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var setB = b.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var intersection = setA.Intersect(setB, StringComparer.OrdinalIgnoreCase).Count();
        var minCount = Math.Min(setA.Count, setB.Count);
        return minCount > 0 ? (double)intersection / minCount : 0d;
    }

    private static string CollapseMultiTopicTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var splitters = new[] { " / ", "/", "|", " /", "/ " };
        foreach (var splitter in splitters)
        {
            var idx = title.IndexOf(splitter, StringComparison.Ordinal);
            if (idx > 0)
            {
                return title[..idx].Trim();
            }
        }

        return title.Trim();
    }

    private sealed record TranscriptChunk(string Text, int StartIndex);
    private sealed record ChunkTopic(ChapterTopic Topic, int StartIndex);
    private sealed record ChapterWithPosition(ChapterTopic Topic, int Position);
    private sealed record ChapterOrder(ChapterTopic Topic, int Position, int Order);

    private static bool IsChapterHeaderLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        var cleaned = TextCleaningUtility.CleanTitleLine(line);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return true;
        }

        var trimmed = cleaned.Trim();
        var normalized = trimmed.TrimEnd(':').ToLowerInvariant();

        var headers = new[]
        {
            "hauptpunkte",
            "kapitel",
            "kapitelmarker",
            "themen",
            "themenbereiche",
            "topics",
            "outline",
            "gliederung",
            "abschnitte",
            "sektionen"
        };

        foreach (var header in headers)
        {
            if (normalized == header || normalized.StartsWith(header + " ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParsePrefixedValue(string line, IReadOnlyList<string> prefixes, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (line.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase))
            {
                value = line[(prefix.Length + 1)..].Trim();
                return true;
            }
        }

        return false;
    }

    private static List<string> ExtractKeywords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<string>();
        }

        var separators = new[] { ',', ';', '|', '/', '\\', '\t' };
        var raw = value
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TextCleaningUtility.RemoveQuotes)
            .Select(TextCleaningUtility.RemoveNumberPrefix)
            .Select(k => k.Trim())
            .Where(k => k.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        return raw;
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

    private static string StripListPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.TrimStart();

        if (NumberedListRegex().IsMatch(trimmed))
        {
            trimmed = NumberedListRegex().Replace(trimmed, string.Empty).TrimStart();
        }
        else if (BulletListRegex().IsMatch(trimmed))
        {
            trimmed = BulletListRegex().Replace(trimmed, string.Empty).TrimStart();
        }

        return trimmed;
    }

    [GeneratedRegex(@"^\d+[\.\)]\s*")]
    private static partial Regex NumberedListRegex();

    [GeneratedRegex(@"^[-\*\u2022]\s*")]
    private static partial Regex BulletListRegex();
}


