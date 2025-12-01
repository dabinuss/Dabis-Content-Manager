using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DCM.Core.Models;

namespace DCM.Core.Services;

public sealed class SimpleContentSuggestionService : IContentSuggestionService
{
    public Task<IReadOnlyList<string>> SuggestTitlesAsync(UploadProject project, ChannelPersona persona)
    {
        var result = new List<string>();

        var fileNameBase = GetBaseTitleFromFile(project.VideoFilePath);
        var baseTitle = string.IsNullOrWhiteSpace(project.Title)
            ? fileNameBase
            : project.Title.Trim();

        if (string.IsNullOrWhiteSpace(baseTitle))
        {
            baseTitle = "Neues Video";
        }

        // 1. Basis
        result.Add(baseTitle);

        // 2. Highlights / Best Moments
        result.Add($"{baseTitle} | Highlights");
        result.Add($"{baseTitle} – Best Moments");

        // Optionaler ToneOfVoice-Schlenker (z. B. „– sarkastische Highlights“)
        if (!string.IsNullOrWhiteSpace(persona.ToneOfVoice))
        {
            var tone = persona.ToneOfVoice.Trim();
            result.Add($"{baseTitle} – {tone} Highlights");
        }

        // Auf 3–4 sinnvolle Titel reduzieren & duplikate entfernen
        var distinct = result
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        if (distinct.Count == 0)
        {
            distinct.Add("Neues Video");
        }

        return Task.FromResult<IReadOnlyList<string>>(distinct);
    }

    public Task<string?> SuggestDescriptionAsync(UploadProject project, ChannelPersona persona)
    {
        var lines = new List<string>();

        var baseTitle = !string.IsNullOrWhiteSpace(project.Title)
            ? project.Title.Trim()
            : GetBaseTitleFromFile(project.VideoFilePath);

        if (!string.IsNullOrWhiteSpace(baseTitle))
        {
            lines.Add(baseTitle);
            lines.Add(string.Empty);
        }

        // Optional: ContentType / Kanalinfo
        if (!string.IsNullOrWhiteSpace(persona.ContentType) ||
            !string.IsNullOrWhiteSpace(persona.TargetAudience))
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(persona.ContentType))
            {
                parts.Add(persona.ContentType.Trim());
            }

            if (!string.IsNullOrWhiteSpace(persona.TargetAudience))
            {
                parts.Add($"für {persona.TargetAudience.Trim()}");
            }

            lines.Add(string.Join(" – ", parts));
            lines.Add(string.Empty);
        }

        // Transcript: erste 1–3 „Lesbare“ Zeilen
        if (!string.IsNullOrWhiteSpace(project.TranscriptText))
        {
            var raw = project.TranscriptText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);

            var transcriptLines = raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length > 0)
                .Take(3);

            lines.AddRange(transcriptLines);
        }

        if (lines.Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        var text = string.Join(Environment.NewLine, lines);
        return Task.FromResult<string?>(text);
    }

    public Task<IReadOnlyList<string>> SuggestTagsAsync(UploadProject project, ChannelPersona persona)
    {
        var tags = new List<string>();

        // Dateiname tokenisieren
        var baseName = GetBaseTitleFromFile(project.VideoFilePath);
        if (!string.IsNullOrWhiteSpace(baseName))
        {
            foreach (var token in Tokenize(baseName))
            {
                tags.Add(token);
            }
        }

        // Titel ebenfalls tokenisieren
        if (!string.IsNullOrWhiteSpace(project.Title))
        {
            foreach (var token in Tokenize(project.Title))
            {
                tags.Add(token);
            }
        }

        // 2–3 Standardtags aus ContentType / TargetAudience
        if (!string.IsNullOrWhiteSpace(persona.ContentType))
        {
            foreach (var token in Tokenize(persona.ContentType))
            {
                tags.Add(token);
            }
        }

        if (!string.IsNullOrWhiteSpace(persona.TargetAudience))
        {
            foreach (var token in Tokenize(persona.TargetAudience))
            {
                tags.Add(token);
            }
        }

        var distinct = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(distinct);
    }

    private static string GetBaseTitleFromFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName ?? string.Empty;
    }

    private static IEnumerable<string> Tokenize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            yield break;

        // Trenner: Leerzeichen, -, _
        var rough = input
            .Replace('-', ' ')
            .Replace('_', ' ');

        foreach (var part in rough.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleaned = NormalizeToken(part);
            if (!string.IsNullOrWhiteSpace(cleaned))
                yield return cleaned;
        }
    }

    private static string NormalizeToken(string token)
    {
        // Nur Buchstaben/Ziffern/#, alles kleinschreiben
        var filtered = token
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '#')
            .ToArray();

        if (filtered.Length == 0)
            return string.Empty;

        return new string(filtered).ToLowerInvariant();
    }
}
