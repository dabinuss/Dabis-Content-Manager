using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Erzeugt Highlight-Kandidatenfenster aus zeitgestempelten Segmenten.
/// </summary>
public sealed class CandidateWindowGenerator
{
    public const int MinDurationSeconds = 15;
    public const int MaxDurationSeconds = 90;
    public const int WindowStepSeconds = 10;
    private static readonly TimeSpan PauseThreshold = TimeSpan.FromSeconds(2);

    public IReadOnlyList<CandidateWindow> GenerateWindows(
        IReadOnlyList<ITimedSegment> segments,
        TimeSpan? minDuration = null,
        TimeSpan? maxDuration = null,
        TimeSpan? stepDuration = null)
    {
        if (segments is null || segments.Count == 0)
        {
            return Array.Empty<CandidateWindow>();
        }

        var ordered = segments.OrderBy(s => s.Start).ToList();
        var min = minDuration ?? TimeSpan.FromSeconds(MinDurationSeconds);
        var max = maxDuration ?? TimeSpan.FromSeconds(MaxDurationSeconds);
        var step = stepDuration ?? TimeSpan.FromSeconds(WindowStepSeconds);

        if (min <= TimeSpan.Zero || max <= TimeSpan.Zero || max < min || step <= TimeSpan.Zero)
        {
            return Array.Empty<CandidateWindow>();
        }

        var windows = new List<CandidateWindow>();
        var totalDuration = ordered[^1].End;
        var windowStart = TimeSpan.Zero;
        var startIndex = 0;

        while (windowStart < totalDuration)
        {
            while (startIndex < ordered.Count && ordered[startIndex].End <= windowStart)
            {
                startIndex++;
            }

            if (startIndex >= ordered.Count)
            {
                break;
            }

            var windowSegments = new List<ITimedSegment>();
            TimeSpan? actualStart = null;
            var actualEnd = TimeSpan.Zero;
            var index = startIndex;

            while (index < ordered.Count)
            {
                var segment = ordered[index];
                if (segment.End <= windowStart)
                {
                    index++;
                    continue;
                }

                if (actualStart is null)
                {
                    actualStart = segment.Start;
                }

                if (windowSegments.Count > 0)
                {
                    var gap = segment.Start - actualEnd;
                    var currentDuration = actualEnd - actualStart.Value;
                    if (gap > PauseThreshold && currentDuration >= min)
                    {
                        break;
                    }
                }

                windowSegments.Add(segment);
                actualEnd = segment.End;

                var duration = actualEnd - actualStart.Value;
                if (duration >= max)
                {
                    break;
                }

                index++;
            }

            if (windowSegments.Count > 0 && actualStart.HasValue)
            {
                var duration = actualEnd - actualStart.Value;
                if (duration >= min && duration <= max)
                {
                    var text = string.Join(" ", windowSegments.Select(s => s.Text.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)));

                    windows.Add(new CandidateWindow
                    {
                        Index = windows.Count,
                        Start = actualStart.Value,
                        End = actualEnd,
                        Text = text,
                        Segments = windowSegments,
                        StartSegmentIndex = startIndex,
                        EndSegmentIndex = startIndex + windowSegments.Count - 1
                    });
                }
            }

            windowStart += step;
        }

        return DeduplicateWindows(windows);
    }

    private static IReadOnlyList<CandidateWindow> DeduplicateWindows(List<CandidateWindow> windows)
    {
        if (windows.Count <= 1)
        {
            return windows;
        }

        var deduplicated = new List<CandidateWindow>();
        var active = new List<CandidateWindow>();
        var sorted = windows.OrderBy(w => w.Start).ThenBy(w => w.Duration).ToList();

        foreach (var window in sorted)
        {
            active.RemoveAll(existing => existing.End <= window.Start);

            var isDuplicate = active.Any(existing =>
            {
                var overlapStart = TimeSpan.FromTicks(Math.Max(existing.Start.Ticks, window.Start.Ticks));
                var overlapEnd = TimeSpan.FromTicks(Math.Min(existing.End.Ticks, window.End.Ticks));
                var overlap = overlapEnd - overlapStart;
                if (overlap <= TimeSpan.Zero)
                {
                    return false;
                }

                var shorter = TimeSpan.FromTicks(Math.Min(existing.Duration.Ticks, window.Duration.Ticks));
                if (shorter <= TimeSpan.Zero)
                {
                    return false;
                }

                return overlap.TotalSeconds / shorter.TotalSeconds > 0.8;
            });

            if (!isDuplicate)
            {
                deduplicated.Add(window);
                active.Add(window);
            }
        }

        for (var i = 0; i < deduplicated.Count; i++)
        {
            deduplicated[i].Index = i;
        }

        return deduplicated;
    }
}
