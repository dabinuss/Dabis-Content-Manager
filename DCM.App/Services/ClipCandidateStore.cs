using System;
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
/// </summary>
public sealed class ClipCandidateStore
{
    private readonly IAppLogger _logger;

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

        try
        {
            Directory.CreateDirectory(Constants.ClipCandidatesFolder);
            var path = GetCachePath(cache.DraftId);
            var json = JsonSerializer.Serialize(cache, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Fehler beim Speichern des Kandidaten-Cache: {ex.Message}", "ClipCandidateStore");
        }
    }

    public ClipCandidateCache? LoadCache(Guid draftId)
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

    public void InvalidateCache(Guid draftId)
    {
        var path = GetCachePath(draftId);
        TryDeleteCache(path);
    }

    public bool IsCacheValid(Guid draftId, string currentTranscriptHash)
    {
        var cache = LoadCache(draftId);
        return cache?.IsValidFor(currentTranscriptHash) == true;
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
        }
        catch
        {
            // Ignorieren
        }
    }
}
