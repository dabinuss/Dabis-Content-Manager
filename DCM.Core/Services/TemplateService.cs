// DCM.Core/Services/TemplateService.cs

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DCM.Core.Models;

namespace DCM.Core.Services;

public sealed class TemplateService
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{\{(?<name>[A-Z0-9_]+)\}\}", RegexOptions.Compiled);

    public string ApplyTemplate(string template, IDictionary<string, string?> values)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        if (values is null) throw new ArgumentNullException(nameof(values));

        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups["name"].Value;

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

        var dict = new Dictionary<string, string?>
        {
            ["TITLE"] = project.Title,
            ["DATE"] = project.ScheduledTime?.ToString("yyyy-MM-dd"),
            ["TAGS"] = project.GetTagsAsCsv(),
            ["PLAYLIST"] = project.PlaylistId,
            ["VISIBILITY"] = project.Visibility.ToString(),
            ["PLATFORM"] = project.Platform.ToString(),
            ["CREATED_AT"] = project.CreatedAt.ToString("yyyy-MM-dd HH:mm")
        };

        return ApplyTemplate(template, dict);
    }
}
