using System.IO.Compression;
using System.Runtime.InteropServices;
using DCM.Core;

namespace DCM.Transcription.Internal;

/// <summary>
/// Verwaltet FFmpeg-Installation und -Verfügbarkeit.
/// </summary>
internal sealed class FFmpegManager
{
    private const string FFmpegWindowsDownloadUrl = 
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    private readonly HttpClient _httpClient;
    private string? _ffmpegPath;
    private string? _ffprobePath;

    public FFmpegManager(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Gibt an, ob FFmpeg verfügbar ist.
    /// </summary>
    public bool IsAvailable => _ffmpegPath is not null && File.Exists(_ffmpegPath);

    /// <summary>
    /// Pfad zur FFmpeg-Executable.
    /// </summary>
    public string? FFmpegPath => _ffmpegPath;

    /// <summary>
    /// Pfad zur FFprobe-Executable.
    /// </summary>
    public string? FFprobePath => _ffprobePath;

    /// <summary>
    /// Prüft, ob FFmpeg verfügbar ist und setzt die Pfade.
    /// </summary>
    public bool CheckAvailability()
    {
        // Zuerst im App-Ordner suchen
        var appFfmpegPath = FindInAppFolder();
        if (appFfmpegPath is not null)
        {
            _ffmpegPath = appFfmpegPath;
            _ffprobePath = GetFFprobePath(appFfmpegPath);
            return true;
        }

        // Dann im PATH suchen
        var systemFfmpegPath = FindInSystemPath();
        if (systemFfmpegPath is not null)
        {
            _ffmpegPath = systemFfmpegPath;
            _ffprobePath = GetFFprobePath(systemFfmpegPath);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Lädt FFmpeg herunter und installiert es im App-Ordner.
    /// </summary>
    public async Task<bool> DownloadAsync(
        IProgress<DependencyDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Auf Linux/macOS erwarten wir, dass FFmpeg installiert ist
            progress?.Report(new DependencyDownloadProgress(
                DependencyType.FFmpeg,
                0,
                "FFmpeg muss auf diesem System manuell installiert werden."));
            return false;
        }

        try
        {
            progress?.Report(DependencyDownloadProgress.FFmpegDownload(0, "Starte Download..."));

            // Sicherstellen, dass der Ordner existiert (Property erstellt ihn)
            var extractPath = Constants.FFmpegFolder;
            var zipPath = Path.Combine(extractPath, "ffmpeg-download.zip");

            // Download mit Progress
            await DownloadFileAsync(
                FFmpegWindowsDownloadUrl,
                zipPath,
                progress,
                cancellationToken);

            progress?.Report(DependencyDownloadProgress.FFmpegDownload(90, "Entpacke..."));

            // Alte Dateien löschen falls vorhanden
            var existingDirs = Directory.GetDirectories(extractPath, "ffmpeg-*");
            foreach (var dir in existingDirs)
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                    // Ignorieren
                }
            }

            // Entpacken
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

            // Zip löschen
            try
            {
                File.Delete(zipPath);
            }
            catch
            {
                // Ignorieren
            }

            progress?.Report(DependencyDownloadProgress.FFmpegDownload(100, "Abgeschlossen."));

            // Pfade aktualisieren
            return CheckAvailability();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report(new DependencyDownloadProgress(
                DependencyType.FFmpeg,
                0,
                $"Download fehlgeschlagen: {ex.Message}"));
            return false;
        }
    }

    private async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<DependencyDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Sicherstellen, dass der Zielordner existiert
        var directory = Path.GetDirectoryName(destinationPath);
        if (directory is not null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[81920];
        int bytesRead;
        var lastReportTime = DateTime.UtcNow;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            // Progress nur alle 100ms reporten
            if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds > 100)
            {
                var percent = totalBytes > 0
                    ? (double)downloadedBytes / totalBytes * 85 // 85% für Download, 15% für Entpacken
                    : 0;

                progress?.Report(new DependencyDownloadProgress(
                    DependencyType.FFmpeg,
                    percent,
                    "FFmpeg wird heruntergeladen...",
                    downloadedBytes,
                    totalBytes));

                lastReportTime = DateTime.UtcNow;
            }
        }
    }

    private static string? FindInAppFolder()
    {
        var ffmpegFolder = Constants.FFmpegFolder;

        if (!Directory.Exists(ffmpegFolder))
        {
            return null;
        }

        // Suche in Unterordnern (z.B. ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe)
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

        try
        {
            var files = Directory.GetFiles(ffmpegFolder, exeName, SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindInSystemPath()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
        var pathEnv = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var paths = pathEnv.Split(separator);

        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.Combine(path, exeName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ungültiger Pfad, ignorieren
            }
        }

        return null;
    }

    private static string? GetFFprobePath(string ffmpegPath)
    {
        var directory = Path.GetDirectoryName(ffmpegPath);
        if (directory is null)
        {
            return null;
        }

        var ffprobeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";
        var ffprobePath = Path.Combine(directory, ffprobeName);

        return File.Exists(ffprobePath) ? ffprobePath : null;
    }
}