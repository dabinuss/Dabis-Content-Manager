using System.Text;
using System.Text.RegularExpressions;

namespace DCM.Transcription.PostProcessing;

/// <summary>
/// Verarbeitet Rohtranskriptionen für bessere Lesbarkeit.
/// Führt nur sichere Transformationen durch, die den Inhalt nicht verfälschen.
/// </summary>
public sealed partial class TranscriptionPostProcessor
{
    private readonly PostProcessingOptions _options;

    public TranscriptionPostProcessor(PostProcessingOptions? options = null)
    {
        _options = options ?? new PostProcessingOptions();
    }

    /// <summary>
    /// Verarbeitet eine Liste von Transkriptions-Segmenten zu einem formatierten Text.
    /// </summary>
    /// <param name="segments">Die Rohsegmente von Whisper.</param>
    /// <returns>Formatierter Text mit Absätzen.</returns>
    public string Process(IReadOnlyList<TranscriptionSegment> segments)
    {
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var currentParagraph = new StringBuilder();
        TimeSpan? lastSegmentEnd = null;

        foreach (var segment in segments)
        {
            var text = CleanSegmentText(segment.Text);

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Prüfen ob ein neuer Absatz beginnen soll
            if (lastSegmentEnd.HasValue && _options.InsertParagraphs)
            {
                var pauseDuration = segment.Start - lastSegmentEnd.Value;

                if (pauseDuration >= _options.ParagraphPauseThreshold)
                {
                    // Aktuellen Absatz abschließen
                    if (currentParagraph.Length > 0)
                    {
                        builder.AppendLine(currentParagraph.ToString().Trim());
                        builder.AppendLine();
                        currentParagraph.Clear();
                    }
                }
            }

            // Text zum aktuellen Absatz hinzufügen
            if (currentParagraph.Length > 0)
            {
                // Leerzeichen zwischen Segmenten, außer nach Satzzeichen mit Leerzeichen
                var lastChar = currentParagraph[^1];
                if (!char.IsWhiteSpace(lastChar))
                {
                    currentParagraph.Append(' ');
                }
            }

            currentParagraph.Append(text);
            lastSegmentEnd = segment.End;
        }

        // Letzten Absatz hinzufügen
        if (currentParagraph.Length > 0)
        {
            builder.Append(currentParagraph.ToString().Trim());
        }

        var result = builder.ToString();

        // Finale Bereinigung
        result = RemoveWordDuplications(result);
        result = NormalizeWhitespace(result);

        return result.Trim();
    }

    /// <summary>
    /// Verarbeitet einen einfachen Text ohne Segment-Informationen.
    /// </summary>
    /// <param name="text">Der Rohtext.</param>
    /// <returns>Bereinigter Text.</returns>
    public string Process(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text;

        result = RemoveWordDuplications(result);
        result = NormalizeWhitespace(result);

        return result.Trim();
    }

    private static string CleanSegmentText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Führende/trailing Whitespace entfernen
        return text.Trim();
    }

    private string RemoveWordDuplications(string text)
    {
        if (!_options.RemoveWordDuplications)
        {
            return text;
        }

        // Pattern: Wort gefolgt von Leerzeichen und demselben Wort (case-insensitive)
        // Erfasst auch mehrfache Wiederholungen: "und und und" → "und"
        var result = text;
        string previous;

        do
        {
            previous = result;
            result = WordDuplicationRegex().Replace(result, "$1");
        }
        while (result != previous);

        return result;
    }

    private static string NormalizeWhitespace(string text)
    {
        // Mehrfache Leerzeichen zu einem
        var result = MultipleSpacesRegex().Replace(text, " ");

        // Leerzeichen vor Satzzeichen entfernen
        result = SpaceBeforePunctuationRegex().Replace(result, "$1");

        // Mehrfache Zeilenumbrüche auf maximal zwei reduzieren
        result = MultipleNewlinesRegex().Replace(result, "\n\n");

        return result;
    }

    // Regex für Wortduplikationen: findet "wort wort" (case-insensitive)
    [GeneratedRegex(@"\b(\w+)\s+\1\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WordDuplicationRegex();

    // Regex für mehrfache Leerzeichen
    [GeneratedRegex(@" {2,}", RegexOptions.Compiled)]
    private static partial Regex MultipleSpacesRegex();

    // Regex für Leerzeichen vor Satzzeichen
    [GeneratedRegex(@"\s+([.,!?;:])", RegexOptions.Compiled)]
    private static partial Regex SpaceBeforePunctuationRegex();

    // Regex für mehrfache Zeilenumbrüche
    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex MultipleNewlinesRegex();
}