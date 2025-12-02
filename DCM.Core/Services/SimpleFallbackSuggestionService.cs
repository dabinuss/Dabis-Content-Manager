using System.IO;
using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Einfacher Fallback-Service, der regelbasierte Vorschläge liefert,
/// wenn kein LLM verfügbar ist.
/// </summary>
public sealed class SimpleFallbackSuggestionService : IFallbackSuggestionService
{
    public Task<IReadOnlyList<string>> SuggestTitlesAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var suggestions = new List<string>();

        // Dateiname als Basis
        var fileName = string.Empty;
        if (!string.IsNullOrWhiteSpace(project.VideoFilePath))
        {
            try
            {
                fileName = Path.GetFileNameWithoutExtension(project.VideoFilePath);
            }
            catch
            {
                // Fehler ignorieren
            }
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            // Einfache Formatierung des Dateinamens
            var cleaned = fileName
                .Replace("_", " ")
                .Replace("-", " ")
                .Replace(".", " ")
                .Trim();

            suggestions.Add(cleaned);

            // Variante mit Emoji
            if (!string.IsNullOrWhiteSpace(persona.ContentType) &&
                persona.ContentType.Contains("Gaming", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add($"🎮 {cleaned}");
            }

            // Variante mit Channel-Name
            if (!string.IsNullOrWhiteSpace(persona.ChannelName))
            {
                suggestions.Add($"{cleaned} | {persona.ChannelName}");
            }
        }

        // Fallback wenn nichts gefunden
        if (suggestions.Count == 0)
        {
            suggestions.Add("[Kein Titel aus Dateiname ableitbar]");
        }

        return Task.FromResult<IReadOnlyList<string>>(suggestions);
    }

    public Task<string?> SuggestDescriptionAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(project.Title))
        {
            parts.Add(project.Title);
            parts.Add(string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(persona.ChannelName))
        {
            parts.Add($"📺 Kanal: {persona.ChannelName}");
        }

        if (!string.IsNullOrWhiteSpace(persona.ContentType))
        {
            parts.Add($"🎬 Content: {persona.ContentType}");
        }

        if (project.Tags.Count > 0)
        {
            parts.Add(string.Empty);
            parts.Add($"Tags: {string.Join(", ", project.Tags)}");
        }

        parts.Add(string.Empty);
        parts.Add("[Beschreibung wurde automatisch generiert - bitte anpassen]");

        return Task.FromResult<string?>(string.Join("\n", parts));
    }

    public Task<IReadOnlyList<string>> SuggestTagsAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tags = new List<string>();

        // Tags aus Persona ableiten
        if (!string.IsNullOrWhiteSpace(persona.ContentType))
        {
            var contentWords = persona.ContentType
                .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            tags.AddRange(contentWords.Take(3));
        }

        if (!string.IsNullOrWhiteSpace(persona.ChannelName))
        {
            tags.Add(persona.ChannelName);
        }

        // Sprache als Tag
        if (!string.IsNullOrWhiteSpace(persona.Language))
        {
            if (persona.Language.Contains("de", StringComparison.OrdinalIgnoreCase))
            {
                tags.Add("deutsch");
                tags.Add("german");
            }
            else if (persona.Language.Contains("en", StringComparison.OrdinalIgnoreCase))
            {
                tags.Add("english");
            }
        }

        // Dateiname-basierte Tags
        if (!string.IsNullOrWhiteSpace(project.VideoFilePath))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(project.VideoFilePath);
                var words = fileName
                    .Replace("_", " ")
                    .Replace("-", " ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(w => w.Length > 2)
                    .Take(5);
                tags.AddRange(words);
            }
            catch
            {
                // Fehler ignorieren
            }
        }

        // Duplikate entfernen
        var uniqueTags = tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (uniqueTags.Count == 0)
        {
            uniqueTags.Add("[Keine Tags ableitbar]");
        }

        return Task.FromResult<IReadOnlyList<string>>(uniqueTags);
    }
}