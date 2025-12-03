using System.Text.Json;
using DCM.Core.Models;

namespace DCM.Core.Services;

public sealed class UploadHistoryService
{
    private readonly string _filePath;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Erstellt einen UploadHistoryService.
    /// Wenn kein Pfad angegeben ist, wird der Standard-AppData-Pfad verwendet.
    /// </summary>
    public UploadHistoryService(string? customFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _filePath = customFilePath;
        }
        else
        {
            _filePath = Path.Combine(Constants.AppDataFolder, Constants.UploadHistoryFileName);
        }
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
                return Array.Empty<UploadHistoryEntry>();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<UploadHistoryEntry>>(json, JsonOptions);
                return list ?? new List<UploadHistoryEntry>();
            }
            catch
            {
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

            if (File.Exists(_filePath))
            {
                try
                {
                    File.Copy(_filePath, _filePath + ".bak", overwrite: true);
                }
                catch
                {
                    // Backup ist Komfort, Fehler ignorieren.
                }
            }

            var json = JsonSerializer.Serialize(entries, JsonOptions);
            File.WriteAllText(_filePath, json);
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
                }
                catch
                {
                    // Fehler ignorieren.
                }
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
        catch
        {
            return new List<UploadHistoryEntry>();
        }
    }
}