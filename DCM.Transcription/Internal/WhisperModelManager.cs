using System;
using System.Diagnostics;
using DCM.Core;
using DCM.Core.Configuration;

namespace DCM.Transcription.Internal;

/// <summary>
/// Verwaltet Whisper-Modell-Downloads und -Verfügbarkeit.
/// </summary>
internal sealed class WhisperModelManager
{
    private readonly HttpClient _httpClient;

    // Minimaler State-Lock: verhindert Race-Glitches bei parallelen Statuschecks/Downloads
    private readonly object _stateLock = new();

    private string? _currentModelPath;
    private WhisperModelSize? _currentModelSize;

    public WhisperModelManager(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Gibt an, ob ein Modell verfügbar ist.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            string? path;
            lock (_stateLock)
            {
                path = _currentModelPath;
            }

            return path is not null && File.Exists(path);
        }
    }

    /// <summary>
    /// Pfad zum aktuellen Modell.
    /// </summary>
    public string? ModelPath
    {
        get
        {
            lock (_stateLock)
            {
                return _currentModelPath;
            }
        }
    }

    /// <summary>
    /// Größe des aktuellen Modells.
    /// </summary>
    public WhisperModelSize? ModelSize
    {
        get
        {
            lock (_stateLock)
            {
                return _currentModelSize;
            }
        }
    }

    /// <summary>
    /// Prüft, ob ein bestimmtes Modell verfügbar ist.
    /// </summary>
    public bool CheckAvailability(WhisperModelSize size)
    {
        var modelPath = GetModelPath(size);

        if (File.Exists(modelPath))
        {
            // Prüfen ob die Datei vollständig ist (mindestens 50% der erwarteten Größe)
            var fileInfo = new FileInfo(modelPath);
            var expectedSize = size.GetApproximateSizeBytes();

            if (fileInfo.Length >= expectedSize * 0.5)
            {
                lock (_stateLock)
                {
                    _currentModelPath = modelPath;
                    _currentModelSize = size;
                }
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Findet das größte verfügbare Modell.
    /// </summary>
    public WhisperModelSize? FindLargestAvailableModel()
    {
        // Vom größten zum kleinsten prüfen
        var sizes = new[]
        {
            WhisperModelSize.Large,
            WhisperModelSize.Medium,
            WhisperModelSize.Small,
            WhisperModelSize.Base,
            WhisperModelSize.Tiny
        };

        foreach (var size in sizes)
        {
            if (CheckAvailability(size))
            {
                return size;
            }
        }

        return null;
    }

    /// <summary>
    /// Lädt ein Whisper-Modell herunter.
    /// </summary>
    public async Task<bool> DownloadAsync(
        WhisperModelSize size,
        IProgress<DependencyDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Defensiv: Ordner wirklich anlegen
        Directory.CreateDirectory(Constants.WhisperModelsFolder);

        var modelPath = GetModelPath(size);
        var tempPath = modelPath + ".download";
        var url = size.GetDownloadUrl();

        try
        {
            progress?.Report(DependencyDownloadProgress.WhisperModelDownload(0));

            // Falls alte Temp-Datei existiert, löschen
            CleanupTempFile(tempPath);

            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var expectedBytes = size.GetApproximateSizeBytes();
            var totalBytes = response.Content.Headers.ContentLength ?? expectedBytes;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            var buffer = new byte[81920];
            int bytesRead;

            // Progress nur alle ~100ms reporten
            var throttle = Stopwatch.StartNew();
            long lastReportMs = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                downloadedBytes += bytesRead;

                var nowMs = throttle.ElapsedMilliseconds;
                if (nowMs - lastReportMs > 100)
                {
                    var percent = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;

                    progress?.Report(new DependencyDownloadProgress(
                        DependencyType.WhisperModel,
                        percent,
                        $"Whisper-Modell ({size.GetSizeDescription()}) wird heruntergeladen...",
                        downloadedBytes,
                        totalBytes));

                    lastReportMs = nowMs;
                }
            }

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Sanity-Check (gleiche Schwelle wie CheckAvailability: >= 50% expected)
            // -> verhindert "Download OK aber Datei praktisch leer/kaputt".
            try
            {
                var fi = new FileInfo(tempPath);
                if (!fi.Exists || fi.Length < expectedBytes * 0.5)
                {
                    CleanupTempFile(tempPath);

                    progress?.Report(new DependencyDownloadProgress(
                        DependencyType.WhisperModel,
                        0,
                        "Download unvollständig oder beschädigt. Bitte erneut versuchen."));

                    return false;
                }
            }
            catch
            {
                // Wenn wir nicht prüfen können, machen wir weiter wie bisher
            }

            // Umbenennen/Ersetzen möglichst atomar (File.Replace ist framework-breit verfügbar)
            if (File.Exists(modelPath))
            {
                try
                {
                    File.Replace(tempPath, modelPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch
                {
                    // Fallback auf Delete+Move (Semantik wie vorher)
                    try
                    {
                        File.Delete(modelPath);
                    }
                    catch
                    {
                        // ignorieren
                    }

                    File.Move(tempPath, modelPath);
                }
            }
            else
            {
                File.Move(tempPath, modelPath);
            }

            lock (_stateLock)
            {
                _currentModelPath = modelPath;
                _currentModelSize = size;
            }

            progress?.Report(DependencyDownloadProgress.WhisperModelDownload(100, totalBytes, totalBytes));

            return true;
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            CleanupTempFile(tempPath);

            progress?.Report(new DependencyDownloadProgress(
                DependencyType.WhisperModel,
                0,
                $"Download fehlgeschlagen: {ex.Message}"));

            return false;
        }
    }

    public void RemoveOtherModels(WhisperModelSize keepSize)
    {
        var sizes = Enum.GetValues<WhisperModelSize>();
        foreach (var size in sizes)
        {
            if (size == keepSize)
            {
                continue;
            }

            var path = GetModelPath(size);
            var tempPath = path + ".download";

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

            // Temp-Reste ebenfalls entfernen
            CleanupTempFile(tempPath);
        }
    }

    private static string GetModelPath(WhisperModelSize size)
    {
        return Path.Combine(Constants.WhisperModelsFolder, size.GetFileName());
    }

    private static void CleanupTempFile(string tempPath)
    {
        try
        {
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
