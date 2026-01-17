using System.Text.Json;
using DCM.Core.Logging;

namespace DCM.Core.Configuration;

public sealed class JsonSettingsProvider : ISettingsProvider
{
    private readonly IAppLogger _logger;
    private readonly string _settingsFilePath;

    public JsonSettingsProvider(IAppLogger? logger = null, string? customAppDataFolder = null)
    {
        _logger = logger ?? AppLogger.Instance;
        var baseFolder = ResolveBaseFolder(customAppDataFolder);
        _settingsFilePath = Path.Combine(baseFolder, Constants.SettingsFileName);
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

    public AppSettings Load()
    {
        var path = _settingsFilePath;

        if (!File.Exists(path))
        {
            _logger.Debug("Einstellungsdatei existiert nicht, verwende Standardwerte", "Settings");
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
            _logger.Debug("Einstellungen erfolgreich geladen", "Settings");
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Laden der Einstellungen: {ex.Message}", "Settings", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var path = _settingsFilePath;

        try
        {
            // Backup erstellen
            if (File.Exists(path))
            {
                try
                {
                    File.Copy(path, path + ".bak", overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Backup der Einstellungen fehlgeschlagen: {ex.Message}", "Settings");
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(path, json);
            _logger.Debug("Einstellungen erfolgreich gespeichert", "Settings");
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Speichern der Einstellungen: {ex.Message}", "Settings", ex);
            throw;
        }
    }
}
