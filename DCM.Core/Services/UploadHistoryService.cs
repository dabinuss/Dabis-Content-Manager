using System.Text.Json;
using DCM.Core.Logging;
using DCM.Core.Models;

namespace DCM.Core.Services;

public sealed class UploadHistoryService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly IAppLogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Erstellt einen UploadHistoryService.
    /// Wenn kein Pfad angegeben ist, wird der Standard-AppData-Pfad verwendet.
    /// </summary>
    public UploadHistoryService(string? customFilePath = null, IAppLogger? logger = null)
    {
        _logger = logger ?? AppLogger.Instance;

        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _filePath = customFilePath;
        }
        else
        {
            _filePath = Path.Combine(Constants.AppDataFolder, Constants.UploadHistoryFileName);
        }

        _logger.Debug($"UploadHistoryService initialisiert, Pfad: {_filePath}", "UploadHistory");
    }

    /// <summary>
    /// Liefert alle gespeicherten Upload-Historieneinträge.
    /// Bei Fehlern oder fehlender Datei wird eine leere Liste zurückgegeben.
    /// </summary>
    public IReadOnlyList<UploadHistoryEntry> GetAll()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                _logger.Debug("Historiendatei existiert nicht, gebe leere Liste zurück", "UploadHistory");
                return Array.Empty<UploadHistoryEntry>();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<UploadHistoryEntry>>(json, JsonOptions);
                var result = list ?? new List<UploadHistoryEntry>();
                _logger.Debug($"Historie geladen: {result.Count} Einträge", "UploadHistory");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Laden der Historie: {ex.Message}", "UploadHistory", ex);
                return Array.Empty<UploadHistoryEntry>();
            }
        }
    }

    /// <summary>
    /// Fügt einen neuen Eintrag basierend auf Projekt und Ergebnis hinzu.
    /// </summary>
    public void AddEntry(UploadProject project, UploadResult result)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (result is null) throw new ArgumentNullException(nameof(result));

        lock (_lock)
        {
            var entries = LoadInternalList();

            var title = project.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                try
                {
                    title = Path.GetFileNameWithoutExtension(project.VideoFilePath) ?? string.Empty;
                }
                catch
                {
                    title = string.Empty;
                }
            }

            var entry = new UploadHistoryEntry
            {
                Id = Guid.NewGuid(),
                Platform = project.Platform,
                VideoTitle = title,
                VideoUrl = result.VideoUrl,
                DateTime = DateTimeOffset.Now,
                Success = result.Success,
                ErrorMessage = result.Success ? null : result.ErrorMessage
            };

            entries.Add(entry);
            _logger.Debug($"Historieneintrag hinzugefügt: {title} ({(result.Success ? "Erfolg" : "Fehler")})", "UploadHistory");

            if (File.Exists(_filePath))
            {
                try
                {
                    File.Copy(_filePath, _filePath + ".bak", overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Backup der Historie fehlgeschlagen: {ex.Message}", "UploadHistory");
                }
            }

            try
            {
                var json = JsonSerializer.Serialize(entries, JsonOptions);
                File.WriteAllText(_filePath, json);
                _logger.Debug($"Historie gespeichert: {entries.Count} Einträge", "UploadHistory");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Speichern der Historie: {ex.Message}", "UploadHistory", ex);
            }
        }
    }

    /// <summary>
    /// Löscht die komplette Upload-Historie.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    File.Delete(_filePath);
                    _logger.Info("Upload-Historie gelöscht", "UploadHistory");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler beim Löschen der Historie: {ex.Message}", "UploadHistory", ex);
                }
            }
            else
            {
                _logger.Debug("Historie-Datei existiert nicht, nichts zu löschen", "UploadHistory");
            }
        }
    }

    private List<UploadHistoryEntry> LoadInternalList()
    {
        if (!File.Exists(_filePath))
        {
            return new List<UploadHistoryEntry>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<UploadHistoryEntry>>(json, JsonOptions);
            return list ?? new List<UploadHistoryEntry>();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Fehler beim internen Laden der Historie: {ex.Message}", "UploadHistory");
            return new List<UploadHistoryEntry>();
        }
    }
}