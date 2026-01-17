using System.Diagnostics;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Transcription.Internal;
using DCM.Transcription.PostProcessing;
using FFMpegCore;
using Whisper.net;

namespace DCM.Transcription;

/// <summary>
/// Implementierung des Transkriptions-Services mit Whisper.net.
/// </summary>
public sealed class TranscriptionService : ITranscriptionService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    private readonly FFmpegManager _ffmpegManager;
    private readonly WhisperModelManager _whisperModelManager;
    private readonly TranscriptionPostProcessor _postProcessor;

    private readonly SemaphoreSlim _transcriptionLock = new(1, 1);
    private readonly object _factoryLock = new();

    private WhisperFactory? _whisperFactory;
    private string? _loadedModelPath;

    private string? _configuredFfmpegDir;
    private bool _disposed;

    public TranscriptionService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;

        _ffmpegManager = new FFmpegManager(_httpClient);
        _whisperModelManager = new WhisperModelManager(_httpClient);
        _postProcessor = new TranscriptionPostProcessor();

        // Initial prüfen was verfügbar ist
        _ffmpegManager.CheckAvailability();
        _whisperModelManager.FindLargestAvailableModel();
    }

    /// <inheritdoc />
    public bool IsReady => _ffmpegManager.IsAvailable && _whisperModelManager.IsAvailable;

    /// <inheritdoc />
    public DependencyStatus GetDependencyStatus()
    {
        // Aktuellen Status prüfen
        _ffmpegManager.CheckAvailability();
        _whisperModelManager.FindLargestAvailableModel();

        if (_ffmpegManager.IsAvailable && _whisperModelManager.IsAvailable)
        {
            return DependencyStatus.AllReady(
                _ffmpegManager.FFmpegPath!,
                _ffmpegManager.FFprobePath,
                _whisperModelManager.ModelPath!,
                _whisperModelManager.ModelSize!.Value);
        }

        return new DependencyStatus
        {
            FFmpegAvailable = _ffmpegManager.IsAvailable,
            FFmpegPath = _ffmpegManager.FFmpegPath,
            FFprobePath = _ffmpegManager.FFprobePath,
            WhisperModelAvailable = _whisperModelManager.IsAvailable,
            WhisperModelPath = _whisperModelManager.ModelPath,
            InstalledModelSize = _whisperModelManager.ModelSize
        };
    }

    /// <inheritdoc />
    public async Task<bool> EnsureDependenciesAsync(
        WhisperModelSize modelSize,
        IProgress<DependencyDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // FFmpeg prüfen und ggf. herunterladen
        if (!_ffmpegManager.CheckAvailability())
        {
            var ffmpegSuccess = await _ffmpegManager.DownloadAsync(progress, cancellationToken).ConfigureAwait(false);
            if (!ffmpegSuccess)
            {
                return false;
            }

            // Flags/Paths updaten
            _ffmpegManager.CheckAvailability();
        }

        // FFMpegCore konfigurieren (nur wenn sich der Ordner geändert hat)
        ConfigureFFmpeg();

        // Whisper-Modell prüfen und ggf. herunterladen
        if (!_whisperModelManager.CheckAvailability(modelSize))
        {
            var whisperSuccess = await _whisperModelManager.DownloadAsync(
                modelSize,
                progress,
                cancellationToken).ConfigureAwait(false);

            if (!whisperSuccess)
            {
                return false;
            }

            // Flags/Paths updaten
            _whisperModelManager.CheckAvailability(modelSize);
        }

        return true;
    }

    public void RemoveOtherModels(WhisperModelSize keepSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Sicherheitsnetz: Nicht während aktiver Transkription löschen.
        // (kompatibel, aber verhindert Dateikollisionen)
        if (!_transcriptionLock.Wait(0))
        {
            return;
        }

        try
        {
            _whisperModelManager.RemoveOtherModels(keepSize);
        }
        finally
        {
            _transcriptionLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(
        string videoFilePath,
        string? language = null,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(videoFilePath))
        {
            return TranscriptionResult.Failed($"Videodatei nicht gefunden: {videoFilePath}");
        }

        if (!IsReady)
        {
            return TranscriptionResult.Failed(
                "Transkriptions-Service ist nicht bereit. Bitte zuerst EnsureDependenciesAsync aufrufen.");
        }

        // ModelPath einmal capturen, damit der Lauf stabil bleibt, selbst wenn parallel irgendwo am Modell gerührt wird.
        var modelPath = _whisperModelManager.ModelPath;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return TranscriptionResult.Failed("Kein Whisper-Modell verfügbar.");
        }

        var stopwatch = Stopwatch.StartNew();
        string? audioFilePath = null;

        // Nur ein Transkriptionsvorgang gleichzeitig
        await _transcriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            progress?.Report(TranscriptionProgress.Initializing("Bereite Transkription vor..."));

            // FFmpeg konfigurieren
            ConfigureFFmpeg();

            // Audio extrahieren
            progress?.Report(TranscriptionProgress.ExtractingAudio(0));
            audioFilePath = await ExtractAudioAsync(videoFilePath, progress, cancellationToken).ConfigureAwait(false);

            if (audioFilePath is null)
            {
                return TranscriptionResult.Failed("Audio-Extraktion fehlgeschlagen.", stopwatch.Elapsed);
            }

            // Transkribieren - jetzt komplett im Hintergrund
            progress?.Report(TranscriptionProgress.Transcribing(0));
            var text = await TranscribeAudioInBackgroundAsync(
                audioFilePath,
                modelPath,
                language,
                progress,
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(text))
            {
                return TranscriptionResult.Failed(
                    "Transkription ergab keinen Text.",
                    stopwatch.Elapsed);
            }

            progress?.Report(TranscriptionProgress.Completed());

            return TranscriptionResult.Ok(text, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return TranscriptionResult.Cancelled(stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            progress?.Report(TranscriptionProgress.Failed(ex.Message));
            return TranscriptionResult.Failed(ex.Message, stopwatch.Elapsed);
        }
        finally
        {
            _transcriptionLock.Release();

            // Temporäre Audio-Datei aufräumen
            CleanupTempFile(audioFilePath);
        }
    }

    private async Task<string> TranscribeAudioInBackgroundAsync(
        string audioFilePath,
        string modelPath,
        string? language,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Audio-Dauer vorab (I/O) – unabhängig davon, ob wir den eigentlichen Whisper-Run im Threadpool machen.
        var audioDuration = await GetAudioDurationAsync(audioFilePath).ConfigureAwait(false);
        var totalSeconds = audioDuration?.TotalSeconds ?? 1;

        // Gesamte Transkription und Post-Processing im Hintergrund-Task
        return await Task.Run(async () =>
        {
            var factory = GetOrCreateWhisperFactory(modelPath);
            if (factory is null)
            {
                throw new InvalidOperationException("Whisper-Factory konnte nicht erstellt werden.");
            }

            // whisper.cpp nutzt intern standardmäßig Beam Search mit beam_size=5
            var threadCount = Math.Max(2, Environment.ProcessorCount - 1);
            var builder = factory.CreateBuilder().WithThreads(threadCount);

            if (!string.IsNullOrEmpty(language))
            {
                builder.WithLanguage(language);
            }
            else
            {
                builder.WithLanguageDetection();
            }

            using var processor = builder.Build();

            var segments = await TranscribeProcessorToSegmentsAsync(
                processor,
                audioFilePath,
                totalSeconds,
                progress,
                cancellationToken).ConfigureAwait(false);

            // Post-Processing ebenfalls im Hintergrund
            progress?.Report(new TranscriptionProgress(
                TranscriptionPhase.Transcribing,
                99,
                "Formatiere Text..."));

            return _postProcessor.Process(segments);

        }, cancellationToken).ConfigureAwait(false);
    }

    // FIX: NICHT dynamic, sonst CS8416 bei await foreach
    private async Task<List<TranscriptionSegment>> TranscribeProcessorToSegmentsAsync(
        WhisperProcessor processor,
        string audioFilePath,
        double totalSeconds,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var segments = new List<TranscriptionSegment>();

        // Stabileres Timing fürs Throttling/ETA
        var transcriptionStopwatch = Stopwatch.StartNew();
        var progressStopwatch = Stopwatch.StartNew();
        long lastProgressMs = 0;
        var segmentsSinceLastReport = 0;

        await using var fileStream = new FileStream(
            audioFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
        {
            segments.Add(new TranscriptionSegment
            {
                Text = segment.Text,
                Start = segment.Start,
                End = segment.End
            });

            segmentsSinceLastReport++;

            // Progress-Throttling: Nur alle 500ms ODER alle 10 Segmente reporten
            var nowMs = progressStopwatch.ElapsedMilliseconds;
            if ((nowMs - lastProgressMs) >= 500 || segmentsSinceLastReport >= 10)
            {
                var currentTime = segment.End.TotalSeconds;
                var percent = Math.Min(98, currentTime / totalSeconds * 100);

                TimeSpan? estimatedRemaining = null;
                if (percent > 5)
                {
                    var elapsed = transcriptionStopwatch.Elapsed;
                    var estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / (percent / 100));
                    estimatedRemaining = estimatedTotal - elapsed;

                    if (estimatedRemaining < TimeSpan.Zero)
                    {
                        estimatedRemaining = null;
                    }
                }

                progress?.Report(TranscriptionProgress.Transcribing(percent, estimatedRemaining));

                lastProgressMs = nowMs;
                segmentsSinceLastReport = 0;
            }
        }

        return segments;
    }

    private void ConfigureFFmpeg()
    {
        var ffmpegExePath = _ffmpegManager.FFmpegPath;
        if (ffmpegExePath is null)
        {
            return;
        }

        var ffmpegDir = Path.GetDirectoryName(ffmpegExePath);
        if (ffmpegDir is null)
        {
            return;
        }

        // GlobalFFOptions ist global – nur neu setzen, wenn es sich wirklich geändert hat.
        if (string.Equals(_configuredFfmpegDir, ffmpegDir, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _configuredFfmpegDir = ffmpegDir;

        GlobalFFOptions.Configure(options =>
        {
            options.BinaryFolder = ffmpegDir;
        });
    }

    private async Task<string?> ExtractAudioAsync(
        string videoFilePath,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Sicherstellen, dass der Ordner existiert
        var tempFolder = Constants.TranscriptionTempFolder;
        Directory.CreateDirectory(tempFolder);

        var audioFileName = $"{Guid.NewGuid():N}.wav";
        var audioFilePath = Path.Combine(tempFolder, audioFileName);

        try
        {
            // Whisper erwartet: 16kHz, Mono, 16-bit PCM WAV
            // speechnorm normalisiert die Lautstärke für bessere Transkriptionsqualität
            await FFMpegArguments
                .FromFileInput(videoFilePath)
                .OutputToFile(audioFilePath, overwrite: true, options => options
                    .WithAudioSamplingRate(16000)
                    .WithCustomArgument("-acodec pcm_s16le") // 16-bit PCM
                    .WithCustomArgument("-ac 1") // Mono
                    .WithCustomArgument("-vn") // Kein Video
                    .WithCustomArgument("-af speechnorm=e=12.5:r=0.0001:l=1") // Audio-Normalisierung
                    .ForceFormat("wav"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously().ConfigureAwait(false);

            progress?.Report(TranscriptionProgress.ExtractingAudio(100));

            return File.Exists(audioFilePath) ? audioFilePath : null;
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(audioFilePath);
            throw;
        }
        catch
        {
            CleanupTempFile(audioFilePath);
            return null;
        }
    }

    // Beibehalten (intern), damit vorhandene Aufrufer stabil bleiben (falls später genutzt).
    private async Task<List<TranscriptionSegment>> TranscribeAudioToSegmentsAsync(
        string audioFilePath,
        string? language,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var modelPath = _whisperModelManager.ModelPath;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new InvalidOperationException("Kein Whisper-Modell verfügbar.");
        }

        var factory = GetOrCreateWhisperFactory(modelPath);
        if (factory is null)
        {
            throw new InvalidOperationException("Whisper-Factory konnte nicht erstellt werden.");
        }

        var threadCount = Math.Max(2, Environment.ProcessorCount - 1);
        var builder = factory.CreateBuilder().WithThreads(threadCount);

        if (!string.IsNullOrEmpty(language))
        {
            builder.WithLanguage(language);
        }
        else
        {
            builder.WithLanguageDetection();
        }

        using var processor = builder.Build();

        var audioDuration = await GetAudioDurationAsync(audioFilePath).ConfigureAwait(false);
        var totalSeconds = audioDuration?.TotalSeconds ?? 1;

        return await TranscribeProcessorToSegmentsAsync(
            processor,
            audioFilePath,
            totalSeconds,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private WhisperFactory? GetOrCreateWhisperFactory()
    {
        var currentModelPath = _whisperModelManager.ModelPath;
        return string.IsNullOrWhiteSpace(currentModelPath) ? null : GetOrCreateWhisperFactory(currentModelPath);
    }

    private WhisperFactory? GetOrCreateWhisperFactory(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return null;
        }

        lock (_factoryLock)
        {
            // Prüfen ob sich das Modell geändert hat
            if (_whisperFactory is not null && string.Equals(_loadedModelPath, modelPath, StringComparison.Ordinal))
            {
                return _whisperFactory;
            }

            // Alte Factory disposen falls vorhanden
            _whisperFactory?.Dispose();

            // Neue Factory erstellen
            _whisperFactory = WhisperFactory.FromPath(modelPath);
            _loadedModelPath = modelPath;

            return _whisperFactory;
        }
    }

    private static async Task<TimeSpan?> GetAudioDurationAsync(string audioFilePath)
    {
        try
        {
            var mediaInfo = await FFProbe.AnalyseAsync(audioFilePath).ConfigureAwait(false);
            return mediaInfo.Duration;
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupTempFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Ignorieren - Temp-Ordner wird eh irgendwann aufgeräumt
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_factoryLock)
        {
            _whisperFactory?.Dispose();
            _whisperFactory = null;
        }

        _transcriptionLock.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
