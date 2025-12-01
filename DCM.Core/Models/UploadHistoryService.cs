using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DCM.Core.Models;

namespace DCM.Core.Services
{
    public sealed class UploadHistoryService
    {
        private readonly string _filePath;
        private readonly object _lock = new();

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        /// <summary>
        /// Erstellt einen UploadHistoryService.
        /// Wenn kein Pfad angegeben ist, wird
        /// %AppData%\DabisContentManager\upload_history.json
        /// verwendet.
        /// </summary>
        public UploadHistoryService(string? customFilePath = null)
        {
            if (!string.IsNullOrWhiteSpace(customFilePath))
            {
                _filePath = customFilePath;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var folder = Path.Combine(appData, "DabisContentManager");
                Directory.CreateDirectory(folder);
                _filePath = Path.Combine(folder, "upload_history.json");
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
                    var list = JsonSerializer.Deserialize<List<UploadHistoryEntry>>(json, _jsonOptions);
                    return list ?? new List<UploadHistoryEntry>();
                }
                catch
                {
                    // Datei korrupt? -> nicht crashen, sondern neu anfangen.
                    return Array.Empty<UploadHistoryEntry>();
                }
            }
        }

        /// <summary>
        /// Fügt einen neuen Eintrag basierend auf Projekt und Ergebnis hinzu
        /// und schreibt die JSON-Datei (mit optionalem Backup).
        /// </summary>
        public void AddEntry(UploadProject project, UploadResult result)
        {
            if (project is null) throw new ArgumentNullException(nameof(project));
            if (result is null) throw new ArgumentNullException(nameof(result));

            lock (_lock)
            {
                var entries = LoadInternalListUnsafe();

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

                // Kleines Backup, falls beim Schreiben was schiefgeht
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

                var json = JsonSerializer.Serialize(entries, _jsonOptions);
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
                        // Wenn Delete scheitert, werfen wir keinen fatalen Fehler.
                    }
                }
            }
        }

        private List<UploadHistoryEntry> LoadInternalListUnsafe()
        {
            if (!File.Exists(_filePath))
            {
                return new List<UploadHistoryEntry>();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<UploadHistoryEntry>>(json, _jsonOptions);
                return list ?? new List<UploadHistoryEntry>();
            }
            catch
            {
                // Korrupt? -> neu anfangen.
                return new List<UploadHistoryEntry>();
            }
        }
    }
}
