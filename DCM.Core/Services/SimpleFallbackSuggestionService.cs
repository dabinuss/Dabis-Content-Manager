using DCM.Core.Logging;
using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Einfacher Fallback-Service, der regelbasierte Vorschläge liefert,
/// wenn kein LLM verfügbar ist.
/// </summary>
public sealed class SimpleFallbackSuggestionService : IFallbackSuggestionService
{
    private readonly IAppLogger _logger;

    public SimpleFallbackSuggestionService(IAppLogger? logger = null)
    {
        _logger = logger ?? AppLogger.Instance;
    }

    public Task<IReadOnlyList<string>> SuggestTitlesAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.Debug("Fallback-Titelgenerierung gestartet", "FallbackSuggestion");

        var suggestions = new List<string>();

        // Dateiname als Basis
        var fileName = string.Empty;
        if (!string.IsNullOrWhiteSpace(project.VideoFilePath))
        {
            try
            {
                fileName = Path.GetFileNameWithoutExtension(project.VideoFilePath);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fehler beim Extrahieren des Dateinamens: {ex.Message}", "FallbackSuggestion");
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
            _logger.Warning("Keine Titelvorschläge aus Dateiname ableitbar", "FallbackSuggestion");
        }
        else
        {
            _logger.Debug($"Fallback-Titel generiert: {suggestions.Count} Vorschläge", "FallbackSuggestion");
        }

        return Task.FromResult<IReadOnlyList<string>>(suggestions);
    }

    public Task<IReadOnlyList<string>> SuggestDescriptionAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.Debug("Fallback-Beschreibungsgenerierung gestartet", "FallbackSuggestion");

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(project.Title))
        {
            parts.Add(project.Title);
            parts.Add(string.Empty);
        }

        // Keine weiteren Platzhalter-/Hinweis-Texte hinzufügen – nur Titel übernehmen

        var result = string.Join("\n", parts);
        _logger.Debug($"Fallback-Beschreibung generiert: {result.Length} Zeichen", "FallbackSuggestion");

        return Task.FromResult<IReadOnlyList<string>>(new List<string> { result });
    }

    public Task<IReadOnlyList<string>> SuggestTagsAsync(
        UploadProject project,
        ChannelPersona persona,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.Debug("Fallback-Taggenerierung gestartet", "FallbackSuggestion");

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
            catch (Exception ex)
            {
                _logger.Warning($"Fehler beim Extrahieren von Tags aus Dateiname: {ex.Message}", "FallbackSuggestion");
            }
        }

        // Duplikate entfernen
        var uniqueTags = tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (uniqueTags.Count == 0)
        {
            uniqueTags.Add("[Keine Tags ableitbar]");
            _logger.Warning("Keine Fallback-Tags ableitbar", "FallbackSuggestion");
        }
        else
        {
            _logger.Debug($"Fallback-Tags generiert: {uniqueTags.Count} Tags", "FallbackSuggestion");
        }

        return Task.FromResult<IReadOnlyList<string>>(uniqueTags);
    }
}
