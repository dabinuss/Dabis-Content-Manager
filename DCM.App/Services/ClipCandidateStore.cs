using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DCM.Core;
using DCM.Core.Logging;
using DCM.Core.Models;

namespace DCM.App.Services;

/// <summary>
/// Persistenter Cache für Clip-Kandidaten pro Draft.
/// Thread-safe: Schreiboperationen verwenden atomares Write-via-Rename,
/// und pro DraftId wird über ein Lock serialisiert.
/// </summary>
public sealed class ClipCandidateStore
{
    private readonly IAppLogger _logger;

    /// <summary>
    /// Pro-DraftId-Locks um gleichzeitige Lese-/Schreibzugriffe auf dieselbe Datei zu verhindern.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, object> _fileLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ClipCandidateStore(IAppLogger? logger = null)
    {
        _logger = logger ?? AppLogger.Instance;
    }

    public void SaveCache(ClipCandidateCache cache)
    {
        if (cache is null)
        {
            throw new ArgumentNullException(nameof(cache));
        }

        var lockObj = _fileLocks.GetOrAdd(cache.DraftId, _ => new object());
        lock (lockObj)
        {
            try
            {
                Directory.CreateDirectory(Constants.ClipCandidatesFolder);
                var path = GetCachePath(cache.DraftId);
                var tempPath = path + ".tmp";
                var json = JsonSerializer.Serialize(cache, JsonOptions);

                // Atomares Schreiben: Erst in Temp-Datei schreiben, dann umbenennen.
                // File.Move mit overwrite ist auf Windows ab .NET 6 atomar genug,
                // um halb geschriebene Dateien beim Lesen zu verhindern.
                File.WriteAllText(tempPath, json, Encoding.UTF8);

                // Bestehende Datei ersetzen
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(tempPath, path);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fehler beim Speichern des Kandidaten-Cache: {ex.Message}", "ClipCandidateStore");
            }
        }
    }

    public ClipCandidateCache? LoadCache(Guid draftId)
    {
        var lockObj = _fileLocks.GetOrAdd(draftId, _ => new object());
        lock (lockObj)
        {
            var path = GetCachePath(draftId);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ClipCandidateCache>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fehler beim Laden des Kandidaten-Cache: {ex.Message}", "ClipCandidateStore");
                TryDeleteCache(path);
                return null;
            }
        }
    }

    public void InvalidateCache(Guid draftId)
    {
        var lockObj = _fileLocks.GetOrAdd(draftId, _ => new object());
        lock (lockObj)
        {
            var path = GetCachePath(draftId);
            TryDeleteCache(path);
        }
    }

    public bool IsCacheValid(Guid draftId, string currentTranscriptHash)
    {
        // Schnelle Prüfung: Nur Datei-Existenz, ohne vollständige Deserialisierung.
        // Für Laden + Validierung in einem Schritt stattdessen TryLoadValidCache verwenden.
        var path = GetCachePath(draftId);
        if (!File.Exists(path))
        {
            return false;
        }

        var cache = LoadCache(draftId);
        return cache?.IsValidFor(currentTranscriptHash) == true;
    }

    /// <summary>
    /// Lädt und validiert den Cache in einem einzigen Durchgang.
    /// Vermeidet doppeltes Deserialisieren bei "erst prüfen, dann laden"-Patterns.
    /// Gibt null zurück wenn kein valider Cache vorhanden ist.
    /// </summary>
    public ClipCandidateCache? TryLoadValidCache(Guid draftId, string currentTranscriptHash)
    {
        var cache = LoadCache(draftId);
        if (cache is null)
        {
            return null;
        }

        if (!cache.IsValidFor(currentTranscriptHash))
        {
            return null;
        }

        return cache;
    }

    public string GetCachePath(Guid draftId)
    {
        return Path.Combine(Constants.ClipCandidatesFolder, $"{draftId:N}.json");
    }

    public static string ComputeTranscriptHash(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(transcript);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private void TryDeleteCache(string path)
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
}
