using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

            // ffprobe bevorzugt im gleichen Ordner wie ffmpeg
            _ffprobePath = GetFFprobePath(appFfmpegPath)
                           ?? FindInSystemPath(GetExeName("ffprobe"));

            return true;
        }

        // Dann im PATH suchen
        var systemFfmpegPath = FindInSystemPath(GetExeName("ffmpeg"));
        if (systemFfmpegPath is not null)
        {
            _ffmpegPath = systemFfmpegPath;

            // ffprobe bevorzugt im gleichen Ordner wie ffmpeg
            _ffprobePath = GetFFprobePath(systemFfmpegPath)
                           ?? FindInSystemPath(GetExeName("ffprobe"));

            return true;
        }

        _ffmpegPath = null;
        _ffprobePath = null;
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

        var extractPath = Constants.FFmpegFolder;

        // Zip atomar laden -> weniger kaputte zip-Dateien bei Abbruch
        var zipPath = Path.Combine(extractPath, "ffmpeg-download.zip");
        var tempZipPath = zipPath + ".download";

        try
        {
            progress?.Report(DependencyDownloadProgress.FFmpegDownload(0, "Starte Download..."));

            Directory.CreateDirectory(extractPath);

            // Alte Temp-Zip löschen
            TryDeleteFile(tempZipPath);

            // Download mit Progress
            await DownloadFileAsync(
                FFmpegWindowsDownloadUrl,
                tempZipPath,
                progress,
                cancellationToken).ConfigureAwait(false);

            // TempZip -> Zip (Overwrite über Delete um Framework-kompatibel zu bleiben)
            TryDeleteFile(zipPath);
            File.Move(tempZipPath, zipPath);

            if (!ValidateDownloadedZip(zipPath, progress))
            {
                TryDeleteFile(zipPath);
                return false;
            }

            progress?.Report(DependencyDownloadProgress.FFmpegDownload(90, "Entpacke..."));

            // Alte Dateien löschen falls vorhanden
            var existingDirs = SafeGetDirectories(extractPath, "ffmpeg-*");
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
            TryDeleteFile(zipPath);

            progress?.Report(DependencyDownloadProgress.FFmpegDownload(100, "Abgeschlossen."));

            // Pfade aktualisieren
            return CheckAvailability();
        }
        catch (OperationCanceledException)
        {
            // Cleanup
            TryDeleteFile(tempZipPath);
            throw;
        }
        catch (Exception ex)
        {
            // Cleanup
            TryDeleteFile(tempZipPath);

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
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[81920];
        int bytesRead;

        // Progress nur alle ~100ms reporten (Stopwatch stabiler als DateTime)
        var throttle = Stopwatch.StartNew();
        long lastReportMs = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            downloadedBytes += bytesRead;

            var nowMs = throttle.ElapsedMilliseconds;
            if (nowMs - lastReportMs > 100)
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

                lastReportMs = nowMs;
            }
        }

        // Falls Download sehr schnell war: einmal final reporten
        {
            var percent = totalBytes > 0
                ? (double)downloadedBytes / totalBytes * 85
                : 0;

            progress?.Report(new DependencyDownloadProgress(
                DependencyType.FFmpeg,
                percent,
                "FFmpeg wird heruntergeladen...",
                downloadedBytes,
                totalBytes));
        }
    }

    private static bool ValidateDownloadedZip(string zipPath, IProgress<DependencyDownloadProgress>? progress)
    {
        try
        {
            var expectedHash = Environment.GetEnvironmentVariable("DCM_FFMPEG_SHA256");
            if (!string.IsNullOrWhiteSpace(expectedHash))
            {
                if (!VerifySha256(zipPath, expectedHash, out var actualHash))
                {
                    progress?.Report(new DependencyDownloadProgress(
                        DependencyType.FFmpeg,
                        0,
                        $"FFmpeg-Download Hash-Mismatch. Erwartet: {expectedHash}, erhalten: {actualHash}"));
                    return false;
                }
            }

            var ffmpegName = GetExeName("ffmpeg");
            var ffprobeName = GetExeName("ffprobe");
            if (!ZipContainsExecutables(zipPath, ffmpegName, ffprobeName))
            {
                progress?.Report(new DependencyDownloadProgress(
                    DependencyType.FFmpeg,
                    0,
                    "FFmpeg-Download ist unvollständig oder beschädigt."));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            progress?.Report(new DependencyDownloadProgress(
                DependencyType.FFmpeg,
                0,
                $"FFmpeg-Download konnte nicht validiert werden: {ex.Message}"));
            return false;
        }
    }

    private static bool VerifySha256(string filePath, string expectedHash, out string actualHash)
    {
        expectedHash = expectedHash.Trim().ToLowerInvariant();
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        actualHash = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ZipContainsExecutables(string zipPath, string ffmpegName, string ffprobeName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var hasFfmpeg = archive.Entries.Any(e => string.Equals(e.Name, ffmpegName, StringComparison.OrdinalIgnoreCase));
        var hasFfprobe = archive.Entries.Any(e => string.Equals(e.Name, ffprobeName, StringComparison.OrdinalIgnoreCase));
        return hasFfmpeg && hasFfprobe;
    }

    private static string GetExeName(string baseName)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{baseName}.exe" : baseName;

    private static string? FindInAppFolder()
    {
        var ffmpegFolder = Constants.FFmpegFolder;

        if (!Directory.Exists(ffmpegFolder))
        {
            return null;
        }

        // Suche in Unterordnern (z.B. ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe)
        var exeName = GetExeName("ffmpeg");

        try
        {
            // EnumerateFiles ist speicherschonender als GetFiles
            return Directory.EnumerateFiles(ffmpegFolder, exeName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindInSystemPath(string exeName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var paths = pathEnv.Split(separator)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);

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

        var ffprobeName = GetExeName("ffprobe");
        var ffprobePath = Path.Combine(directory, ffprobeName);

        return File.Exists(ffprobePath) ? ffprobePath : null;
    }

    private static string[] SafeGetDirectories(string root, string pattern)
    {
        try
        {
            return Directory.GetDirectories(root, pattern);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void TryDeleteFile(string path)
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
