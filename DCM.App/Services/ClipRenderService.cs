using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DCM.Core.Logging;
using DCM.Transcription.PostProcessing;
using DCM.Core;
using DCM.Core.Models;
using DCM.Core.Services;
using FFMpegCore;

namespace DCM.App.Services;

/// <summary>
/// Service für das Rendern von Video-Clips mit FFmpeg.
/// </summary>
public sealed class ClipRenderService : IClipRenderService
{
    private readonly ASSSubtitleGenerator _subtitleGenerator = new();
    private readonly IFaceDetectionService _faceDetectionService;
    private readonly DraftTranscriptStore _segmentStore;
    private readonly IAppLogger _logger;
    private string? _ffmpegPath;
    private string? _ffmpegDir;
    private bool _isReady;

    public ClipRenderService() : this(null, null, null)
    {
    }

    internal ClipRenderService(
        DraftTranscriptStore? segmentStore = null,
        IFaceDetectionService? faceDetectionService = null,
        IAppLogger? logger = null)
    {
        _segmentStore = segmentStore ?? new DraftTranscriptStore();
        _faceDetectionService = faceDetectionService ?? new FaceAiSharpDetectionService();
        _logger = logger ?? AppLogger.Instance;
    }

    /// <inheritdoc/>
    public bool IsReady => _isReady;

    /// <inheritdoc/>
    public bool TryInitialize()
    {
        // FFmpeg im App-Ordner oder PATH suchen
        _ffmpegPath = FindFFmpeg();

        if (_ffmpegPath is null)
        {
            _isReady = false;
            return false;
        }

        _ffmpegDir = Path.GetDirectoryName(_ffmpegPath);

        // FFMpegCore konfigurieren
        if (_ffmpegDir is not null)
        {
            GlobalFFOptions.Configure(options =>
            {
                options.BinaryFolder = _ffmpegDir;
            });
        }

        _isReady = true;
        return true;
    }

    /// <inheritdoc/>
    public async Task<ClipRenderResult> RenderClipAsync(
        ClipRenderJob job,
        IProgress<ClipRenderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Phase: Vorbereitung
            progress?.Report(new ClipRenderProgress
            {
                JobId = job.Id,
                Phase = ClipRenderPhase.Pending,
                PhaseProgress = 0,
                TotalProgress = 0,
                StatusMessage = "Vorbereitung..."
            });

            // Validierung
            if (!File.Exists(job.SourceVideoPath))
            {
                return ClipRenderResult.Fail(job.Id, "Quell-Video nicht gefunden.", stopwatch.Elapsed);
            }

            // Ausgabeordner erstellen
            var outputDir = Path.GetDirectoryName(job.OutputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var videoInfo = await GetVideoInfoAsync(job.SourceVideoPath, cancellationToken);
            var sourceWidth = videoInfo?.Width ?? 0;
            var sourceHeight = videoInfo?.Height ?? 0;

            CropRegionResult? cropRegion = null;
            if (job.CropMode != CropMode.None && job.OutputWidth > 0 && job.OutputHeight > 0 && sourceWidth > 0 && sourceHeight > 0)
            {
                cropRegion = await ResolveCropRegionAsync(
                    job,
                    sourceWidth,
                    sourceHeight,
                    progress,
                    cancellationToken);
            }

            // Temporärer Ausgabepfad
            var tempOutputPath = job.OutputPath + ".tmp.mp4";

            try
            {
                // Phase: Untertitel generieren (falls aktiviert)
                string? subtitlePath = null;
                if (job.BurnSubtitles && job.SubtitleSegments is { Count: > 0 })
                {
                    progress?.Report(new ClipRenderProgress
                    {
                        JobId = job.Id,
                        Phase = ClipRenderPhase.SubtitleGeneration,
                        PhaseProgress = 0,
                        TotalProgress = 10,
                        StatusMessage = "Untertitel werden generiert..."
                    });

                    subtitlePath = await GenerateSubtitleFileAsync(job, sourceWidth, sourceHeight, cancellationToken);
                }
                else if (job.BurnSubtitles && !string.IsNullOrEmpty(job.SubtitlePath) && File.Exists(job.SubtitlePath))
                {
                    subtitlePath = job.SubtitlePath;
                }
                else if (job.BurnSubtitles)
                {
                    var clipSegments = BuildSubtitleSegmentsFromStore(job);
                    if (clipSegments is { Count: > 0 })
                    {
                        job.SubtitleSegments = clipSegments;
                        progress?.Report(new ClipRenderProgress
                        {
                            JobId = job.Id,
                            Phase = ClipRenderPhase.SubtitleGeneration,
                            PhaseProgress = 0,
                            TotalProgress = 10,
                            StatusMessage = "Untertitel werden generiert..."
                        });
                        subtitlePath = await GenerateSubtitleFileAsync(job, sourceWidth, sourceHeight, cancellationToken);
                    }
                }

                // Phase: Rendering
                progress?.Report(new ClipRenderProgress
                {
                    JobId = job.Id,
                    Phase = ClipRenderPhase.VideoRendering,
                    PhaseProgress = 0,
                    TotalProgress = 20,
                    StatusMessage = "Video wird gerendert..."
                });

                var renderSuccess = await RenderWithFFmpegAsync(
                    job,
                    tempOutputPath,
                    subtitlePath,
                    cropRegion,
                    sourceWidth,
                    sourceHeight,
                    progress,
                    cancellationToken);

                if (!renderSuccess)
                {
                    CleanupTempFile(tempOutputPath);
                    CleanupTempFile(subtitlePath);
                    return ClipRenderResult.Fail(job.Id, "FFmpeg-Rendering fehlgeschlagen.", stopwatch.Elapsed);
                }

                // Phase: Finalisierung
                progress?.Report(new ClipRenderProgress
                {
                    JobId = job.Id,
                    Phase = ClipRenderPhase.PostProcessing,
                    PhaseProgress = 0,
                    TotalProgress = 95,
                    StatusMessage = "Finalisierung..."
                });

                // Temp-Datei zur finalen Datei verschieben
                if (File.Exists(job.OutputPath))
                {
                    File.Delete(job.OutputPath);
                }
                File.Move(tempOutputPath, job.OutputPath);

                // Untertitel-Datei aufräumen
                CleanupTempFile(subtitlePath);

                // Dateigröße ermitteln
                var fileInfo = new FileInfo(job.OutputPath);
                var fileSize = fileInfo.Exists ? fileInfo.Length : 0;

                progress?.Report(ClipRenderProgress.Completed(job.Id));

                return ClipRenderResult.Ok(
                    job.Id,
                    job.OutputPath,
                    stopwatch.Elapsed,
                    fileSize);
            }
            catch (OperationCanceledException)
            {
                CleanupTempFile(tempOutputPath);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new ClipRenderProgress
            {
                JobId = job.Id,
                Phase = ClipRenderPhase.Cancelled,
                PhaseProgress = 0,
                TotalProgress = 0,
                StatusMessage = "Abgebrochen"
            });
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report(ClipRenderProgress.Failed(job.Id, ex.Message));
            return ClipRenderResult.Fail(job.Id, ex.Message, stopwatch.Elapsed);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ClipRenderResult>> RenderClipsAsync(
        IReadOnlyList<ClipRenderJob> jobs,
        IProgress<ClipBatchRenderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ClipRenderResult>(jobs.Count);

        for (var i = 0; i < jobs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = jobs[i];
            var jobIndex = i;

            // Fortschritt für diesen Job
            var jobProgress = progress is not null
                ? new Progress<ClipRenderProgress>(p =>
                {
                    progress.Report(new ClipBatchRenderProgress
                    {
                        CurrentJobIndex = jobIndex,
                        TotalJobs = jobs.Count,
                        CurrentJobProgress = p
                    });
                })
                : null;

            var result = await RenderClipAsync(job, jobProgress, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    /// <inheritdoc/>
    public ClipRenderJob CreateJobFromCandidate(
        ClipCandidate candidate,
        string sourceVideoPath,
        string outputFolder,
        bool convertToPortrait = false)
    {
        // Ausgabedateiname generieren
        var sourceFileName = Path.GetFileNameWithoutExtension(sourceVideoPath);
        var timestamp = candidate.StartFormatted.Replace(":", "-");
        var outputFileName = $"{sourceFileName}_clip_{timestamp}.mp4";
        var outputPath = Path.Combine(outputFolder, outputFileName);

        var job = new ClipRenderJob
        {
            CandidateId = candidate.Id,
            Candidate = candidate,
            SourceDraftId = candidate.SourceDraftId,
            SourceVideoPath = sourceVideoPath,
            OutputPath = outputPath,
            StartTime = candidate.Start,
            EndTime = candidate.End
        };

        if (convertToPortrait)
        {
            job.OutputWidth = 1080;
            job.OutputHeight = 1920;
            job.CropMode = CropMode.AutoDetect;
        }
        else
        {
            // Landscape beibehalten - keine Größenänderung
            job.OutputWidth = 0;
            job.OutputHeight = 0;
            job.CropMode = CropMode.None;
        }

        return job;
    }

    private async Task<bool> RenderWithFFmpegAsync(
        ClipRenderJob job,
        string outputPath,
        string? subtitlePath,
        CropRegionResult? cropRegion,
        int sourceWidth,
        int sourceHeight,
        IProgress<ClipRenderProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startSeconds = job.StartTime.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
        var durationSeconds = job.Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

        try
        {
            var arguments = FFMpegArguments
                .FromFileInput(job.SourceVideoPath, verifyExists: true, options => options
                    .WithCustomArgument($"-ss {startSeconds}"));

            var processor = arguments.OutputToFile(outputPath, overwrite: true, options =>
            {
                // Dauer
                options.WithCustomArgument($"-t {durationSeconds}");

                // Video-Filter zusammenbauen
                var videoFilters = BuildVideoFilters(job, cropRegion, subtitlePath);
                if (!string.IsNullOrEmpty(videoFilters))
                {
                    options.WithCustomArgument($"-vf \"{videoFilters}\"");
                }

                // Video-Codec und Qualität
                options.WithVideoCodec(job.VideoCodec);
                options.WithCustomArgument($"-crf {job.VideoQuality}");

                // Preset für Geschwindigkeit/Qualität Balance
                options.WithCustomArgument("-preset medium");

                // Audio
                options.WithAudioCodec(job.AudioCodec);
                options.WithAudioBitrate(job.AudioBitrate);

                // Für bessere Kompatibilität
                options.WithCustomArgument("-movflags +faststart");
                options.WithCustomArgument("-pix_fmt yuv420p");
            });

            // Fortschritts-Tracking über Duration-basiertes Polling
            var clipDuration = job.Duration;
            var lastProgressReport = DateTime.UtcNow;

            await processor
                .CancellableThrough(cancellationToken)
                .NotifyOnProgress(percent =>
                {
                    // Throttle progress reports
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressReport).TotalMilliseconds < 100)
                    {
                        return;
                    }
                    lastProgressReport = now;

                    var totalProgress = 20 + (percent * 0.75); // 20-95%
                    progress?.Report(new ClipRenderProgress
                    {
                        JobId = job.Id,
                        Phase = ClipRenderPhase.VideoRendering,
                        PhaseProgress = percent,
                        TotalProgress = Math.Min(95, totalProgress),
                        StatusMessage = $"Rendering... {percent:F0}%"
                    });
                }, clipDuration)
                .ProcessAsynchronously();

            return File.Exists(outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildVideoFilters(
        ClipRenderJob job,
        CropRegionResult? cropRegion,
        string? subtitlePath)
    {
        var filters = new List<string>();

        // Crop/Scale für Portrait-Format
        if (job.OutputWidth > 0 && job.OutputHeight > 0)
        {
            if (job.CropMode == CropMode.None || cropRegion is null)
            {
                // Nur skalieren ohne Crop
                filters.Add($"scale={job.OutputWidth}:{job.OutputHeight}:force_original_aspect_ratio=decrease");
                filters.Add($"pad={job.OutputWidth}:{job.OutputHeight}:(ow-iw)/2:(oh-ih)/2");
            }
            else
            {
                filters.Add(cropRegion.ToFfmpegCropFilter());
                filters.Add($"scale={job.OutputWidth}:{job.OutputHeight}:force_original_aspect_ratio=decrease");
                filters.Add($"pad={job.OutputWidth}:{job.OutputHeight}:(ow-iw)/2:(oh-ih)/2");
            }
        }

        // Untertitel einbrennen
        if (!string.IsNullOrEmpty(subtitlePath) && File.Exists(subtitlePath))
        {
            // Pfad für FFmpeg escapen (Backslashes und Doppelpunkte)
            var escapedPath = subtitlePath
                .Replace("\\", "/")
                .Replace(":", "\\:");

            filters.Add($"ass='{escapedPath}'");
        }

        return string.Join(",", filters);
    }

    private async Task<string?> GenerateSubtitleFileAsync(
        ClipRenderJob job,
        int sourceWidth,
        int sourceHeight,
        CancellationToken cancellationToken)
    {
        if (job.SubtitleSegments is null || job.SubtitleSegments.Count == 0)
        {
            return null;
        }

        var subtitleFolder = Constants.ClipSubtitlesFolder;
        Directory.CreateDirectory(subtitleFolder);

        var subtitlePath = Path.Combine(subtitleFolder, $"{job.Id:N}.ass");

        try
        {
            var settings = job.SubtitleSettings ?? new ClipSubtitleSettings();
            var playResX = job.OutputWidth > 0 ? job.OutputWidth : (sourceWidth > 0 ? sourceWidth : 1080);
            var playResY = job.OutputHeight > 0 ? job.OutputHeight : (sourceHeight > 0 ? sourceHeight : 1920);
            var assContent = _subtitleGenerator.Generate(job.SubtitleSegments, settings, playResX, playResY);
            await File.WriteAllTextAsync(subtitlePath, assContent, Encoding.UTF8, cancellationToken);
            return subtitlePath;
        }
        catch
        {
            return null;
        }
    }

    private async Task<CropRegionResult?> ResolveCropRegionAsync(
        ClipRenderJob job,
        int sourceWidth,
        int sourceHeight,
        IProgress<ClipRenderProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (job.CropMode == CropMode.Center)
        {
            progress?.Report(new ClipRenderProgress
            {
                JobId = job.Id,
                Phase = ClipRenderPhase.CropCalculation,
                PhaseProgress = 0,
                TotalProgress = 10,
                StatusMessage = "Crop-Region wird berechnet..."
            });
            return CropRegionResult.CreateCenterCrop(sourceWidth, sourceHeight);
        }

        if (job.CropMode == CropMode.Manual)
        {
            progress?.Report(new ClipRenderProgress
            {
                JobId = job.Id,
                Phase = ClipRenderPhase.CropCalculation,
                PhaseProgress = 0,
                TotalProgress = 10,
                StatusMessage = "Crop-Region wird berechnet..."
            });
            return CreateManualCrop(sourceWidth, sourceHeight, job.OutputWidth, job.OutputHeight, job.ManualCropOffsetX);
        }

        if (job.CropMode == CropMode.AutoDetect && _faceDetectionService.IsAvailable)
        {
            progress?.Report(new ClipRenderProgress
            {
                JobId = job.Id,
                Phase = ClipRenderPhase.FaceDetection,
                PhaseProgress = 0,
                TotalProgress = 5,
                StatusMessage = "Gesichter werden analysiert..."
            });

            var analyses = await _faceDetectionService.AnalyzeVideoAsync(
                job.SourceVideoPath,
                TimeSpan.FromSeconds(FaceDetectionDefaults.SampleIntervalSeconds),
                job.StartTime,
                job.EndTime,
                cancellationToken);

            progress?.Report(new ClipRenderProgress
            {
                JobId = job.Id,
                Phase = ClipRenderPhase.CropCalculation,
                PhaseProgress = 0,
                TotalProgress = 10,
                StatusMessage = "Crop-Region wird berechnet..."
            });

            return _faceDetectionService.CalculateCropRegion(
                analyses,
                new PixelSize(sourceWidth, sourceHeight),
                new PixelSize(job.OutputWidth, job.OutputHeight),
                CropStrategy.MultipleFaces);
        }

        return CropRegionResult.CreateCenterCrop(sourceWidth, sourceHeight);
    }

    private IReadOnlyList<ClipSubtitleSegment>? BuildSubtitleSegmentsFromStore(ClipRenderJob job)
    {
        if (job.SourceDraftId == Guid.Empty)
        {
            return null;
        }

        var segments = _segmentStore.LoadSegments(job.SourceDraftId);
        if (segments is null || segments.Count == 0)
        {
            return null;
        }

        return BuildSubtitleSegments(segments, job.StartTime, job.EndTime);
    }

    private static IReadOnlyList<ClipSubtitleSegment> BuildSubtitleSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        TimeSpan clipStart,
        TimeSpan clipEnd)
    {
        var list = new List<ClipSubtitleSegment>();

        foreach (var segment in segments)
        {
            var start = segment.Start < clipStart ? clipStart : segment.Start;
            var end = segment.End > clipEnd ? clipEnd : segment.End;
            if (end <= start)
            {
                continue;
            }

            List<ClipSubtitleWord>? words = null;
            string? text = null;

            if (segment.Words is not null && segment.Words.Count > 0)
            {
                words = new List<ClipSubtitleWord>();
                var wordTexts = new List<string>();

                foreach (var word in segment.Words)
                {
                    if (word.End <= clipStart || word.Start >= clipEnd)
                    {
                        continue;
                    }

                    var wordStart = word.Start < clipStart ? clipStart : word.Start;
                    var wordEnd = word.End > clipEnd ? clipEnd : word.End;
                    if (wordEnd <= wordStart)
                    {
                        continue;
                    }

                    words.Add(new ClipSubtitleWord
                    {
                        Text = word.Text,
                        Start = wordStart - clipStart,
                        End = wordEnd - clipStart
                    });
                    if (!string.IsNullOrWhiteSpace(word.Text))
                    {
                        wordTexts.Add(word.Text);
                    }
                }

                if (words.Count == 0)
                {
                    continue;
                }

                text = wordTexts.Count > 0 ? string.Join(" ", wordTexts) : segment.Text;
            }
            else
            {
                text = segment.Text;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            list.Add(new ClipSubtitleSegment
            {
                Text = text,
                Start = start - clipStart,
                End = end - clipStart,
                Words = words
            });
        }

        return list;
    }

    private static CropRegionResult CreateManualCrop(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        double offsetX)
    {
        var ratio = targetHeight > 0 ? (double)targetWidth / targetHeight : 9.0 / 16.0;
        var cropWidth = (int)Math.Round(sourceHeight * ratio);
        if (cropWidth > sourceWidth)
        {
            cropWidth = sourceWidth;
        }

        var maxShift = Math.Max(0, (sourceWidth - cropWidth) / 2.0);
        var centerX = sourceWidth / 2.0 + Math.Clamp(offsetX, -1.0, 1.0) * maxShift;
        var cropX = (int)Math.Round(centerX - cropWidth / 2.0);
        cropX = Math.Clamp(cropX, 0, sourceWidth - cropWidth);

        return new CropRegionResult
        {
            Region = new CropRectangle(cropX, 0, cropWidth, sourceHeight),
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            Strategy = CropStrategy.CenterFallback,
            BasedOnFaceDetection = false
        };
    }

    private async Task<(int Width, int Height)?> GetVideoInfoAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = await FFProbe.AnalyseAsync(path).ConfigureAwait(false);
            var stream = info.PrimaryVideoStream;
            if (stream is null)
            {
                return null;
            }

            return (stream.Width, stream.Height);
        }
        catch (Exception ex)
        {
            _logger.Warning($"FFProbe fehlgeschlagen: {ex.Message}", "ClipRender");
            return null;
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

    private static void CleanupTempFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

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
