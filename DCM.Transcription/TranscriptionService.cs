using System.Diagnostics;
using System.Globalization;
using System.Text;
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

    private static readonly TimeSpan ChunkDuration = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan ChunkOverlap = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan StagnationWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SegmentTimeEpsilon = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ChunkEndTolerance = TimeSpan.FromSeconds(2);
    private const int StagnationSegmentThreshold = 40;
    private const int RepeatedTextStreakThreshold = 10;
    private const int RepeatedTextMinLength = 6;
    private const int MaxBatchChunkExtraction = 60;

    private sealed record TranscriptionDecodeSettings(
        bool ForceNoContext,
        int? MaxLastTextTokens,
        float? Temperature,
        float? TemperatureInc,
        float? LogProbThreshold,
        float? EntropyThreshold,
        float? NoSpeechThreshold,
        bool? UseGreedySampling);

    private sealed record TranscriptionChunkResult(
        List<TranscriptionSegment> Segments,
        bool IsStuck);

    private sealed record AudioChunk(
        TimeSpan Start,
        TimeSpan Duration,
        bool TrimLeadingOverlap);

    private sealed class TranscriptionProgressState
    {
        public TranscriptionProgressState(Stopwatch stopwatch)
        {
            Stopwatch = stopwatch;
        }

        public Stopwatch Stopwatch { get; }
        public double LastPercent { get; set; }
        public long LastReportMs { get; set; }
        public object SyncRoot { get; } = new();
    }

    public TranscriptionService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;

        _ffmpegManager = new FFmpegManager(_httpClient);
        _whisperModelManager = new WhisperModelManager(_httpClient);
        _postProcessor = new TranscriptionPostProcessor(new PostProcessingOptions
        {
            RemoveWordDuplications = false,
            InsertParagraphs = true,
            ParagraphPauseThreshold = TimeSpan.FromSeconds(1.5)
        });

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

            var segments = new List<TranscriptionSegment>();
            var progressState = new TranscriptionProgressState(Stopwatch.StartNew());
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeatTask = RunTranscriptionHeartbeatAsync(progress, progressState, heartbeatCts.Token);

            try
            {
                if (!audioDuration.HasValue || audioDuration.Value <= ChunkDuration)
                {
                    var duration = audioDuration ?? TimeSpan.Zero;
                    var chunkSegments = await TranscribeChunkWithRetryAsync(
                        factory,
                        audioFilePath,
                        language,
                        duration,
                        audioDuration,
                        totalSeconds,
                        progress,
                        progressState,
                        cancellationToken,
                        offset: TimeSpan.Zero).ConfigureAwait(false);

                    segments.AddRange(chunkSegments);
                }
                else
                {
                    var chunks = BuildChunkSchedule(audioDuration.Value);
                    var chunkFiles = chunks.Count > 0 && chunks.Count <= MaxBatchChunkExtraction
                        ? await ExtractAudioChunksAsync(audioFilePath, chunks, cancellationToken).ConfigureAwait(false)
                        : null;

                    try
                    {
                        for (var index = 0; index < chunks.Count; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var chunk = chunks[index];
                            var chunkPath = chunkFiles is not null
                                ? chunkFiles[index]
                                : await ExtractAudioChunkAsync(
                                    audioFilePath,
                                    chunk.Start,
                                    chunk.Duration,
                                    cancellationToken).ConfigureAwait(false);

                            try
                            {
                                if (string.IsNullOrWhiteSpace(chunkPath))
                                {
                                    throw new InvalidOperationException("Audio-Extraktion fehlgeschlagen.");
                                }

                                var chunkSegments = await TranscribeChunkWithRetryAsync(
                                    factory,
                                    chunkPath,
                                    language,
                                    chunk.Duration,
                                    audioDuration,
                                    totalSeconds,
                                    progress,
                                    progressState,
                                    cancellationToken,
                                    chunk.Start).ConfigureAwait(false);

                                if (chunk.TrimLeadingOverlap)
                                {
                                    var trimBefore = chunk.Start + ChunkOverlap;
                                    var filtered = new List<TranscriptionSegment>(chunkSegments.Count);
                                    foreach (var segment in chunkSegments)
                                    {
                                        if (segment.End > trimBefore)
                                        {
                                            filtered.Add(segment);
                                        }
                                    }
                                    chunkSegments = filtered;
                                }

                                segments.AddRange(chunkSegments);
                            }
                            finally
                            {
                                CleanupTempFile(chunkPath);
                            }

                            var chunkPercent = totalSeconds > 0
                                ? Math.Min(98, (chunk.Start + chunk.Duration).TotalSeconds / totalSeconds * 100)
                                : 0;

                            ReportTranscribingProgress(progress, progressState, chunkPercent, estimatedRemaining: null);
                        }
                    }
                    finally
                    {
                        if (chunkFiles is not null)
                        {
                            foreach (var path in chunkFiles)
                            {
                                CleanupTempFile(path);
                            }
                        }
                    }
                }
            }
            finally
            {
                heartbeatCts.Cancel();
                try
                {
                    await heartbeatTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when canceling the heartbeat.
                }
            }

            // Post-Processing ebenfalls im Hintergrund
            progress?.Report(new TranscriptionProgress(
                TranscriptionPhase.Transcribing,
                99,
                "Formatiere Text..."));

            return _postProcessor.Process(segments);

        }, cancellationToken).ConfigureAwait(false);
    }


    private static List<AudioChunk> BuildChunkSchedule(TimeSpan totalDuration)
    {
        var chunks = new List<AudioChunk>();

        if (totalDuration <= ChunkDuration || ChunkDuration <= ChunkOverlap)
        {
            chunks.Add(new AudioChunk(TimeSpan.Zero, totalDuration, false));
            return chunks;
        }

        var step = ChunkDuration - ChunkOverlap;
        var offset = TimeSpan.Zero;
        var index = 0;

        while (offset < totalDuration)
        {
            var remaining = totalDuration - offset;
            var duration = remaining < ChunkDuration ? remaining : ChunkDuration;
            var trimOverlap = index > 0 && ChunkOverlap < duration;
            chunks.Add(new AudioChunk(offset, duration, trimOverlap));
            offset += step;
            index++;
        }

        return chunks;
    }

    private async Task<List<TranscriptionSegment>> TranscribeChunkWithRetryAsync(
        WhisperFactory factory,
        string audioFilePath,
        string? language,
        TimeSpan chunkDuration,
        TimeSpan? overallAudioDuration,
        double totalSeconds,
        IProgress<TranscriptionProgress>? progress,
        TranscriptionProgressState progressState,
        CancellationToken cancellationToken,
        TimeSpan offset)
    {
        var primaryResult = await TranscribeChunkOnceAsync(
            factory,
            audioFilePath,
            language,
            chunkDuration,
            overallAudioDuration,
            totalSeconds,
            progress,
            progressState,
            cancellationToken,
            offset,
            settings: null).ConfigureAwait(false);

        if (!primaryResult.IsStuck)
        {
            return primaryResult.Segments;
        }

        var fallbackSettings = new TranscriptionDecodeSettings(
            ForceNoContext: true,
            MaxLastTextTokens: 128,
            Temperature: 0.4f,
            TemperatureInc: 0.2f,
            LogProbThreshold: -1.0f,
            EntropyThreshold: 2.4f,
            NoSpeechThreshold: 0.3f,
            UseGreedySampling: null);

        var retryResult = await TranscribeChunkOnceAsync(
            factory,
            audioFilePath,
            language,
            chunkDuration,
            overallAudioDuration,
            totalSeconds,
            progress,
            progressState,
            cancellationToken,
            offset,
            settings: fallbackSettings).ConfigureAwait(false);

        if (!retryResult.IsStuck)
        {
            return retryResult.Segments;
        }

        return retryResult.Segments.Count >= primaryResult.Segments.Count
            ? retryResult.Segments
            : primaryResult.Segments;
    }

    private async Task<TranscriptionChunkResult> TranscribeChunkOnceAsync(
        WhisperFactory factory,
        string audioFilePath,
        string? language,
        TimeSpan chunkDuration,
        TimeSpan? overallAudioDuration,
        double totalSeconds,
        IProgress<TranscriptionProgress>? progress,
        TranscriptionProgressState progressState,
        CancellationToken cancellationToken,
        TimeSpan offset,
        TranscriptionDecodeSettings? settings)
    {
        var threadCount = Math.Max(2, Environment.ProcessorCount - 1);
        var builder = factory.CreateBuilder().WithThreads(threadCount);
        var configuredDuration = chunkDuration > TimeSpan.Zero ? chunkDuration : (TimeSpan?)null;

        ConfigureProcessorBuilder(builder, language, overallAudioDuration, settings);

        await using var processor = builder.Build();

        return await TranscribeProcessorToSegmentsAsync(
            processor,
            audioFilePath,
            totalSeconds,
            progress,
            progressState,
            cancellationToken,
            offset,
            configuredDuration).ConfigureAwait(false);
    }

    // FIX: NICHT dynamic, sonst CS8416 bei await foreach
    private async Task<TranscriptionChunkResult> TranscribeProcessorToSegmentsAsync(
        WhisperProcessor processor,
        string audioFilePath,
        double totalSeconds,
        IProgress<TranscriptionProgress>? progress,
        TranscriptionProgressState progressState,
        CancellationToken cancellationToken,
        TimeSpan offset,
        TimeSpan? expectedDuration)
    {
        var segments = new List<TranscriptionSegment>();

        // Stabileres Timing fürs Throttling/ETA
        // Stabileres Timing f�r Throttling/ETA: globaler Stopwatch kommt aus progressState.
        var segmentsSinceLastReport = 0;
        var isStuck = false;
        var lastSegmentEnd = offset;
        TimeSpan? stagnationStart = null;
        var stagnationSegments = 0;
        var lastNormalizedText = string.Empty;
        var repeatedTextStreak = 0;
        var expectedEnd = expectedDuration.HasValue && expectedDuration.Value > TimeSpan.Zero
            ? offset + expectedDuration.Value + ChunkEndTolerance
            : (TimeSpan?)null;

        await using var fileStream = new FileStream(
            audioFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
        {
            var segmentStart = segment.Start + offset;
            var segmentEnd = segment.End + offset;

            if (segments.Count > 0 && segmentEnd + SegmentTimeEpsilon < lastSegmentEnd)
            {
                isStuck = true;
                break;
            }

            if (expectedEnd.HasValue && segmentEnd > expectedEnd.Value)
            {
                isStuck = true;
                break;
            }

            if (stagnationStart is null)
            {
                stagnationStart = segmentEnd;
            }
            else if (segmentEnd - stagnationStart.Value <= StagnationWindow)
            {
                stagnationSegments++;
                if (stagnationSegments >= StagnationSegmentThreshold)
                {
                    isStuck = true;
                    break;
                }
            }
            else
            {
                stagnationStart = segmentEnd;
                stagnationSegments = 0;
            }

            var normalizedText = NormalizeSegmentText(segment.Text ?? string.Empty);
            if (normalizedText.Length >= RepeatedTextMinLength && normalizedText == lastNormalizedText)
            {
                repeatedTextStreak++;
                if (repeatedTextStreak >= RepeatedTextStreakThreshold)
                {
                    isStuck = true;
                    break;
                }
            }
            else
            {
                repeatedTextStreak = 0;
                lastNormalizedText = normalizedText;
            }

            segments.Add(new TranscriptionSegment
            {
                Text = segment.Text ?? string.Empty,
                Start = segmentStart,
                End = segmentEnd
            });

            segmentsSinceLastReport++;
            lastSegmentEnd = segmentEnd;

            // Progress-Throttling: Nur alle 500ms ODER alle 10 Segmente reporten
            var nowMs = progressState.Stopwatch.ElapsedMilliseconds;
            if ((nowMs - progressState.LastReportMs) >= 500 || segmentsSinceLastReport >= 10)
            {
                var currentTime = segmentEnd.TotalSeconds;
                var percent = Math.Min(98, currentTime / totalSeconds * 100);
                if (percent < progressState.LastPercent)
                {
                    percent = progressState.LastPercent;
                }

                TimeSpan? estimatedRemaining = null;
                if (percent > 5)
                {
                    var elapsed = progressState.Stopwatch.Elapsed;
                    var estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / (percent / 100));
                    estimatedRemaining = estimatedTotal - elapsed;

                    if (estimatedRemaining < TimeSpan.Zero)
                    {
                        estimatedRemaining = null;
                    }
                }

                ReportTranscribingProgress(progress, progressState, percent, estimatedRemaining);
                segmentsSinceLastReport = 0;
            }
        }

        return new TranscriptionChunkResult(segments, isStuck);
    }

    private static string NormalizeSegmentText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
            lastWasSpace = false;
        }

        return builder.ToString();
    }

    private static void ReportTranscribingProgress(
        IProgress<TranscriptionProgress>? progress,
        TranscriptionProgressState state,
        double percent,
        TimeSpan? estimatedRemaining)
    {
        if (progress is null)
        {
            return;
        }

        lock (state.SyncRoot)
        {
            if (percent < state.LastPercent)
            {
                percent = state.LastPercent;
            }

            var nowMs = state.Stopwatch.ElapsedMilliseconds;
            var percentDelta = percent - state.LastPercent;
            var elapsedSinceLastReport = nowMs - state.LastReportMs;

            if (elapsedSinceLastReport < 1000 && percentDelta < 0.5)
            {
                return;
            }

            progress.Report(TranscriptionProgress.Transcribing(percent, estimatedRemaining));
            state.LastPercent = percent;
            state.LastReportMs = nowMs;
        }
    }

    private static async Task RunTranscriptionHeartbeatAsync(
        IProgress<TranscriptionProgress>? progress,
        TranscriptionProgressState state,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(2.5);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var percent = state.LastPercent;
            if (percent <= 0 || percent >= 99)
            {
                continue;
            }

            ReportTranscribingProgress(progress, state, percent, estimatedRemaining: null);
        }
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


    private async Task<string?> ExtractAudioChunkAsync(
        string audioFilePath,
        TimeSpan offset,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var tempFolder = Constants.TranscriptionTempFolder;
        Directory.CreateDirectory(tempFolder);

        var audioFileName = $"{Guid.NewGuid():N}.wav";
        var chunkPath = Path.Combine(tempFolder, audioFileName);
        var offsetSeconds = Math.Max(0, offset.TotalSeconds);
        var durationSeconds = Math.Max(0.01, duration.TotalSeconds);
        var offsetArgument = offsetSeconds.ToString(CultureInfo.InvariantCulture);
        var durationArgument = durationSeconds.ToString(CultureInfo.InvariantCulture);

        try
        {
            await FFMpegArguments
                .FromFileInput(audioFilePath, verifyExists: true, options => options
                    .WithCustomArgument($"-ss {offsetArgument}"))
                .OutputToFile(chunkPath, overwrite: true, options => options
                    .WithAudioSamplingRate(16000)
                    .WithCustomArgument("-acodec pcm_s16le") // 16-bit PCM
                    .WithCustomArgument("-ac 1") // Mono
                    .WithCustomArgument("-vn") // Kein Video
                    .WithCustomArgument($"-t {durationArgument}")
                    .ForceFormat("wav"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously().ConfigureAwait(false);

            return File.Exists(chunkPath) ? chunkPath : null;
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(chunkPath);
            throw;
        }
        catch
        {
            CleanupTempFile(chunkPath);
            return null;
        }
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

        var audioDuration = await GetAudioDurationAsync(audioFilePath).ConfigureAwait(false);
        var totalSeconds = audioDuration?.TotalSeconds ?? 1;

        var duration = audioDuration ?? TimeSpan.Zero;
        var progressState = new TranscriptionProgressState(Stopwatch.StartNew());

        return await TranscribeChunkWithRetryAsync(
            factory,
            audioFilePath,
            language,
            duration,
            audioDuration,
            totalSeconds,
            progress,
            progressState,
            cancellationToken,
            offset: TimeSpan.Zero).ConfigureAwait(false);
    }

    private static void ConfigureProcessorBuilder(
        WhisperProcessorBuilder builder,
        string? language,
        TimeSpan? audioDuration,
        TranscriptionDecodeSettings? settings)
    {
        if (!string.IsNullOrEmpty(language))
        {
            builder.WithLanguage(language);
        }
        else
        {
            builder.WithLanguageDetection();
        }

        if (settings?.UseGreedySampling == true)
        {
            builder.WithGreedySamplingStrategy();
        }
        else if (settings?.UseGreedySampling == false)
        {
            builder.WithBeamSearchSamplingStrategy();
        }

        var useNoContext = settings?.ForceNoContext == true
            || (audioDuration.HasValue && audioDuration.Value.TotalMinutes >= 20);

        if (useNoContext)
        {
            // Lange Audios: Kontext begrenzen, um Endlosschleifen bei Wiederholungen zu vermeiden.
            builder.WithNoContext();
            builder.WithMaxLastTextTokens(settings?.MaxLastTextTokens ?? 256);
        }
        else if (settings?.MaxLastTextTokens is not null)
        {
            builder.WithMaxLastTextTokens(settings.MaxLastTextTokens.Value);
        }

        builder.WithTemperature(settings?.Temperature ?? 0f);
        builder.WithTemperatureInc(settings?.TemperatureInc ?? 0.2f);
        builder.WithLogProbThreshold(settings?.LogProbThreshold ?? -1.0f);
        builder.WithEntropyThreshold(settings?.EntropyThreshold ?? 2.4f);
        if (settings?.NoSpeechThreshold is not null)
        {
            builder.WithNoSpeechThreshold(settings.NoSpeechThreshold.Value);
        }
    }

    private async Task<List<string>?> ExtractAudioChunksAsync(
        string audioFilePath,
        IReadOnlyList<AudioChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return new List<string>();
        }

        var ffmpegPath = _ffmpegManager.FFmpegPath;
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return null;
        }

        var tempFolder = Constants.TranscriptionTempFolder;
        Directory.CreateDirectory(tempFolder);

        var outputPaths = new List<string>(chunks.Count);
        var arguments = new StringBuilder();
        arguments.Append("-hide_banner -loglevel error -y -i ");
        arguments.Append(QuotePath(audioFilePath));
        arguments.Append(' ');

        foreach (var chunk in chunks)
        {
            var outputPath = Path.Combine(tempFolder, $"{Guid.NewGuid():N}.wav");
            outputPaths.Add(outputPath);

            var offsetSeconds = Math.Max(0, chunk.Start.TotalSeconds);
            var durationSeconds = Math.Max(0.01, chunk.Duration.TotalSeconds);
            var offsetArgument = offsetSeconds.ToString(CultureInfo.InvariantCulture);
            var durationArgument = durationSeconds.ToString(CultureInfo.InvariantCulture);

            arguments.Append("-ss ").Append(offsetArgument).Append(' ');
            arguments.Append("-t ").Append(durationArgument).Append(' ');
            arguments.Append("-map 0:a:0 -acodec pcm_s16le -ac 1 -ar 16000 -vn ");
            arguments.Append(QuotePath(outputPath)).Append(' ');
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments.ToString(),
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return null;
            }

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore cleanup failures on cancellation.
                }
            });

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(
                process.WaitForExitAsync(cancellationToken),
                outputTask,
                errorTask).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            foreach (var outputPath in outputPaths)
            {
                if (!File.Exists(outputPath))
                {
                    foreach (var path in outputPaths)
                    {
                        CleanupTempFile(path);
                    }

                    return null;
                }
            }

            return outputPaths;
        }
        catch (OperationCanceledException)
        {
            foreach (var outputPath in outputPaths)
            {
                CleanupTempFile(outputPath);
            }

            throw;
        }
        catch
        {
            foreach (var outputPath in outputPaths)
            {
                CleanupTempFile(outputPath);
            }

            return null;
        }
    }

    private static string QuotePath(string path)
        => $"\"{path.Replace("\"", "\\\"")}\"";

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
