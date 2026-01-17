using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DCM.Core.Logging;
using DCM.Core.Models;

namespace DCM.Core.Services;

public sealed partial class TemplateService
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{+\s*(?<name>[A-Za-z0-9_]+)\s*\}+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IAppLogger _logger;

    public TemplateService(IAppLogger? logger = null)
    {
        _logger = logger ?? AppLogger.Instance;
    }

    public string ApplyTemplate(string template, IDictionary<string, string?> values)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        if (values is null) throw new ArgumentNullException(nameof(values));

        var replacementCount = 0;

        var result = PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups["name"].Value.ToUpperInvariant();

            if (values.TryGetValue(key, out var value) && value is not null)
            {
                replacementCount++;
                return value;
            }

            _logger.Debug($"Platzhalter '{key}' nicht gefunden oder leer", "TemplateService");
            return string.Empty;
        });

        _logger.Debug($"Template angewendet: {replacementCount} Platzhalter ersetzt", "TemplateService");
        return result;
    }

    public string ApplyTemplate(string template, UploadProject project)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        _logger.Debug($"Template wird auf Projekt '{project.Title}' angewendet", "TemplateService");

        // Tags kommasepariert
        var tagsCsv = project.Tags is not null && project.Tags.Count > 0
            ? string.Join(", ", project.Tags)
            : string.Empty;

        // HashTags als Hashtags formatiert
        var hashTags = project.Tags is not null && project.Tags.Count > 0
            ? string.Join(" ", project.Tags.Select(t => "#" + SanitizeHashtag(t)))
            : string.Empty;

        var dict = new Dictionary<string, string?>
        {
            ["TITLE"] = project.Title,
            ["DESCRIPTION"] = project.Description ?? string.Empty,
            ["TAGS"] = tagsCsv,
            ["HASHTAGS"] = hashTags,
            ["PLAYLIST"] = project.PlaylistTitle ?? project.PlaylistId,
            ["PLAYLIST_ID"] = project.PlaylistId,
            ["VISIBILITY"] = project.Visibility.ToString(),
            ["PLATFORM"] = project.Platform.ToString(),
            ["CATEGORY"] = project.CategoryId ?? string.Empty,
            ["LANGUAGE"] = project.Language ?? string.Empty,
            ["MADEFORKIDS"] = project.MadeForKids.HasValue
                ? (project.MadeForKids.Value ? "Yes" : "No")
                : string.Empty,
            ["COMMENTSTATUS"] = project.CommentStatus == CommentStatusSetting.Default
                ? string.Empty
                : project.CommentStatus.ToString(),
            ["SCHEDULEDDATE"] = project.ScheduledTime?.ToString("yyyy-MM-dd") ?? string.Empty,
            ["SCHEDULEDTIME"] = project.ScheduledTime?.ToString("HH:mm") ?? string.Empty,
            ["DATE"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["CREATED_AT"] = project.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            ["VIDEOFILE"] = string.IsNullOrWhiteSpace(project.VideoFilePath)
                ? string.Empty
                : Path.GetFileName(project.VideoFilePath),
            ["TRANSCRIPT"] = project.TranscriptText
        };

        return ApplyTemplate(template, dict);
    }

    /// <summary>
    /// Bereinigt einen Tag fuer die Verwendung als Hashtag.
    /// Lowercase, keine Leerzeichen, keine Sonderzeichen.
    /// </summary>
    private static string SanitizeHashtag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        // Lowercase, Leerzeichen/Bindestriche/Unterstriche entfernen
        var sanitized = tag
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "");

        // Nur alphanumerische Zeichen behalten (inkl. Umlaute)
        return AlphanumericRegex().Replace(sanitized, "");
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]")]
    private static partial Regex AlphanumericRegex();
}
