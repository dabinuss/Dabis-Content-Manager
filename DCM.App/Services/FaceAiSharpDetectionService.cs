using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DCM.Core;
using DCM.Core.Models;
using DCM.Core.Services;
using FaceAiSharp;
using FaceAiSharp.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp;
using FFMpegCore;

namespace DCM.App.Services;

/// <summary>
/// Face-Detection-Service auf Basis von FaceAiSharp (SCRFD).
/// </summary>
public sealed class FaceAiSharpDetectionService : IFaceDetectionService, IDisposable
{
    private const string ModelFileName = "scrfd_2.5g_kps_640x640.onnx";
    private static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "DCM_FaceDetection");

    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly Lazy<IFaceDetector?> _detector;
    private string? _modelPath;
    private string? _ffmpegPath;
    private string? _ffmpegDir;
    private bool _disposed;

    public FaceAiSharpDetectionService()
    {
        _detector = new Lazy<IFaceDetector?>(CreateDetector, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => !_disposed && EnsureFfmpegAvailable() && _detector.Value is not null;

    public async Task<IReadOnlyList<FrameFaceAnalysis>> AnalyzeVideoAsync(
        string videoPath,
        TimeSpan sampleInterval,
        TimeSpan? startTime,
        TimeSpan? endTime,
        CancellationToken ct)
    {
        if (_disposed)
        {
            return Array.Empty<FrameFaceAnalysis>();
        }

        if (!IsAvailable || string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            return Array.Empty<FrameFaceAnalysis>();
        }

        var duration = await GetVideoDurationAsync(videoPath, ct).ConfigureAwait(false);
        if (!duration.HasValue || duration.Value <= TimeSpan.Zero)
        {
            return Array.Empty<FrameFaceAnalysis>();
        }

        var start = startTime ?? TimeSpan.Zero;
        var end = endTime ?? duration.Value;
        if (start < TimeSpan.Zero)
        {
            start = TimeSpan.Zero;
        }
        if (end > duration.Value)
        {
            end = duration.Value;
        }
        if (end <= start)
        {
            return Array.Empty<FrameFaceAnalysis>();
        }

        var interval = sampleInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(FaceDetectionDefaults.SampleIntervalSeconds)
            : sampleInterval;

        var timestamps = BuildSampleSchedule(start, end, interval, FaceDetectionDefaults.MaxSamples);
        if (timestamps.Count == 0)
        {
            return Array.Empty<FrameFaceAnalysis>();
        }

        Directory.CreateDirectory(TempFolder);

        var frames = new List<(TimeSpan Timestamp, byte[] Data)>(timestamps.Count);
        foreach (var timestamp in timestamps)
        {
            ct.ThrowIfCancellationRequested();
            var bytes = await ExtractFrameAsync(videoPath, timestamp, ct).ConfigureAwait(false);
            if (bytes is not null)
            {
                frames.Add((timestamp, bytes));
            }
        }

        if (frames.Count == 0)
        {
            return Array.Empty<FrameFaceAnalysis>();
        }

        var analyses = new ConcurrentBag<FrameFaceAnalysis>();
        var options = new ParallelOptions
        {
            CancellationToken = ct,
            // Leave CPU headroom so the WPF UI stays responsive during auto-detect.
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 3)
        };

        await Parallel.ForEachAsync(frames, options, (frame, token) =>
        {
            var faces = DetectFacesInFrame(frame.Data);
            analyses.Add(new FrameFaceAnalysis
            {
                Timestamp = frame.Timestamp,
                Faces = faces
            });
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        return analyses.OrderBy(a => a.Timestamp).ToList();
    }

    public CropRegionResult CalculateCropRegion(
        IReadOnlyList<FrameFaceAnalysis> analyses,
        PixelSize sourceSize,
        PixelSize targetSize,
        CropStrategy preferredStrategy = CropStrategy.MultipleFaces)
    {
        var faces = analyses.SelectMany(a => a.Faces).ToList();

        if (faces.Count == 0)
        {
            return CropRegionResult.CreateCenterCrop(sourceSize.Width, sourceSize.Height);
        }

        var targetRatio = targetSize.Height > 0
            ? (double)targetSize.Width / targetSize.Height
            : 9.0 / 16.0;

        var targetWidth = (int)Math.Round(sourceSize.Height * targetRatio);
        if (targetWidth > sourceSize.Width)
        {
            targetWidth = sourceSize.Width;
        }

        var clusters = ClusterFaces(faces, FaceDetectionDefaults.ClusterDistancePixels);
        var dominant = clusters.OrderByDescending(c => c.Count).First();

        double centerX;
        CropStrategy strategy;

        if (clusters.Count == 1 || dominant.Count >= faces.Count * 0.8)
        {
            strategy = CropStrategy.SingleFace;
            centerX = MedianCenterX(dominant.Faces);
        }
        else
        {
            var minCenter = clusters.Min(c => c.CenterX);
            var maxCenter = clusters.Max(c => c.CenterX);
            var combinedWidth = maxCenter - minCenter;

            if (combinedWidth <= targetWidth * 1.2)
            {
                strategy = preferredStrategy == CropStrategy.DominantFace
                    ? CropStrategy.DominantFace
                    : CropStrategy.MultipleFaces;
                centerX = (minCenter + maxCenter) / 2.0;
            }
            else
            {
                strategy = CropStrategy.DominantFace;
                var largestCluster = clusters.OrderByDescending(c => c.AverageArea).First();
                centerX = MedianCenterX(largestCluster.Faces);
            }
        }

        var cropX = (int)Math.Round(centerX - targetWidth / 2.0);
        cropX = Math.Clamp(cropX, 0, sourceSize.Width - targetWidth);

        var region = new CropRectangle(cropX, 0, targetWidth, sourceSize.Height);

        return new CropRegionResult
        {
            Region = region,
            SourceWidth = sourceSize.Width,
            SourceHeight = sourceSize.Height,
            Strategy = strategy,
            FacesConsidered = faces.Count,
            BasedOnFaceDetection = true,
            DebugInfo = $"Faces={faces.Count}, Clusters={clusters.Count}, Strategy={strategy}"
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_detector.IsValueCreated && _detector.Value is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _memoryCache.Dispose();
    }

    private IFaceDetector? CreateDetector()
    {
        _modelPath = ResolveModelPath();
        if (string.IsNullOrWhiteSpace(_modelPath))
        {
            return null;
        }

        var options = new ScrfdDetectorOptions
        {
            ModelPath = _modelPath,
            AutoResizeInputToModelDimensions = true,
            ConfidenceThreshold = FaceDetectionDefaults.MinConfidence,
            NonMaxSupressionThreshold = 0.4f
        };

        return new ScrfdDetector(_memoryCache, options, new SessionOptions());
    }

    private IReadOnlyList<FaceDetectionResult> DetectFacesInFrame(byte[] jpegData)
    {
        var detector = _detector.Value;
        if (detector is null)
        {
            return Array.Empty<FaceDetectionResult>();
        }

        using var image = Image.Load(jpegData);
        var detections = detector.Detect(image);

        if (detections is null || detections.Count == 0)
        {
            return Array.Empty<FaceDetectionResult>();
        }

        var results = new List<FaceDetectionResult>(detections.Count);
        foreach (var detection in detections)
        {
            var confidence = detection.Confidence ?? 1.0f;
            if (confidence < FaceDetectionDefaults.MinConfidence)
            {
                continue;
            }

            var box = detection.Box;
            var landmarks = detection.Landmarks?
                .Select(p => new FacePoint(p.X, p.Y))
                .ToArray() ?? Array.Empty<FacePoint>();

            results.Add(new FaceDetectionResult
            {
                BoundingBox = new FaceRect(box.X, box.Y, box.Width, box.Height),
                Landmarks = landmarks,
                Confidence = confidence
            });
        }

        return results;
    }

    private static List<FaceCluster> ClusterFaces(
        IReadOnlyList<FaceDetectionResult> faces,
        int distanceThreshold)
    {
        var clusters = new List<FaceCluster>();

        foreach (var face in faces)
        {
            var centerX = face.BoundingBox.CenterX;
            var centerY = face.BoundingBox.CenterY;

            FaceCluster? assigned = null;
            foreach (var cluster in clusters)
            {
                var dx = cluster.CenterX - centerX;
                var dy = cluster.CenterY - centerY;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance <= distanceThreshold)
                {
                    assigned = cluster;
                    break;
                }
            }

            if (assigned is null)
            {
                clusters.Add(new FaceCluster(face));
            }
            else
            {
                assigned.Add(face);
            }
        }

        return clusters;
    }

    private static double MedianCenterX(IReadOnlyList<FaceDetectionResult> faces)
    {
        var centers = faces.Select(f => (double)f.BoundingBox.CenterX).OrderBy(x => x).ToArray();
        if (centers.Length == 0)
        {
            return 0;
        }

        var mid = centers.Length / 2;
        if (centers.Length % 2 == 0)
        {
            return (centers[mid - 1] + centers[mid]) / 2.0;
        }
        return centers[mid];
    }

    private static List<TimeSpan> BuildSampleSchedule(
        TimeSpan start,
        TimeSpan end,
        TimeSpan interval,
        int maxSamples)
    {
        var duration = end - start;
        if (duration <= TimeSpan.Zero)
        {
            return new List<TimeSpan>();
        }

        var count = (int)Math.Floor(duration.TotalMilliseconds / interval.TotalMilliseconds) + 1;
        if (count > maxSamples)
        {
            var adjustedInterval = TimeSpan.FromTicks(duration.Ticks / Math.Max(1, maxSamples - 1));
            interval = adjustedInterval <= TimeSpan.Zero ? interval : adjustedInterval;
            count = maxSamples;
        }

        var list = new List<TimeSpan>(count);
        for (var i = 0; i < count; i++)
        {
            var ts = start + TimeSpan.FromTicks(interval.Ticks * i);
            if (ts > end)
            {
                ts = end;
            }
            list.Add(ts);
        }

        return list;
    }

    private async Task<byte[]?> ExtractFrameAsync(string videoPath, TimeSpan timestamp, CancellationToken ct)
    {
        if (!EnsureFfmpegAvailable())
        {
            return null;
        }

        var tempPath = Path.Combine(TempFolder, $"{Guid.NewGuid():N}.jpg");
        var timestampArg = timestamp.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

        try
        {
            await FFMpegArguments
                .FromFileInput(videoPath, verifyExists: true, options => options
                    .WithCustomArgument($"-ss {timestampArg}"))
                .OutputToFile(tempPath, overwrite: true, options => options
                    .WithCustomArgument("-threads 1")
                    .WithCustomArgument("-frames:v 1")
                    .WithCustomArgument("-q:v 2")
                    .ForceFormat("mjpeg"))
                .CancellableThrough(ct)
                .ProcessAsynchronously()
                .ConfigureAwait(false);

            if (!File.Exists(tempPath))
            {
                return null;
            }

            return await File.ReadAllBytesAsync(tempPath, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task<TimeSpan?> GetVideoDurationAsync(string videoPath, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var info = await FFProbe.AnalyseAsync(videoPath).ConfigureAwait(false);
            return info.Duration;
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveModelPath()
    {
        var baseDir = AppContext.BaseDirectory ?? string.Empty;
        var direct = Path.Combine(baseDir, "onnx", ModelFileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        var fallback = Path.Combine(baseDir, ModelFileName);
        if (File.Exists(fallback))
        {
            return fallback;
        }

        try
        {
            return Directory.EnumerateFiles(baseDir, ModelFileName, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private bool EnsureFfmpegAvailable()
    {
        if (_ffmpegPath is not null && _ffmpegDir is not null)
        {
            return true;
        }

        _ffmpegPath = FindFFmpeg();
        if (_ffmpegPath is null)
        {
            return false;
        }

        _ffmpegDir = Path.GetDirectoryName(_ffmpegPath);
        if (_ffmpegDir is null)
        {
            return false;
        }

        GlobalFFOptions.Configure(options => options.BinaryFolder = _ffmpegDir);
        return true;
    }

    private static string? FindFFmpeg()
    {
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
                // ignore
            }
        }

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
                // ignore
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
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
            // ignore
        }
    }

    private sealed class FaceCluster
    {
        private float _centerX;
        private float _centerY;
        private float _areaSum;

        public FaceCluster(FaceDetectionResult face)
        {
            Faces = new List<FaceDetectionResult> { face };
            _centerX = face.BoundingBox.CenterX;
            _centerY = face.BoundingBox.CenterY;
            _areaSum = face.BoundingBox.Area;
        }

        public List<FaceDetectionResult> Faces { get; }

        public int Count => Faces.Count;

        public float CenterX => _centerX;

        public float CenterY => _centerY;

        public float AverageArea => Count > 0 ? _areaSum / Count : 0;

        public void Add(FaceDetectionResult face)
        {
            Faces.Add(face);
            var count = Faces.Count;
            _centerX = (_centerX * (count - 1) + face.BoundingBox.CenterX) / count;
            _centerY = (_centerY * (count - 1) + face.BoundingBox.CenterY) / count;
            _areaSum += face.BoundingBox.Area;
        }
    }
}
