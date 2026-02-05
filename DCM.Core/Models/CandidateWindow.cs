namespace DCM.Core.Models;

/// <summary>
/// Ein Zeitfenster im Transkript als potentieller Highlight-Kandidat.
/// Wird für die LLM-Analyse verwendet.
/// </summary>
public sealed class CandidateWindow
{
    /// <summary>
    /// Laufender Index des Fensters.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Startzeit des Fensters im Video.
    /// </summary>
    public TimeSpan Start { get; init; }

    /// <summary>
    /// Endzeit des Fensters im Video.
    /// </summary>
    public TimeSpan End { get; init; }

    /// <summary>
    /// Dauer des Fensters.
    /// </summary>
    public TimeSpan Duration => End - Start;

    /// <summary>
    /// Der Transkript-Text in diesem Fenster.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Die enthaltenen Transkript-Segmente.
    /// </summary>
    public required IReadOnlyList<ITimedSegment> Segments { get; init; }

    /// <summary>
    /// Index des ersten Segments in der Gesamtliste.
    /// </summary>
    public int StartSegmentIndex { get; init; }

    /// <summary>
    /// Index des letzten Segments in der Gesamtliste.
    /// </summary>
    public int EndSegmentIndex { get; init; }

    /// <summary>
    /// Anzahl der Wörter im Fenster.
    /// </summary>
    public int WordCount => Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Erstellt CandidateWindows aus einer Liste von Segmenten.
    /// Sliding Window mit überlappenden Fenstern.
    /// </summary>
    /// <param name="segments">Alle Transkript-Segmente.</param>
    /// <param name="minDuration">Minimale Fensterdauer.</param>
    /// <param name="maxDuration">Maximale Fensterdauer.</param>
    /// <param name="stepDuration">Schrittweite für Sliding Window.</param>
    public static IReadOnlyList<CandidateWindow> CreateWindows(
        IReadOnlyList<ITimedSegment> segments,
        TimeSpan minDuration,
        TimeSpan maxDuration,
        TimeSpan stepDuration)
    {
        if (segments.Count == 0)
        {
            return Array.Empty<CandidateWindow>();
        }

        var windows = new List<CandidateWindow>();
        var totalDuration = segments[^1].End;

        var windowStart = TimeSpan.Zero;

        while (windowStart < totalDuration)
        {
            // Versuche verschiedene Fenstergrößen
            foreach (var targetDuration in GetTargetDurations(minDuration, maxDuration))
            {
                var windowEnd = windowStart + targetDuration;
                if (windowEnd > totalDuration)
                {
                    windowEnd = totalDuration;
                }

                // Finde Segmente in diesem Fenster
                var windowSegments = new List<ITimedSegment>();
                int startIdx = -1;
                int endIdx = -1;

                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];
                    // Segment ist im Fenster wenn es überlappt
                    if (seg.End > windowStart && seg.Start < windowEnd)
                    {
                        if (startIdx < 0) startIdx = i;
                        endIdx = i;
                        windowSegments.Add(seg);
                    }
                }

                if (windowSegments.Count > 0)
                {
                    var actualStart = windowSegments[0].Start;
                    var actualEnd = windowSegments[^1].End;
                    var actualDuration = actualEnd - actualStart;

                    // Nur hinzufügen wenn Dauer im erlaubten Bereich
                    if (actualDuration >= minDuration && actualDuration <= maxDuration)
                    {
                        var text = string.Join(" ", windowSegments.Select(s => s.Text.Trim()));

                        windows.Add(new CandidateWindow
                        {
                            Start = actualStart,
                            End = actualEnd,
                            Text = text,
                            Segments = windowSegments,
                            StartSegmentIndex = startIdx,
                            EndSegmentIndex = endIdx
                        });
                    }
                }
            }

            windowStart += stepDuration;
        }

        // Deduplizierung: Entferne sehr ähnliche Fenster
        return DeduplicateWindows(windows);
    }

    private static IEnumerable<TimeSpan> GetTargetDurations(TimeSpan min, TimeSpan max)
    {
        // Generiere Zielgrößen: min, Mitte, max
        yield return min;
        yield return TimeSpan.FromSeconds((min.TotalSeconds + max.TotalSeconds) / 2);
        yield return max;
    }

    private static IReadOnlyList<CandidateWindow> DeduplicateWindows(List<CandidateWindow> windows)
    {
        if (windows.Count <= 1)
        {
            return windows;
        }

        var deduplicated = new List<CandidateWindow>();
        var sorted = windows.OrderBy(w => w.Start).ThenBy(w => w.Duration).ToList();

        foreach (var window in sorted)
        {
            // Prüfen ob ein sehr ähnliches Fenster bereits existiert
            bool isDuplicate = deduplicated.Any(existing =>
            {
                var overlapStart = TimeSpan.FromTicks(Math.Max(existing.Start.Ticks, window.Start.Ticks));
                var overlapEnd = TimeSpan.FromTicks(Math.Min(existing.End.Ticks, window.End.Ticks));
                var overlap = overlapEnd - overlapStart;

                if (overlap <= TimeSpan.Zero) return false;

                // Überlappung > 80% der kürzeren Duration = Duplikat
                var shorterDuration = TimeSpan.FromTicks(Math.Min(existing.Duration.Ticks, window.Duration.Ticks));
                return overlap.TotalSeconds / shorterDuration.TotalSeconds > 0.8;
            });

            if (!isDuplicate)
            {
                deduplicated.Add(window);
            }
        }

        return deduplicated;
    }
}
