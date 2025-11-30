using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DCM.Core.Models;

namespace DCM.Core.Configuration;

public sealed class JsonTemplateRepository : ITemplateRepository
{
    private const string FolderName = "DabisContentManager";
    private const string FileName = "templates.json";

    private static string GetFolderPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, FolderName);

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        return folder;
    }

    private static string GetFilePath() => Path.Combine(GetFolderPath(), FileName);

    public IEnumerable<Template> Load()
    {
        var path = GetFilePath();

        if (!File.Exists(path))
        {
            // Falls keine Datei existiert: Standardtemplate erzeugen
            var defaults = GetDefaultTemplates().ToArray();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var list = JsonSerializer.Deserialize<List<Template>>(json, options);
        return list ?? Enumerable.Empty<Template>();
    }

    public void Save(IEnumerable<Template> templates)
    {
        var path = GetFilePath();

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(templates, options);
        File.WriteAllText(path, json);
    }

    private static IEnumerable<Template> GetDefaultTemplates()
    {
        yield return new Template
        {
            Name = "Standard YouTube Beschreibung",
            Platform = PlatformType.YouTube,
            IsDefault = true,
            Body =
@"Titel: {{TITLE}}

Upload am: {{DATE}}

Tags: {{TAGS}}

Playlist: {{PLAYLIST}}
Sichtbarkeit: {{VISIBILITY}}
Plattform: {{PLATFORM}}

Erstellt am: {{CREATED_AT}}"
        };
    }
}
