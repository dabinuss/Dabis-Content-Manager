using System.Text.Json;
using DCM.Core.Logging;
using DCM.Core.Models;
using DCM.Core;

namespace DCM.Core.Configuration;

public sealed class JsonPresetRepository : IPresetRepository
{
    private readonly IAppLogger _logger;
    private readonly string _presetsFilePath;
    private readonly string _templatesFilePath;

    public JsonPresetRepository(IAppLogger? logger = null, string? customAppDataFolder = null)
    {
        _logger = logger ?? AppLogger.Instance;
        var baseFolder = ResolveBaseFolder(customAppDataFolder);
        _presetsFilePath = Path.Combine(baseFolder, Constants.PresetsFileName);
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

    public IEnumerable<UploadPreset> Load()
    {
        var path = _presetsFilePath;

        if (!File.Exists(path))
        {
            var migrated = TryMigrateTemplates();
            if (migrated is not null)
            {
                Save(migrated);
                return migrated;
            }

            _logger.Debug("Preset-Datei existiert nicht, erstelle Standard-Presets", "Presets");
            var defaults = GetDefaultPresets().ToArray();
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

            var list = JsonSerializer.Deserialize<List<UploadPreset>>(json, options);
            var result = list ?? Enumerable.Empty<UploadPreset>();
            _logger.Debug($"Presets geladen: {result.Count()} Eintraege", "Presets");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Laden der Presets: {ex.Message}", "Presets", ex);
            return Enumerable.Empty<UploadPreset>();
        }
    }

    public void Save(IEnumerable<UploadPreset> presets)
    {
        var path = _presetsFilePath;

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
                    _logger.Warning($"Backup der Presets fehlgeschlagen: {ex.Message}", "Presets");
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var presetList = presets.ToList();
            var json = JsonSerializer.Serialize(presetList, options);
            AtomicFile.WriteAllText(path, json);
            _logger.Debug($"Presets gespeichert: {presetList.Count} Eintraege", "Presets");
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Speichern der Presets: {ex.Message}", "Presets", ex);
            throw;
        }
    }

    private List<UploadPreset>? TryMigrateTemplates()
    {
        if (!File.Exists(_templatesFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_templatesFilePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var templates = JsonSerializer.Deserialize<List<Template>>(json, options);
            if (templates is null || templates.Count == 0)
            {
                return null;
            }

            var presets = templates.Select(t => new UploadPreset
            {
                Id = string.IsNullOrWhiteSpace(t.Id) ? Guid.NewGuid().ToString("N") : t.Id,
                Name = t.Name ?? string.Empty,
                Platform = t.Platform,
                Description = t.Description,
                IsDefault = t.IsDefault,
                DescriptionTemplate = t.Body ?? string.Empty,
                Visibility = VideoVisibility.Unlisted
            }).ToList();

            _logger.Debug($"Templates migriert: {presets.Count} Presets erstellt", "Presets");
            return presets;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Migration von Templates fehlgeschlagen: {ex.Message}", "Presets");
            return null;
        }
    }

    private static IEnumerable<UploadPreset> GetDefaultPresets()
    {
        yield return new UploadPreset
        {
            Name = "Standard YouTube Preset",
            Platform = PlatformType.YouTube,
            IsDefault = true,
            Visibility = VideoVisibility.Unlisted,
            DescriptionTemplate =
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
