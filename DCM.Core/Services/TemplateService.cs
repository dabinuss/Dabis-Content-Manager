using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DCM.Core.Models;

namespace DCM.Core.Services;

public sealed partial class TemplateService
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{\{\s*(?<name>[A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string ApplyTemplate(string template, IDictionary<string, string?> values)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        if (values is null) throw new ArgumentNullException(nameof(values));

        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups["name"].Value.ToUpperInvariant();

            if (values.TryGetValue(key, out var value) && value is not null)
            {
                return value;
            }

            // Unbekannte oder leere Platzhalter werden einfach durch "" ersetzt
            return string.Empty;
        });
    }

    public string ApplyTemplate(string template, UploadProject project)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        string? transcriptSnippet = null;

        if (!string.IsNullOrWhiteSpace(project.TranscriptText))
        {
            var text = project.TranscriptText.Trim();

            const int maxLength = 280;
            if (text.Length > maxLength)
            {
                transcriptSnippet = text[..maxLength].TrimEnd() + "…";
            }
            else
            {
                transcriptSnippet = text;
            }
        }

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
            ["DATE"] = project.ScheduledTime?.ToString("yyyy-MM-dd"),
            ["TAGS"] = tagsCsv,
            ["HASHTAGS"] = hashTags,
            ["PLAYLIST"] = project.PlaylistId,
            ["VISIBILITY"] = project.Visibility.ToString(),
            ["PLATFORM"] = project.Platform.ToString(),
            ["CREATED_AT"] = project.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            ["TRANSCRIPT_SNIPPET"] = transcriptSnippet,
            ["TRANSCRIPT"] = project.TranscriptText,
            ["YEAR"] = DateTime.Now.Year.ToString(),
            ["MONTH"] = DateTime.Now.ToString("MM"),
            ["DAY"] = DateTime.Now.ToString("dd"),
            ["VIDEOFILE"] = string.IsNullOrWhiteSpace(project.VideoFilePath)
                ? string.Empty
                : System.IO.Path.GetFileName(project.VideoFilePath),
            ["VIDEOPATH"] = project.VideoFilePath ?? string.Empty,
            ["SCHEDULEDDATE"] = project.ScheduledTime?.ToString("yyyy-MM-dd") ?? string.Empty,
            ["SCHEDULEDTIME"] = project.ScheduledTime?.ToString("HH:mm") ?? string.Empty,
            ["DESCRIPTION"] = project.Description ?? string.Empty
        };

        return ApplyTemplate(template, dict);
    }

    /// <summary>
    /// Bereinigt einen Tag für die Verwendung als Hashtag.
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