using System;
using System.IO;
using System.Linq;

namespace DCM.Core.Services;

/// <summary>
/// Thread-sichere, zentrale Auflösung des FFmpeg-Pfads.
/// Alle Services sollten diese Klasse nutzen statt eigene FindFFmpeg()-Methoden zu haben.
/// Der aufgelöste Pfad wird gecacht und nur einmal pro Laufzeit ermittelt.
/// </summary>
public static class FFmpegPathResolver
{
    private static readonly object Lock = new();
    private static string? _resolvedPath;
    private static string? _resolvedDir;
    private static bool _resolved;

    /// <summary>
    /// Gibt den Pfad zur FFmpeg-Executable zurück, oder null wenn nicht gefunden.
    /// Thread-safe und gecacht nach erstem Aufruf.
    /// </summary>
    public static string? FFmpegPath
    {
        get
        {
            EnsureResolved();
            return _resolvedPath;
        }
    }

    /// <summary>
    /// Gibt das Verzeichnis zurück, in dem FFmpeg liegt, oder null wenn nicht gefunden.
    /// Thread-safe und gecacht nach erstem Aufruf.
    /// </summary>
    public static string? FFmpegDirectory
    {
        get
        {
            EnsureResolved();
            return _resolvedDir;
        }
    }

    /// <summary>
    /// Gibt an, ob FFmpeg verfügbar ist. Thread-safe, keine Seiteneffekte über Caching hinaus.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureResolved();
            return _resolvedPath is not null;
        }
    }

    /// <summary>
    /// Erzwingt eine erneute Auflösung des FFmpeg-Pfads.
    /// Nützlich z.B. nach einem FFmpeg-Download.
    /// </summary>
    public static void Invalidate()
    {
        lock (Lock)
        {
            _resolved = false;
            _resolvedPath = null;
            _resolvedDir = null;
        }
    }

    private static void EnsureResolved()
    {
        if (_resolved)
        {
            return;
        }

        lock (Lock)
        {
            if (_resolved)
            {
                return;
            }

            _resolvedPath = FindFFmpeg();
            _resolvedDir = _resolvedPath is not null ? Path.GetDirectoryName(_resolvedPath) : null;
            _resolved = true;
        }
    }

    private static string? FindFFmpeg()
    {
        // Im App-Ordner suchen
        var appFolder = Constants.FFmpegFolder;
        if (Directory.Exists(appFolder))
        {
            try
            {
                var found = Directory.EnumerateFiles(appFolder, "ffmpeg.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }
            }
            catch
            {
                // Ignorieren
            }
        }

        // Im PATH suchen
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var path in pathEnv.Split(';'))
        {
            try
            {
                var fullPath = Path.Combine(path.Trim(), "ffmpeg.exe");
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignorieren
            }
        }

        return null;
    }
}
