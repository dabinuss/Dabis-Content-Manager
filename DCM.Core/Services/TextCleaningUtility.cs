using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace DCM.Core.Services;

/// <summary>
/// Utility-Klasse für Text-Bereinigung und -Parsing bei der Content-Generierung.
/// </summary>
public static partial class TextCleaningUtility
{
    private const int NormalizeCacheLimit = 2048;
    private const int NormalizeCacheMaxInputLength = 200;
    private static readonly ConcurrentDictionary<string, string> NormalizeComparisonCache = new(StringComparer.Ordinal);
    private static readonly char[] QuoteChars =
    {
        '"', '\'', '\u201E', '\u201C', '\u201A', '\u2018',
        '\u201D', '\u2019', '„', '"', '"', '»', '«', '›', '‹'
    };

    private static readonly char[] BulletChars = { '*', '-', '•', '→', '►' };

    private static readonly string[] MetaLinePrefixes =
    {
        "titel", "beschreibung", "tags", "hinweis", "anmerkung",
        "note", "session", "stil", "fokus", "beachte", "transkript"
    };

    /// <summary>
    /// Entfernt Anführungszeichen vom Anfang und Ende eines Texts.
    /// </summary>
    public static string RemoveQuotes(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text.Trim();

        while (result.Length >= 2)
        {
            var first = result[0];
            var lastIndex = result.Length - 1;
            var last = result[lastIndex];

            if (!QuoteChars.Contains(first))
            {
                break;
            }

            if (QuoteChars.Contains(last))
            {
                result = result[1..^1].Trim();
                continue;
            }

            if (IsTrailingPunctuation(last) && result.Length >= 3)
            {
                var beforeLast = result[lastIndex - 1];
                if (QuoteChars.Contains(beforeLast))
                {
                    result = result[1..(lastIndex - 1)].Trim();
                    continue;
                }
            }

            break;
        }

        return result;
    }

    /// <summary>
    /// Bereinigt eine Titel-Zeile (entfernt Nummerierung, Aufzählungszeichen, etc.).
    /// </summary>
    public static string CleanTitleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var cleaned = line.Trim();

        // Nummerierung entfernen (1. 2. 1) 2) etc.)
        cleaned = RemoveNumberPrefix(cleaned);

        // Anführungszeichen entfernen
        cleaned = RemoveQuotes(cleaned);

        // Aufzählungszeichen entfernen
        cleaned = cleaned.TrimStart(BulletChars).TrimStart();

        return cleaned;
    }

    /// <summary>
    /// Entfernt umschließende Anführungszeichen aus Titelvorschlägen,
    /// auch wenn nach dem abschließenden Anführungszeichen Emojis/Symbole folgen.
    /// </summary>
    public static string RemoveWrappedTitleQuotes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text.Trim();

        // Führende Anführungszeichen entfernen (auch mehrfach).
        result = result.TrimStart(QuoteChars).TrimStart();

        // Abschließende Anführungszeichen direkt nach dem letzten Wort entfernen.
        var lastLetterDigitIndex = LastIndexOfLetterOrDigit(result);
        if (lastLetterDigitIndex >= 0 && lastLetterDigitIndex + 1 < result.Length)
        {
            var index = lastLetterDigitIndex + 1;
            while (index < result.Length && char.IsWhiteSpace(result[index]))
            {
                index++;
            }

            if (index < result.Length && QuoteChars.Contains(result[index]))
            {
                result = result.Remove(index, 1);
            }
        }

        return result.Trim();
    }

    /// <summary>
    /// Entfernt führende Nummerierung wie "1.", "2)", "3:" etc.
    /// </summary>
    public static string RemoveNumberPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var cleaned = text.Trim();

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

    /// <summary>
    /// Bereinigt einen Tag-Präfix (Hashtags, Nummerierung, Aufzählungszeichen).
    /// </summary>
    public static string CleanTagPrefix(string tag)
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
        cleaned = cleaned.TrimStart(BulletChars).TrimStart();

        // Nummerierung entfernen
        cleaned = RemoveNumberPrefix(cleaned);

        return cleaned;
    }

    /// <summary>
    /// Prüft ob eine Zeile Meta-Inhalt ist (keine echte Antwort).
    /// </summary>
    public static bool IsMetaLine(string line)
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

        foreach (var prefix in MetaLinePrefixes)
        {
            if (lower.StartsWith(prefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Prüft ob eine Response eine Fehler- oder Meta-Antwort ist.
    /// </summary>
    public static bool IsErrorResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return true;
        }

        var lower = response.ToLowerInvariant();

        if (response.StartsWith('[') || response.StartsWith('<'))
        {
            return true;
        }

        var errorPatterns = new[]
        {
            "kein transkript", "keine informationen", "nicht möglich",
            "nicht verfügbar", "nicht vorhanden", "llm", "error", "fehler"
        };

        foreach (var pattern in errorPatterns)
        {
            if (lower.Contains(pattern))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Kürzt ein Transkript auf die angegebene Länge.
    /// </summary>
    public static string TruncateTranscript(string transcript, int maxLength)
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

    /// <summary>
    /// Prüft ob ein Tag ein gültiges einzelnes Wort ist.
    /// </summary>
    public static bool IsValidSingleWordTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

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
        return letterCount >= tag.Length * 0.5;
    }

    /// <summary>
    /// Extrahiert einzelne Wörter aus einem String.
    /// </summary>
    public static IEnumerable<string> ExtractSingleWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            yield break;
        }

        var cleaned = input.Trim();
        cleaned = CleanTagPrefix(cleaned);
        cleaned = RemoveQuotes(cleaned);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            yield break;
        }

        var wordSeparators = new[] { ' ', '_', '-', '/', '\\', ':', ';', '.', '!', '?' };
        var words = cleaned.Split(wordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var word in words)
        {
            var trimmedWord = word.Trim();

            if (!string.IsNullOrWhiteSpace(trimmedWord) && IsValidSingleWordTag(trimmedWord))
            {
                yield return trimmedWord;
            }
        }
    }

    /// <summary>
    /// Normalisiert Text für Vergleiche (lowercase, keine Sonderzeichen).
    /// </summary>
    public static string NormalizeForComparison(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Length <= NormalizeCacheMaxInputLength
            && NormalizeComparisonCache.TryGetValue(text, out var cached))
        {
            return cached;
        }

        var normalized = text.ToLowerInvariant();
        normalized = NonAlphanumericRegex().Replace(normalized, " ");
        normalized = MultipleSpacesRegex().Replace(normalized, " ");
        normalized = normalized.Trim();

        if (text.Length <= NormalizeCacheMaxInputLength)
        {
            if (NormalizeComparisonCache.Count >= NormalizeCacheLimit)
            {
                NormalizeComparisonCache.Clear();
            }

            NormalizeComparisonCache.TryAdd(text, normalized);
        }

        return normalized;
    }

    /// <summary>
    /// Berechnet die Ähnlichkeit zwischen zwei Strings (0.0 - 1.0) mittels Jaccard-Index.
    /// </summary>
    public static double CalculateSimilarity(string a, string b)
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

    /// <summary>
    /// Bereinigt Custom-Prompts um Injection zu verhindern.
    /// </summary>
    public static string SanitizeCustomPrompt(string? customPrompt, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(customPrompt))
        {
            return string.Empty;
        }

        var sanitized = customPrompt
            .Replace("<|", "")
            .Replace("|>", "")
            .Replace("system", "")
            .Replace("user", "")
            .Replace("assistant", "")
            .Replace("---", "")
            .Trim();

        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength];
        }

        return sanitized;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}\s]")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesRegex();

    private static bool IsTrailingPunctuation(char ch)
    {
        return ch == ',' || ch == '.' || ch == ';' || ch == ':' || ch == '!' || ch == '?';
    }

    private static int LastIndexOfLetterOrDigit(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                return i;
            }
        }

        return -1;
    }
}


