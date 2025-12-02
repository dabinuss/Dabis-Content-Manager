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
public sealed class ContentSuggestionService : IContentSuggestionService
{
    private readonly ILlmClient _llmClient;
    private readonly IFallbackSuggestionService _fallbackSuggestionService;
    private readonly LlmSettings _settings;
    private readonly Random _random = new();
    private readonly object _initLock = new();

    private const int MaxTranscriptCharsForDescription = 4000;
    private const int MaxTranscriptCharsForTags = 2000;
    private const int MaxTranscriptCharsForTitles = 1500;

    // Minimum-Anforderung: Transkript muss vorhanden sein (nicht nur Titel!)
    private const int MinimumTranscriptLength = 50;

    // Variationswörter für unterschiedliche Generierungen
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

    /// <summary>
    /// Prüft ob LLM verfügbar ist und initialisiert es bei Bedarf.
    /// Thread-safe durch internes Locking.
    /// </summary>
    private bool EnsureLlmReady()
    {
        if (!_settings.IsLocalMode)
        {
            return false;
        }

        // Schneller Check ohne Lock
        if (_llmClient.IsReady)
        {
            return true;
        }

        // Thread-safe Initialisierung
        lock (_initLock)
        {
            // Double-check nach Lock
            if (_llmClient.IsReady)
            {
                return true;
            }

            return _llmClient.TryInitialize();
        }
    }

    /// <summary>
    /// Prüft ob ein TRANSKRIPT vorhanden ist.
    /// OHNE Transkript wird IMMER der Fallback verwendet.
    /// Der Titel allein reicht NICHT aus - das LLM würde nur halluzinieren.
    /// </summary>
    private static bool HasTranscript(UploadProject project)
    {
        if (string.IsNullOrWhiteSpace(project.TranscriptText))
        {
            return false;
        }

        var trimmed = project.TranscriptText.Trim();

        // Muss echten Inhalt haben, nicht nur Whitespace oder kurze Fragmente
        return trimmed.Length >= MinimumTranscriptLength;
    }

    /// <summary>
    /// Generiert einen zufälligen Variations-Seed für unterschiedliche Outputs.
    /// </summary>
    private string GetRandomVariation(string[] options)
    {
        return options[_random.Next(options.Length)];
    }

    /// <summary>
    /// Generiert eine zufällige Session-ID für Cache-Busting.
    /// </summary>
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
        // STRIKTE Prüfung: Ohne Transkript -> Fallback
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

            if (string.IsNullOrWhiteSpace(response) || IsErrorResponse(response))
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

        // Custom-Anweisung als Kontext, NICHT als direkte Anweisung
        if (!string.IsNullOrWhiteSpace(_settings.TitleCustomPrompt))
        {
            sb.AppendLine($"Beachte: {SanitizeCustomPrompt(_settings.TitleCustomPrompt)}");
            sb.AppendLine();
        }

        sb.AppendLine("Generiere 3 neue, unterschiedliche Titel basierend auf diesem Transkript:");
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT START---");
        sb.Append(TruncateTranscript(project.TranscriptText!, MaxTranscriptCharsForTitles));
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT ENDE---");
        sb.AppendLine("<|end|>");
        sb.AppendLine("<|assistant|>");

        return sb.ToString();
    }

    private List<string> ParseTitleResponse(string response)
    {
        // Extrahiere die Custom-Prompt-Wörter um sie zu filtern
        var customPromptWords = ExtractFilterWords(_settings.TitleCustomPrompt);

        return response
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !IsMetaLine(line))
            .Where(line => !ContainsPromptLeakage(line, customPromptWords))
            .Where(line => line.Length > 5 && line.Length < 150)
            .Select(CleanTitleLine)
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
        // STRIKTE Prüfung: Ohne Transkript -> Fallback
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

            if (string.IsNullOrWhiteSpace(response) || IsErrorResponse(response))
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

            // Prüfe auf Prompt-Leakage
            var customPromptWords = ExtractFilterWords(_settings.DescriptionCustomPrompt);
            if (ContainsPromptLeakage(cleaned, customPromptWords))
            {
                System.Diagnostics.Debug.WriteLine("[ContentSuggestion] Beschreibung: Prompt-Leakage erkannt -> Fallback");
                return await _fallbackSuggestionService.SuggestDescriptionAsync(project, persona, cancellationToken);
            }

            // Prüfe ob die Beschreibung nur eine Kopie des Titels ist
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

        // Custom-Anweisung als Kontext, NICHT als direkte Anweisung
        if (!string.IsNullOrWhiteSpace(_settings.DescriptionCustomPrompt))
        {
            sb.AppendLine($"Beachte: {SanitizeCustomPrompt(_settings.DescriptionCustomPrompt)}");
            sb.AppendLine();
        }

        // Aktuellen Titel angeben, damit das LLM weiß was es NICHT wiederholen soll
        if (!string.IsNullOrWhiteSpace(project.Title))
        {
            sb.AppendLine($"Der Videotitel lautet: \"{project.Title}\"");
            sb.AppendLine("WIEDERHOLE DIESEN TITEL NICHT in der Beschreibung!");
            sb.AppendLine();
        }

        sb.AppendLine("Schreibe eine neue Beschreibung die den VIDEO-INHALT zusammenfasst basierend auf diesem Transkript:");
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT START---");
        sb.Append(TruncateTranscript(project.TranscriptText!, MaxTranscriptCharsForDescription));
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT ENDE---");
        sb.AppendLine("<|end|>");
        sb.AppendLine("<|assistant|>");

        return sb.ToString();
    }

    /// <summary>
    /// Prüft ob die generierte Beschreibung nur eine Wiederholung/Variation des Titels ist.
    /// </summary>
    private static bool IsDescriptionJustTitle(string description, UploadProject project)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(project.Title))
        {
            return false;
        }

        var descClean = NormalizeForComparison(description);
        var titleClean = NormalizeForComparison(project.Title);

        // Exakte Übereinstimmung
        if (descClean == titleClean)
        {
            return true;
        }

        // Beschreibung ist zu kurz im Vergleich zum Titel (wahrscheinlich nur Titel)
        if (descClean.Length <= titleClean.Length * 1.3)
        {
            // Prüfe auf hohe Ähnlichkeit
            var similarity = CalculateSimilarity(descClean, titleClean);
            if (similarity > 0.7) // 70% ähnlich
            {
                return true;
            }
        }

        // Beschreibung beginnt exakt mit dem Titel
        if (descClean.StartsWith(titleClean))
        {
            // Nur akzeptabel wenn danach noch substantieller Inhalt kommt
            var remainder = descClean[titleClean.Length..].Trim();
            if (remainder.Length < 50)
            {
                return true;
            }
        }

        // Titel ist komplett in der Beschreibung enthalten und macht >50% aus
        if (descClean.Contains(titleClean))
        {
            var ratio = (double)titleClean.Length / descClean.Length;
            if (ratio > 0.5)
            {
                return true;
            }
        }

        // Prüfe auf typische "Titel als Beschreibung"-Muster
        var descLower = description.ToLowerInvariant();
        if (descLower.StartsWith("in diesem video") ||
            descLower.StartsWith("dieses video") ||
            descLower.StartsWith("video über"))
        {
            // Diese Phrasen sind okay, aber prüfe ob danach nur Titel kommt
            var afterPhrase = Regex.Replace(descLower, @"^(in diesem video|dieses video|video über)[:\s]*", "").Trim();
            var titleLower = project.Title.ToLowerInvariant();

            if (CalculateSimilarity(afterPhrase, titleLower) > 0.7)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalisiert Text für Vergleiche (lowercase, keine Sonderzeichen, keine mehrfachen Leerzeichen).
    /// </summary>
    private static string NormalizeForComparison(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Lowercase
        var normalized = text.ToLowerInvariant();

        // Nur Buchstaben, Zahlen und Leerzeichen behalten
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}\s]", " ");

        // Mehrfache Leerzeichen zu einem
        normalized = Regex.Replace(normalized, @"\s+", " ");

        return normalized.Trim();
    }

    /// <summary>
    /// Berechnet die Ähnlichkeit zwischen zwei Strings (0.0 - 1.0).
    /// Verwendet eine einfache Wort-Überlappungs-Metrik.
    /// </summary>
    private static double CalculateSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return 0.0;
        }

        var wordsA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wordsB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (wordsA.Count == 0 || wordsB.Count == 0)
        {
            return 0.0;
        }

        var intersection = wordsA.Intersect(wordsB, StringComparer.OrdinalIgnoreCase).Count();
        var union = wordsA.Union(wordsB, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)intersection / union : 0.0;
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
            .Where(l => !IsMetaLine(l))
            .Where(l => !l.StartsWith("Beschreibung:", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("YouTube-Beschreibung:", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("Hier ist", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("Hier die", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("Die Beschreibung", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = string.Join("\n", lines).Trim();

        // Anführungszeichen am Anfang/Ende entfernen
        result = RemoveQuotes(result);

        // Wenn die erste Zeile exakt der Titel ist, entferne sie
        if (!string.IsNullOrWhiteSpace(project.Title))
        {
            result = RemoveTitleFromStart(result, project.Title);
        }

        return result;
    }

    /// <summary>
    /// Entfernt den Titel vom Anfang der Beschreibung, falls vorhanden.
    /// </summary>
    private static string RemoveTitleFromStart(string description, string title)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(title))
        {
            return description;
        }

        var descNorm = NormalizeForComparison(description);
        var titleNorm = NormalizeForComparison(title);

        // Prüfe ob die Beschreibung mit dem Titel beginnt
        if (!descNorm.StartsWith(titleNorm))
        {
            return description;
        }

        // Finde die Position wo der Titel endet
        // Wir müssen im Original-String arbeiten, nicht im normalisierten
        var lines = description.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > 0)
        {
            var firstLineNorm = NormalizeForComparison(lines[0]);
            var similarity = CalculateSimilarity(firstLineNorm, titleNorm);

            if (similarity > 0.8) // Erste Zeile ist sehr ähnlich zum Titel
            {
                // Entferne die erste Zeile
                var remainingLines = lines.Skip(1).ToArray();
                var result = string.Join("\n", remainingLines).Trim();

                // Nur zurückgeben wenn noch genug Inhalt übrig ist
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
        // STRIKTE Prüfung: Ohne Transkript -> Fallback
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

            if (string.IsNullOrWhiteSpace(response) || IsErrorResponse(response))
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

        // Custom-Anweisung als Kontext
        if (!string.IsNullOrWhiteSpace(_settings.TagsCustomPrompt))
        {
            sb.AppendLine($"Beachte: {SanitizeCustomPrompt(_settings.TagsCustomPrompt)}");
            sb.AppendLine();
        }

        sb.AppendLine("Generiere neue, einzelne Wörter als Tags basierend auf diesem Transkript:");
        sb.AppendLine();
        sb.AppendLine("---TRANSKRIPT START---");
        sb.Append(TruncateTranscript(project.TranscriptText!, MaxTranscriptCharsForTags));
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

        // Custom-Prompt-Wörter extrahieren zum Filtern
        var customPromptWords = ExtractFilterWords(_settings.TagsCustomPrompt);

        // Alle möglichen Trennzeichen
        var separators = new[] { ',', '\n', '\r', ';', '|' };

        var rawTags = response
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var cleanedTags = new List<string>();

        foreach (var rawTag in rawTags)
        {
            // Jedes potentielle "Tag" nochmal auf Leerzeichen/Unterstriche splitten
            var singleWords = ExtractSingleWords(rawTag);
            cleanedTags.AddRange(singleWords);
        }

        return cleanedTags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Where(t => !IsMetaLine(t))
            .Where(t => !IsPromptLeakageWord(t, customPromptWords))
            .Where(t => !t.StartsWith("Tags:", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Equals("Keine", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Equals("Session", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Equals("Fokus", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Contains("ableitbar", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Contains("vorhanden", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Contains("transkript", StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Length >= 2 && t.Length <= 30)
            .Where(IsValidSingleWordTag)
            .Select(t => t.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
    }

    /// <summary>
    /// Extrahiert einzelne Wörter aus einem String.
    /// Splittet auf Leerzeichen, Unterstriche, Bindestriche etc.
    /// </summary>
    private static IEnumerable<string> ExtractSingleWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            yield break;
        }

        var cleaned = input.Trim();

        // Entferne führende Nummern, Hashtags, Aufzählungszeichen
        cleaned = CleanTagPrefix(cleaned);

        // Entferne Anführungszeichen
        cleaned = RemoveQuotes(cleaned);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            yield break;
        }

        // Splitten auf alle Nicht-Buchstaben (außer Umlaute)
        var wordSeparators = new[] { ' ', '_', '-', '/', '\\', ':', ';', '.', '!', '?' };
        var words = cleaned.Split(wordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var word in words)
        {
            var trimmedWord = word.Trim();

            // Nur Buchstaben und evtl. Zahlen erlauben
            if (!string.IsNullOrWhiteSpace(trimmedWord) && IsValidSingleWordTag(trimmedWord))
            {
                yield return trimmedWord;
            }
        }
    }

    /// <summary>
    /// Prüft ob ein Tag ein gültiges einzelnes Wort ist.
    /// </summary>
    private static bool IsValidSingleWordTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        // Muss mindestens 2 Zeichen haben
        if (tag.Length < 2)
        {
            return false;
        }

        // Darf keine Leerzeichen, Unterstriche oder Bindestriche enthalten
        if (tag.Contains(' ') || tag.Contains('_') || tag.Contains('-'))
        {
            return false;
        }

        // Muss hauptsächlich aus Buchstaben bestehen (inkl. Umlaute)
        var letterCount = tag.Count(c => char.IsLetter(c));
        return letterCount >= tag.Length * 0.5; // Mindestens 50% Buchstaben
    }

    /// <summary>
    /// Entfernt führende Nummerierung, Hashtags, Aufzählungszeichen von einem Tag.
    /// </summary>
    private static string CleanTagPrefix(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var cleaned = tag.Trim();

        // Hashtags entfernen
        while (cleaned.StartsWith('#'))
        {
            cleaned = cleaned[1..].TrimStart();
        }

        // Aufzählungszeichen entfernen
        cleaned = cleaned.TrimStart('*', '-', '•', '→', '►').TrimStart();

        // Nummerierung entfernen (1. 2. 1) 2) etc.)
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

        return cleaned;
    }

    #endregion

    #region Prompt Leakage Prevention

    /// <summary>
    /// Bereinigt Custom-Prompts um Injection zu verhindern.
    /// </summary>
    private static string SanitizeCustomPrompt(string? customPrompt)
    {
        if (string.IsNullOrWhiteSpace(customPrompt))
        {
            return string.Empty;
        }

        // Entferne potentielle Prompt-Injection-Versuche
        var sanitized = customPrompt
            .Replace("<|", "")
            .Replace("|>", "")
            .Replace("system", "")
            .Replace("user", "")
            .Replace("assistant", "")
            .Replace("---", "")
            .Trim();

        // Maximal 200 Zeichen
        if (sanitized.Length > 200)
        {
            sanitized = sanitized[..200];
        }

        return sanitized;
    }

    /// <summary>
    /// Extrahiert Wörter aus dem Custom-Prompt die gefiltert werden sollen.
    /// </summary>
    private static HashSet<string> ExtractFilterWords(string? customPrompt)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(customPrompt))
        {
            return words;
        }

        // Häufige Anweisungswörter die nicht im Output erscheinen sollen
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

        // Wörter aus dem Custom-Prompt extrahieren (nur längere, spezifische)
        var promptWords = customPrompt
            .Split(new[] { ' ', ',', '.', ':', ';', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 5)
            .Where(w => char.IsLetter(w[0]));

        foreach (var word in promptWords)
        {
            words.Add(word.ToLowerInvariant());
        }

        return words;
    }

    /// <summary>
    /// Prüft ob ein Text Prompt-Leakage enthält.
    /// </summary>
    private static bool ContainsPromptLeakage(string text, HashSet<string> filterWords)
    {
        if (string.IsNullOrWhiteSpace(text) || filterWords.Count == 0)
        {
            return false;
        }

        var lower = text.ToLowerInvariant();

        // Typische Leakage-Muster
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

        // Session-ID Leakage
        if (Regex.IsMatch(lower, @"session[:\s]*[a-f0-9]{8}"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Prüft ob ein einzelnes Wort ein Prompt-Leakage-Wort ist.
    /// </summary>
    private static bool IsPromptLeakageWord(string word, HashSet<string> filterWords)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        return filterWords.Contains(word.ToLowerInvariant());
    }

    #endregion

    #region Shared Helper Methods

    /// <summary>
    /// Prüft ob eine Response eine Fehler- oder Meta-Antwort ist.
    /// </summary>
    private static bool IsErrorResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return true;
        }

        var lower = response.ToLowerInvariant();

        // Fehlermuster erkennen
        if (response.StartsWith("[") || response.StartsWith("<"))
        {
            return true;
        }

        if (lower.Contains("kein transkript") ||
            lower.Contains("keine informationen") ||
            lower.Contains("nicht möglich") ||
            lower.Contains("nicht verfügbar") ||
            lower.Contains("nicht vorhanden") ||
            lower.Contains("llm") ||
            lower.Contains("error") ||
            lower.Contains("fehler"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Prüft ob eine Zeile Meta-Inhalt ist (keine echte Antwort).
    /// </summary>
    private static bool IsMetaLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        var trimmed = line.Trim();

        if (trimmed.StartsWith('[') || trimmed.StartsWith('<') || trimmed.StartsWith('#'))
        {
            return true;
        }

        if (trimmed.StartsWith("---"))
        {
            return true;
        }

        var lower = trimmed.ToLowerInvariant();

        if (lower.StartsWith("titel") ||
            lower.StartsWith("beschreibung") ||
            lower.StartsWith("tags") ||
            lower.StartsWith("hinweis") ||
            lower.StartsWith("anmerkung") ||
            lower.StartsWith("note") ||
            lower.StartsWith("session") ||
            lower.StartsWith("stil") ||
            lower.StartsWith("fokus") ||
            lower.StartsWith("beachte") ||
            lower.StartsWith("transkript"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Kürzt das Transkript auf die angegebene Länge.
    /// </summary>
    private static string TruncateTranscript(string transcript, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var trimmed = transcript.Trim();

        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..maxLength] + " [...]";
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
        cleaned = cleaned.TrimStart('*', '-', '•', '→', '►').TrimStart();

        return cleaned;
    }

    private static string RemoveQuotes(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Alle Arten von Anführungszeichen
        var quoteChars = new[] { '"', '\'', '\u201E', '\u201C', '\u201A', '\u2018', '\u201D', '\u2019', '„', '"', '"', '»', '«', '›', '‹' };

        var result = text;

        while (result.Length > 0 && quoteChars.Contains(result[0]))
        {
            result = result[1..];
        }

        while (result.Length > 0 && quoteChars.Contains(result[^1]))
        {
            result = result[..^1];
        }

        return result.Trim();
    }

    #endregion
}