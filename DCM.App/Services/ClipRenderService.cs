using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
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
    private string? _ffmpegPath;
    private string? _ffmpegDir;
    private bool _isReady;

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

                    subtitlePath = await GenerateSubtitleFileAsync(job, cancellationToken);
                }
                else if (job.BurnSubtitles && !string.IsNullOrEmpty(job.SubtitlePath) && File.Exists(job.SubtitlePath))
                {
                    subtitlePath = job.SubtitlePath;
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
                var videoFilters = BuildVideoFilters(job, subtitlePath);
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

    private static string BuildVideoFilters(ClipRenderJob job, string? subtitlePath)
    {
        var filters = new List<string>();

        // Crop/Scale für Portrait-Format
        if (job.OutputWidth > 0 && job.OutputHeight > 0)
        {
            if (job.CropMode == CropMode.None)
            {
                // Nur skalieren ohne Crop
                filters.Add($"scale={job.OutputWidth}:{job.OutputHeight}:force_original_aspect_ratio=decrease");
                filters.Add($"pad={job.OutputWidth}:{job.OutputHeight}:(ow-iw)/2:(oh-ih)/2");
            }
            else
            {
                // Center Crop für Portrait
                // Erst auf Höhe skalieren, dann horizontal croppen
                filters.Add($"scale=-2:{job.OutputHeight}");
                filters.Add($"crop={job.OutputWidth}:{job.OutputHeight}");
            }
        }

        // Untertitel einbrennen
        if (!string.IsNullOrEmpty(subtitlePath) && File.Exists(subtitlePath))
        {
            // Pfad für FFmpeg escapen (Backslashes und Doppelpunkte)
            var escapedPath = subtitlePath
                .Replace("\\", "/")
                .Replace(":", "\\:");

            filters.Add($"subtitles='{escapedPath}'");
        }

        return string.Join(",", filters);
    }

    private async Task<string?> GenerateSubtitleFileAsync(ClipRenderJob job, CancellationToken cancellationToken)
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
            var playResX = job.OutputWidth > 0 ? job.OutputWidth : 1920;
            var playResY = job.OutputHeight > 0 ? job.OutputHeight : 1080;
            var assContent = _subtitleGenerator.Generate(job.SubtitleSegments, settings, playResX, playResY);
            await File.WriteAllTextAsync(subtitlePath, assContent, Encoding.UTF8, cancellationToken);
            return subtitlePath;
        }
        catch
        {
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
