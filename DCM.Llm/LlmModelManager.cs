using System.Diagnostics;
using System.Security.Cryptography;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Core.Logging;

namespace DCM.Llm;

/// <summary>
/// Verwaltet LLM-Modell-Downloads und -Verfügbarkeit.
/// </summary>
public sealed class LlmModelManager
{
    private readonly HttpClient _httpClient;
    private readonly IAppLogger _logger;

    private readonly object _stateLock = new();

    private string? _currentModelPath;
    private LlmModelPreset? _currentPreset;

    private const long MinimumModelSizeBytes = 50 * 1024 * 1024;
    private static readonly byte[] GgufMagic = { 0x47, 0x47, 0x55, 0x46 }; // "GGUF"

    public LlmModelManager(HttpClient httpClient, IAppLogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? AppLogger.Instance;
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
    /// Aktuell verwendetes Preset.
    /// </summary>
    public LlmModelPreset? CurrentPreset
    {
        get
        {
            lock (_stateLock)
            {
                return _currentPreset;
            }
        }
    }

    /// <summary>
    /// Prüft, ob ein bestimmtes Preset verfügbar ist.
    /// </summary>
    public bool CheckAvailability(LlmModelPreset preset)
    {
        if (preset == LlmModelPreset.Custom)
        {
            return false;
        }

        var modelPath = GetModelPath(preset);
        if (modelPath is null)
        {
            return false;
        }

        if (File.Exists(modelPath))
        {
            var fileInfo = new FileInfo(modelPath);
            var expectedSize = preset.GetApproximateSizeBytes();

            // Prüfen ob die Datei weitgehend vollständig ist (mindestens 80% der erwarteten Größe)
            if (fileInfo.Length >= expectedSize * 0.8)
            {
                // Prüfen ob es eine gültige GGUF-Datei ist
                if (IsValidGgufFile(modelPath))
                {
                    lock (_stateLock)
                    {
                        _currentModelPath = modelPath;
                        _currentPreset = preset;
                    }

                    _logger.Debug($"LLM-Modell verfügbar: {preset} ({modelPath})", "LlmModelManager");
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Prüft, ob ein benutzerdefiniertes Modell verfügbar ist.
    /// </summary>
    public bool CheckCustomModelAvailability(string? customPath)
    {
        if (string.IsNullOrWhiteSpace(customPath))
        {
            return false;
        }

        if (!File.Exists(customPath))
        {
            return false;
        }

        var fileInfo = new FileInfo(customPath);
        if (fileInfo.Length < MinimumModelSizeBytes)
        {
            return false;
        }

        if (!IsValidGgufFile(customPath))
        {
            return false;
        }

        lock (_stateLock)
        {
            _currentModelPath = customPath;
            _currentPreset = LlmModelPreset.Custom;
        }

        _logger.Debug($"Custom LLM-Modell verfügbar: {customPath}", "LlmModelManager");
        return true;
    }

    /// <summary>
    /// Gibt den Pfad zum Modell für ein Preset zurück.
    /// </summary>
    public static string? GetModelPath(LlmModelPreset preset)
    {
        var fileName = preset.GetFileName();
        if (fileName is null)
        {
            return null;
        }

        return Path.Combine(Constants.LlmModelsFolder, fileName);
    }

    /// <summary>
    /// Lädt ein LLM-Modell herunter.
    /// </summary>
    public async Task<bool> DownloadAsync(
        LlmModelPreset preset,
        IProgress<LlmModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (preset == LlmModelPreset.Custom)
        {
            _logger.Warning("Kann Custom-Preset nicht herunterladen", "LlmModelManager");
            return false;
        }

        var url = preset.GetDownloadUrl();
        var modelPath = GetModelPath(preset);

        if (url is null || modelPath is null)
        {
            progress?.Report(LlmModelDownloadProgress.Error("Ungültiges Preset"));
            return false;
        }

        Directory.CreateDirectory(Constants.LlmModelsFolder);

        var tempPath = modelPath + ".download";
        var displayName = preset.GetDisplayName();

        try
        {
            progress?.Report(new LlmModelDownloadProgress(0, $"Starte Download von {displayName}..."));

            CleanupTempFile(tempPath);

            _logger.Info($"Starte LLM-Modell-Download: {preset} von {url}", "LlmModelManager");

            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var expectedBytes = preset.GetApproximateSizeBytes();
            var totalBytes = response.Content.Headers.ContentLength ?? expectedBytes;
            var downloadedBytes = 0L;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            {
                var buffer = new byte[81920];
                int bytesRead;

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

                        progress?.Report(LlmModelDownloadProgress.Downloading(
                            percent,
                            displayName,
                            downloadedBytes,
                            totalBytes));

                        lastReportMs = nowMs;
                    }
                }

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Sanity-Check: Datei sollte weitgehend vollständig sein
            try
            {
                var fi = new FileInfo(tempPath);
                if (!fi.Exists || fi.Length < expectedBytes * 0.8)
                {
                    CleanupTempFile(tempPath);
                    var errorMsg = "Download unvollständig oder beschädigt. Bitte erneut versuchen.";
                    _logger.Warning(errorMsg, "LlmModelManager");
                    progress?.Report(LlmModelDownloadProgress.Error(errorMsg));
                    return false;
                }
            }
            catch
            {
                // Wenn wir nicht prüfen können, machen wir weiter
            }

            // GGUF-Magic prüfen
            if (!IsValidGgufFile(tempPath))
            {
                CleanupTempFile(tempPath);
                var errorMsg = "Heruntergeladene Datei ist keine gültige GGUF-Datei.";
                _logger.Warning(errorMsg, "LlmModelManager");
                progress?.Report(LlmModelDownloadProgress.Error(errorMsg));
                return false;
            }

            // SHA256 prüfen falls vorhanden
            if (!VerifySha256IfProvided(preset, tempPath, progress))
            {
                CleanupTempFile(tempPath);
                return false;
            }

            // Umbenennen
            if (File.Exists(modelPath))
            {
                try
                {
                    File.Replace(tempPath, modelPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch
                {
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
                _currentPreset = preset;
            }

            _logger.Info($"LLM-Modell erfolgreich heruntergeladen: {preset}", "LlmModelManager");
            progress?.Report(LlmModelDownloadProgress.Completed(totalBytes));

            return true;
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(tempPath);
            _logger.Debug("LLM-Modell-Download abgebrochen", "LlmModelManager");
            throw;
        }
        catch (Exception ex)
        {
            CleanupTempFile(tempPath);
            var errorMsg = $"Download fehlgeschlagen: {ex.Message}";
            _logger.Error(errorMsg, "LlmModelManager", ex);
            progress?.Report(LlmModelDownloadProgress.Error(errorMsg));
            return false;
        }
    }

    /// <summary>
    /// Entfernt alle heruntergeladenen Modelle außer dem angegebenen.
    /// </summary>
    public void RemoveOtherModels(LlmModelPreset keepPreset)
    {
        var presets = Enum.GetValues<LlmModelPreset>();
        foreach (var preset in presets)
        {
            if (preset == keepPreset || preset == LlmModelPreset.Custom)
            {
                continue;
            }

            var path = GetModelPath(preset);
            if (path is null)
            {
                continue;
            }

            var tempPath = path + ".download";

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    _logger.Debug($"LLM-Modell entfernt: {preset}", "LlmModelManager");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Konnte LLM-Modell nicht entfernen: {ex.Message}", "LlmModelManager");
            }

            CleanupTempFile(tempPath);
        }
    }

    /// <summary>
    /// Entfernt ein bestimmtes Modell.
    /// </summary>
    public bool RemoveModel(LlmModelPreset preset)
    {
        if (preset == LlmModelPreset.Custom)
        {
            return false;
        }

        var path = GetModelPath(preset);
        if (path is null)
        {
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.Info($"LLM-Modell entfernt: {preset}", "LlmModelManager");

                lock (_stateLock)
                {
                    if (_currentPreset == preset)
                    {
                        _currentModelPath = null;
                        _currentPreset = null;
                    }
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Konnte LLM-Modell nicht entfernen: {ex.Message}", "LlmModelManager");
        }

        return false;
    }

    private static bool IsValidGgufFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[4];
            var bytesRead = fs.Read(buffer, 0, 4);

            if (bytesRead < 4)
            {
                return false;
            }

            return buffer[0] == GgufMagic[0]
                   && buffer[1] == GgufMagic[1]
                   && buffer[2] == GgufMagic[2]
                   && buffer[3] == GgufMagic[3];
        }
        catch
        {
            return false;
        }
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

    private bool VerifySha256IfProvided(
        LlmModelPreset preset,
        string filePath,
        IProgress<LlmModelDownloadProgress>? progress)
    {
        var expectedHash = GetExpectedSha256(preset);
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return true;
        }

        try
        {
            if (!VerifySha256(filePath, expectedHash, out var actualHash))
            {
                var errorMsg = $"LLM-Download Hash-Mismatch. Erwartet: {expectedHash}, erhalten: {actualHash}";
                _logger.Warning(errorMsg, "LlmModelManager");
                progress?.Report(LlmModelDownloadProgress.Error(errorMsg));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            var errorMsg = $"LLM-Download konnte nicht validiert werden: {ex.Message}";
            _logger.Warning(errorMsg, "LlmModelManager");
            progress?.Report(LlmModelDownloadProgress.Error(errorMsg));
            return false;
        }
    }

    private static string? GetExpectedSha256(LlmModelPreset preset)
    {
        var key = $"DCM_LLM_SHA256_{preset.ToString().ToUpperInvariant()}";
        return Environment.GetEnvironmentVariable(key)
               ?? Environment.GetEnvironmentVariable("DCM_LLM_SHA256");
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
}
