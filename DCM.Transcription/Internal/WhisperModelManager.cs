using DCM.Core;
using DCM.Core.Configuration;

namespace DCM.Transcription.Internal;

/// <summary>
/// Verwaltet Whisper-Modell-Downloads und -Verfügbarkeit.
/// </summary>
internal sealed class WhisperModelManager
{
    private readonly HttpClient _httpClient;
    private string? _currentModelPath;
    private WhisperModelSize? _currentModelSize;

    public WhisperModelManager(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Gibt an, ob ein Modell verfügbar ist.
    /// </summary>
    public bool IsAvailable => _currentModelPath is not null && File.Exists(_currentModelPath);

    /// <summary>
    /// Pfad zum aktuellen Modell.
    /// </summary>
    public string? ModelPath => _currentModelPath;

    /// <summary>
    /// Größe des aktuellen Modells.
    /// </summary>
    public WhisperModelSize? ModelSize => _currentModelSize;

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
                _currentModelPath = modelPath;
                _currentModelSize = size;
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
        // Sicherstellen, dass der Ordner existiert (Property erstellt ihn)
        var modelsFolder = Constants.WhisperModelsFolder;
        var modelPath = GetModelPath(size);
        var tempPath = modelPath + ".download";
        var url = size.GetDownloadUrl();

        try
        {
            progress?.Report(DependencyDownloadProgress.WhisperModelDownload(0));

            // Falls alte Temp-Datei existiert, löschen
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? size.GetApproximateSizeBytes();
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                tempPath,
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
                    var percent = (double)downloadedBytes / totalBytes * 100;

                    progress?.Report(new DependencyDownloadProgress(
                        DependencyType.WhisperModel,
                        percent,
                        $"Whisper-Modell ({size.GetSizeDescription()}) wird heruntergeladen...",
                        downloadedBytes,
                        totalBytes));

                    lastReportTime = DateTime.UtcNow;
                }
            }

            // Stream schließen bevor wir die Datei verschieben
            await fileStream.FlushAsync(cancellationToken);
            fileStream.Close();

            // Umbenennen nach erfolgreichem Download
            if (File.Exists(modelPath))
            {
                File.Delete(modelPath);
            }

            File.Move(tempPath, modelPath);

            _currentModelPath = modelPath;
            _currentModelSize = size;

            progress?.Report(DependencyDownloadProgress.WhisperModelDownload(100, totalBytes, totalBytes));

            return true;
        }
        catch (OperationCanceledException)
        {
            // Temp-Datei aufräumen
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

    private static string GetModelPath(WhisperModelSize size)
    {
        // Constants.WhisperModelsFolder erstellt den Ordner automatisch
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