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
    private readonly FFmpegManager _ffmpegManager;
    private readonly WhisperModelManager _whisperModelManager;
    private readonly TranscriptionPostProcessor _postProcessor;
    private readonly SemaphoreSlim _transcriptionLock = new(1, 1);
    private readonly object _factoryLock = new();

    private WhisperFactory? _whisperFactory;
    private string? _loadedModelPath;
    private bool _disposed;

    public TranscriptionService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
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
                _whisperModelManager.ModelPath!,
                _whisperModelManager.ModelSize!.Value);
        }

        return new DependencyStatus
        {
            FFmpegAvailable = _ffmpegManager.IsAvailable,
            FFmpegPath = _ffmpegManager.FFmpegPath,
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
        // FFmpeg prüfen und ggf. herunterladen
        if (!_ffmpegManager.CheckAvailability())
        {
            var ffmpegSuccess = await _ffmpegManager.DownloadAsync(progress, cancellationToken);
            if (!ffmpegSuccess)
            {
                return false;
            }
        }

        // FFMpegCore konfigurieren
        ConfigureFFmpeg();

        // Whisper-Modell prüfen und ggf. herunterladen
        if (!_whisperModelManager.CheckAvailability(modelSize))
        {
            var whisperSuccess = await _whisperModelManager.DownloadAsync(
                modelSize,
                progress,
                cancellationToken);

            if (!whisperSuccess)
            {
                return false;
            }
        }

        return true;
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

        var stopwatch = Stopwatch.StartNew();
        string? audioFilePath = null;

        // Nur ein Transkriptionsvorgang gleichzeitig
        await _transcriptionLock.WaitAsync(cancellationToken);

        try
        {
            progress?.Report(TranscriptionProgress.Initializing("Bereite Transkription vor..."));

            // FFmpeg konfigurieren
            ConfigureFFmpeg();

            // Audio extrahieren
            progress?.Report(TranscriptionProgress.ExtractingAudio(0));
            audioFilePath = await ExtractAudioAsync(videoFilePath, progress, cancellationToken);

            if (audioFilePath is null)
            {
                return TranscriptionResult.Failed("Audio-Extraktion fehlgeschlagen.", stopwatch.Elapsed);
            }

            // Transkribieren - jetzt mit Segmenten
            progress?.Report(TranscriptionProgress.Transcribing(0));
            var segments = await TranscribeAudioToSegmentsAsync(
                audioFilePath,
                language,
                progress,
                cancellationToken);

            // Post-Processing
            progress?.Report(new TranscriptionProgress(
                TranscriptionPhase.Transcribing,
                99,
                "Formatiere Text..."));

            var text = _postProcessor.Process(segments);

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

    private void ConfigureFFmpeg()
    {
        if (_ffmpegManager.FFmpegPath is null)
        {
            return;
        }

        var ffmpegDir = Path.GetDirectoryName(_ffmpegManager.FFmpegPath);
        if (ffmpegDir is not null)
        {
            GlobalFFOptions.Configure(options =>
            {
                options.BinaryFolder = ffmpegDir;
            });
        }
    }

    private async Task<string?> ExtractAudioAsync(
        string videoFilePath,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Sicherstellen, dass der Ordner existiert
        var tempFolder = Constants.TranscriptionTempFolder;
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
                .ProcessAsynchronously();

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

    private async Task<List<TranscriptionSegment>> TranscribeAudioToSegmentsAsync(
        string audioFilePath,
        string? language,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var factory = GetOrCreateWhisperFactory();

        if (factory is null)
        {
            throw new InvalidOperationException("Whisper-Factory konnte nicht erstellt werden.");
        }

        // whisper.cpp nutzt intern standardmäßig Beam Search mit beam_size=5
        var builder = factory.CreateBuilder()
            .WithThreads(Math.Max(2, Environment.ProcessorCount - 1));

        if (!string.IsNullOrEmpty(language))
        {
            builder.WithLanguage(language);
        }
        else
        {
            builder.WithLanguageDetection();
        }

        using var processor = builder.Build();

        var segments = new List<TranscriptionSegment>();
        var audioDuration = await GetAudioDurationAsync(audioFilePath);
        var totalSeconds = audioDuration?.TotalSeconds ?? 1;
        var transcriptionStartTime = DateTime.UtcNow;

        await using var fileStream = File.OpenRead(audioFilePath);

        await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
        {
            segments.Add(new TranscriptionSegment
            {
                Text = segment.Text,
                Start = segment.Start,
                End = segment.End
            });

            var currentTime = segment.End.TotalSeconds;
            var percent = Math.Min(98, currentTime / totalSeconds * 100);

            TimeSpan? estimatedRemaining = null;
            if (percent > 5)
            {
                var elapsed = DateTime.UtcNow - transcriptionStartTime;
                var estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / (percent / 100));
                estimatedRemaining = estimatedTotal - elapsed;

                if (estimatedRemaining < TimeSpan.Zero)
                {
                    estimatedRemaining = null;
                }
            }

            progress?.Report(TranscriptionProgress.Transcribing(percent, estimatedRemaining));
        }

        return segments;
    }

    private WhisperFactory? GetOrCreateWhisperFactory()
    {
        var currentModelPath = _whisperModelManager.ModelPath;

        if (currentModelPath is null)
        {
            return null;
        }

        lock (_factoryLock)
        {
            // Prüfen ob sich das Modell geändert hat
            if (_whisperFactory is not null && _loadedModelPath == currentModelPath)
            {
                return _whisperFactory;
            }

            // Alte Factory disposen falls vorhanden
            _whisperFactory?.Dispose();

            // Neue Factory erstellen
            _whisperFactory = WhisperFactory.FromPath(currentModelPath);
            _loadedModelPath = currentModelPath;

            return _whisperFactory;
        }
    }

    private static async Task<TimeSpan?> GetAudioDurationAsync(string audioFilePath)
    {
        try
        {
            var mediaInfo = await FFProbe.AnalyseAsync(audioFilePath);
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
    }
}