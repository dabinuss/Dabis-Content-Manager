using System.Text.Json;
using DCM.Core.Logging;
using DCM.Core.Models;

namespace DCM.Core.Configuration;

public sealed class JsonTemplateRepository : ITemplateRepository
{
    private readonly IAppLogger _logger;
    private readonly string _templatesFilePath;

    public JsonTemplateRepository(IAppLogger? logger = null, string? customAppDataFolder = null)
    {
        _logger = logger ?? AppLogger.Instance;
        var baseFolder = ResolveBaseFolder(customAppDataFolder);
        _templatesFilePath = Path.Combine(baseFolder, Constants.TemplatesFileName);
    }

    private static string ResolveBaseFolder(string? customAppDataFolder)
    {
        if (string.IsNullOrWhiteSpace(customAppDataFolder))
        {
            return Constants.AppDataFolder;
        }

        Directory.CreateDirectory(customAppDataFolder);
        return customAppDataFolder;
    }

    public IEnumerable<Template> Load()
    {
        var path = _templatesFilePath;

        if (!File.Exists(path))
        {
            _logger.Debug("Template-Datei existiert nicht, erstelle Standardtemplates", "Templates");
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
            var result = list ?? Enumerable.Empty<Template>();
            _logger.Debug($"Templates geladen: {result.Count()} Einträge", "Templates");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Laden der Templates: {ex.Message}", "Templates", ex);
            return Enumerable.Empty<Template>();
        }
    }

    public void Save(IEnumerable<Template> templates)
    {
        var path = _templatesFilePath;

        try
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Copy(path, path + ".bak", overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Backup der Templates fehlgeschlagen: {ex.Message}", "Templates");
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var templateList = templates.ToList();
            var json = JsonSerializer.Serialize(templateList, options);
            File.WriteAllText(path, json);
            _logger.Debug($"Templates gespeichert: {templateList.Count} Einträge", "Templates");
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Speichern der Templates: {ex.Message}", "Templates", ex);
            throw;
        }
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
