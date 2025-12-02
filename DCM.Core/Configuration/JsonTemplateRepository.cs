using System.Text.Json;
using DCM.Core.Models;

namespace DCM.Core.Configuration;

public sealed class JsonTemplateRepository : ITemplateRepository
{
    private static string GetFilePath() => Path.Combine(Constants.GetAppDataFolder(), Constants.TemplatesFileName);

    public IEnumerable<Template> Load()
    {
        var path = GetFilePath();

        if (!File.Exists(path))
        {
            var defaults = GetDefaultTemplates().ToArray();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var list = JsonSerializer.Deserialize<List<Template>>(json, options);
            return list ?? Enumerable.Empty<Template>();
        }
        catch
        {
            return Enumerable.Empty<Template>();
        }
    }

    public void Save(IEnumerable<Template> templates)
    {
        var path = GetFilePath();

        if (File.Exists(path))
        {
            var backupPath = path + ".bak";
            try
            {
                File.Copy(path, backupPath, overwrite: true);
            }
            catch
            {
                // Backup-Fehler sind nicht kritisch.
            }
        }

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