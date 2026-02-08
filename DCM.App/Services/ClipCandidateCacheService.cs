using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DCM.Core;
using DCM.Core.Logging;
using DCM.Core.Models;

namespace DCM.App.Services;

/// <summary>
/// Cache-Service für Highlight-Kandidaten.
/// Speichert und lädt Kandidaten pro Draft, um wiederholte LLM-Aufrufe zu vermeiden.
/// </summary>
public sealed class ClipCandidateCacheService
{
    private readonly ConcurrentDictionary<Guid, CachedCandidateEntry> _memoryCache = new();
    private readonly IAppLogger _logger;
    private readonly string _cacheFolder;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ClipCandidateCacheService(IAppLogger? logger = null)
    {
        _logger = logger ?? AppLogger.Instance;
        _cacheFolder = Path.Combine(Constants.AppDataFolder, "ClipCandidateCache");
    }

    /// <summary>
    /// Versucht, gecachte Kandidaten für einen Draft zu laden.
    /// </summary>
    /// <param name="draftId">ID des Drafts.</param>
    /// <param name="transcriptHash">Hash des Transkripts zur Validierung.</param>
    /// <param name="candidates">Die gecachten Kandidaten, falls vorhanden und valide.</param>
    /// <returns>True wenn valide gecachte Daten gefunden wurden.</returns>
    public bool TryGetCandidates(Guid draftId, string transcriptHash, out IReadOnlyList<ClipCandidate> candidates)
    {
        candidates = Array.Empty<ClipCandidate>();

        // Memory Cache prüfen
        if (_memoryCache.TryGetValue(draftId, out var cached) &&
            string.Equals(cached.TranscriptHash, transcriptHash, StringComparison.Ordinal))
        {
            candidates = cached.Candidates;
            _logger.Debug($"Kandidaten aus Memory-Cache geladen: {candidates.Count} für Draft {draftId:N}", "CandidateCache");
            return true;
        }

        // Disk Cache prüfen
        var cachePath = GetCachePath(draftId);
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(cachePath);
            var entry = JsonSerializer.Deserialize<CachedCandidateEntry>(json, JsonOptions);

            if (entry is null || !string.Equals(entry.TranscriptHash, transcriptHash, StringComparison.Ordinal))
            {
                // Hash stimmt nicht überein - Cache invalidieren
                TryDeleteCacheFile(cachePath);
                return false;
            }

            // In Memory-Cache übernehmen
            _memoryCache[draftId] = entry;
            candidates = entry.Candidates;
            _logger.Debug($"Kandidaten aus Disk-Cache geladen: {candidates.Count} für Draft {draftId:N}", "CandidateCache");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Fehler beim Laden des Kandidaten-Cache: {ex.Message}", "CandidateCache");
            TryDeleteCacheFile(cachePath);
            return false;
        }
    }

    /// <summary>
    /// Speichert Kandidaten im Cache.
    /// Disk-Cache wird zuerst geschrieben; nur bei Erfolg wird der Memory-Cache aktualisiert,
    /// damit Memory- und Disk-Cache nie auseinanderlaufen.
    /// </summary>
    /// <param name="draftId">ID des Drafts.</param>
    /// <param name="transcriptHash">Hash des Transkripts.</param>
    /// <param name="candidates">Die zu cachenden Kandidaten.</param>
    public void SetCandidates(Guid draftId, string transcriptHash, IReadOnlyList<ClipCandidate> candidates)
    {
        var entry = new CachedCandidateEntry
        {
            DraftId = draftId,
            TranscriptHash = transcriptHash,
            Candidates = candidates.ToList(),
            CachedAt = DateTimeOffset.UtcNow
        };

        // Disk Cache zuerst aktualisieren (atomares Schreiben über Temp-Datei)
        var diskWriteSucceeded = false;
        try
        {
            Directory.CreateDirectory(_cacheFolder);
            var cachePath = GetCachePath(draftId);
            var tempPath = cachePath + ".tmp";
            var json = JsonSerializer.Serialize(entry, JsonOptions);

            // Atomares Schreiben: Temp-Datei → Rename
            File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }
            File.Move(tempPath, cachePath);

            diskWriteSucceeded = true;
            _logger.Debug($"Kandidaten gecacht: {candidates.Count} für Draft {draftId:N}", "CandidateCache");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Fehler beim Speichern des Kandidaten-Cache: {ex.Message}", "CandidateCache");
        }

        // Memory Cache nur aktualisieren wenn Disk-Write erfolgreich war,
        // um Inkonsistenzen zwischen Memory- und Disk-Cache zu verhindern.
        if (diskWriteSucceeded)
        {
            _memoryCache[draftId] = entry;
        }
    }

    /// <summary>
    /// Invalidiert den Cache für einen Draft.
    /// </summary>
    public void InvalidateCache(Guid draftId)
    {
        _memoryCache.TryRemove(draftId, out _);
        var cachePath = GetCachePath(draftId);
        TryDeleteCacheFile(cachePath);
        _logger.Debug($"Cache invalidiert für Draft {draftId:N}", "CandidateCache");
    }

    /// <summary>
    /// Löscht alle gecachten Kandidaten.
    /// </summary>
    public void ClearAll()
    {
        _memoryCache.Clear();

        try
        {
            if (Directory.Exists(_cacheFolder))
            {
                foreach (var file in Directory.EnumerateFiles(_cacheFolder, "*.json"))
                {
                    TryDeleteCacheFile(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Fehler beim Löschen des Cache-Ordners: {ex.Message}", "CandidateCache");
        }
    }

    /// <summary>
    /// Bereinigt alte Cache-Einträge (älter als 7 Tage).
    /// </summary>
    public int CleanupOldEntries(TimeSpan? maxAge = null)
    {
        var threshold = DateTimeOffset.UtcNow - (maxAge ?? TimeSpan.FromDays(7));
        var removed = 0;

        try
        {
            if (!Directory.Exists(_cacheFolder))
            {
                return 0;
            }

            foreach (var file in Directory.EnumerateFiles(_cacheFolder, "*.json"))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < threshold.UtcDateTime)
                    {
                        info.Delete();
                        removed++;
                    }
                }
                catch
                {
                    // Ignorieren
                }
            }

            if (removed > 0)
            {
                _logger.Debug($"Cache-Cleanup: {removed} alte Einträge entfernt", "CandidateCache");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Fehler beim Cache-Cleanup: {ex.Message}", "CandidateCache");
        }

        return removed;
    }

    private string GetCachePath(Guid draftId)
    {
        return Path.Combine(_cacheFolder, $"{draftId:N}.json");
    }

    private void TryDeleteCacheFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            // Auch eventuelle Temp-Dateien aufräumen
            var tempPath = path + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Ignorieren
        }
    }

    /// <summary>
    /// Berechnet einen Hash aus dem Transkript-Text für Cache-Validierung.
    /// </summary>
    public static string ComputeTranscriptHash(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(transcript);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Cache-Eintrag für persistierte Kandidaten.
    /// </summary>
    private sealed class CachedCandidateEntry
    {
        public Guid DraftId { get; set; }
        public string TranscriptHash { get; set; } = string.Empty;
        public List<ClipCandidate> Candidates { get; set; } = new();
        public DateTimeOffset CachedAt { get; set; }
    }
}
