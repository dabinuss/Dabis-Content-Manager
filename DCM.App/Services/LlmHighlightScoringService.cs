using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DCM.Core.Logging;
using DCM.Core.Models;
using DCM.Core.Services;

namespace DCM.App.Services;

/// <summary>
/// LLM-basierter Service zur Bewertung von Highlight-Kandidaten.
/// </summary>
public sealed class HighlightScoringService : IHighlightScoringService
{
    private readonly ILlmClient _llmClient;
    private readonly IAppLogger _logger;
    private readonly string? _customPrompt;
    private readonly int _maxCandidates;

    private const int ChunkSize = 3500;
    private const int MinScore = 60;
    private static readonly TimeSpan MinClipDuration = TimeSpan.FromSeconds(CandidateWindowGenerator.MinDurationSeconds);
    private static readonly TimeSpan MaxClipDuration = TimeSpan.FromSeconds(CandidateWindowGenerator.MaxDurationSeconds);

    private const string DefaultScoringPrompt = @"Du bist ein Experte für Social-Media-Content. Analysiere die folgenden 
Transkript-Abschnitte und bewerte sie als potenzielle Highlights für 
TikTok, Instagram Reels und YouTube Shorts.

KONTEXT (falls vorhanden): {contentContext}

BEWERTUNGSKRITERIEN (je 0-25 Punkte):
1. Hook-Qualität: Fesselt der Anfang sofort die Aufmerksamkeit?
2. Klarheit: Ist der Inhalt ohne Vorwissen verständlich?
3. Emotionalität: Löst es eine Reaktion aus (Lachen, Staunen, Nachdenken)?
4. Teilbarkeit: Würde man es teilen oder kommentieren?

KANDIDATEN:
{windowsAsNumberedList}

REGELN:
- Wähle maximal {maxCandidates} Kandidaten mit Score > 60
- Clip-Länge: 15-90 Sekunden
- Du darfst Start/End leicht anpassen für besseren Schnitt
- Keine Überlappungen zwischen Kandidaten
- Bevorzuge Stellen mit klarem Anfang und Ende

ANTWORT als JSON (NUR das Array, kein Markdown):
[
  {
    ""index"": 1,
    ""start"": ""00:01:23.500"",
    ""end"": ""00:02:15.200"",
    ""score"": 85,
    ""reason"": ""Starker Hook, überraschende Wendung, emotional""
  }
]";

    public HighlightScoringService(
        ILlmClient llmClient,
        string? customPrompt = null,
        IAppLogger? logger = null,
        int maxCandidates = 5)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _customPrompt = customPrompt;
        _logger = logger ?? AppLogger.Instance;
        _maxCandidates = Math.Clamp(maxCandidates, 1, 20);
    }

    public bool IsReady => _llmClient.IsReady;

    public bool TryInitialize() => _llmClient.TryInitialize();

    public async Task<IReadOnlyList<ClipCandidate>> ScoreHighlightsAsync(
        Guid draftId,
        IReadOnlyList<CandidateWindow> windows,
        string? contentContext,
        CancellationToken cancellationToken = default)
    {
        if (windows is null || windows.Count == 0)
        {
            _logger.Warning("Keine Fenster für Scoring", "HighlightScoring");
            return Array.Empty<ClipCandidate>();
        }

        if (!TryInitialize())
        {
            _logger.Error("LLM konnte nicht initialisiert werden", "HighlightScoring");
            return Array.Empty<ClipCandidate>();
        }

        var chunks = CreateChunks(windows);

        // Chunks parallel an das LLM schicken (unabhängige Anfragen).
        // Parallelität auf 3 begrenzt, um Rate-Limits zu vermeiden.
        var chunkTasks = new List<Task<List<ClipCandidate>>>();
        var throttle = new SemaphoreSlim(3, 3);

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capturedChunk = chunk;
            chunkTasks.Add(Task.Run(async () =>
            {
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var prompt = BuildPrompt(capturedChunk, contentContext);
                    var response = await _llmClient.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(response) || response.StartsWith("[LLM", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warning("Leere oder Fehler-Antwort vom LLM", "HighlightScoring");
                        return new List<ClipCandidate>();
                    }

                    return ParseLlmResponse(response, capturedChunk, draftId);
                }
                finally
                {
                    throttle.Release();
                }
            }, cancellationToken));
        }

        var chunkResults = await Task.WhenAll(chunkTasks).ConfigureAwait(false);
        throttle.Dispose();
        var scoredCandidates = chunkResults.SelectMany(r => r).ToList();

        if (scoredCandidates.Count == 0)
        {
            _logger.Warning("Keine Kandidaten aus LLM-Antworten", "HighlightScoring");
            return Array.Empty<ClipCandidate>();
        }

        var filtered = scoredCandidates
            .Where(c => c.Score >= MinScore)
            .OrderByDescending(c => c.Score)
            .ToList();

        var selected = new List<ClipCandidate>();
        foreach (var candidate in filtered)
        {
            if (selected.Count >= _maxCandidates)
            {
                break;
            }

            if (selected.Any(existing => Overlaps(existing, candidate)))
            {
                continue;
            }

            selected.Add(candidate);
        }

        _logger.Info($"Highlight-Scoring abgeschlossen: {selected.Count} Kandidaten gefunden", "HighlightScoring");
        return selected;
    }

    private IReadOnlyList<List<CandidateWindow>> CreateChunks(IReadOnlyList<CandidateWindow> windows)
    {
        var chunks = new List<List<CandidateWindow>>();
        var current = new List<CandidateWindow>();
        var currentSize = 0;

        foreach (var window in windows)
        {
            var estimate = (window.Text?.Length ?? 0) + 60;
            if (current.Count > 0 && currentSize + estimate > ChunkSize)
            {
                chunks.Add(current);
                current = new List<CandidateWindow>();
                currentSize = 0;
            }

            current.Add(window);
            currentSize += estimate;
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    private string BuildPrompt(IReadOnlyList<CandidateWindow> windows, string? contentContext)
    {
        var listBuilder = new StringBuilder();
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var index = i + 1;
            listBuilder.AppendLine($"{index}. [{FormatTimestamp(window.Start)} - {FormatTimestamp(window.End)}] {window.Text}");
        }

        var template = _customPrompt ?? DefaultScoringPrompt;
        return template
            .Replace("{contentContext}", string.IsNullOrWhiteSpace(contentContext) ? "-" : contentContext)
            .Replace("{maxCandidates}", _maxCandidates.ToString(CultureInfo.InvariantCulture))
            .Replace("{windowsAsNumberedList}", listBuilder.ToString());
    }

    private List<ClipCandidate> ParseLlmResponse(
        string response,
        IReadOnlyList<CandidateWindow> windows,
        Guid draftId)
    {
        var json = ExtractJsonArray(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.Warning("Keine JSON-Antwort vom LLM", "HighlightScoring");
            return new List<ClipCandidate>();
        }

        var results = new List<ClipCandidate>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("index", out var indexProp))
                {
                    continue;
                }

                var index = indexProp.GetInt32() - 1;
                if (index < 0 || index >= windows.Count)
                {
                    _logger.Warning(
                        $"LLM gab ungültigen Index {index + 1} zurück (Chunk hat {windows.Count} Windows) – Kandidat wird ignoriert",
                        "HighlightScoring");
                    continue;
                }

                var window = windows[index];
                var score = ReadDouble(element, "score");
                var reason = ReadString(element, "reason") ?? string.Empty;

                var start = ReadTimestamp(element, "start") ?? window.Start;
                var end = ReadTimestamp(element, "end") ?? window.End;
                (start, end) = NormalizeCandidateRange(start, end, window);

                if (end <= start)
                {
                    continue;
                }

                results.Add(new ClipCandidate
                {
                    SourceDraftId = draftId,
                    Start = start,
                    End = end,
                    Score = Math.Clamp(score, 0, 100),
                    Reason = reason,
                    PreviewText = TruncateText(window.Text, 200),
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.Warning($"JSON-Parsing fehlgeschlagen: {ex.Message}", "HighlightScoring");
        }

        return results;
    }

    private static string? ExtractJsonArray(string response)
    {
        var start = response.IndexOf('[');
        var end = response.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return response.Substring(start, end - start + 1);
    }

    private static double ReadDouble(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetDouble();
            }

            if (prop.ValueKind == JsonValueKind.String &&
                double.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static TimeSpan? ReadTimestamp(JsonElement element, string name)
    {
        var value = ReadString(element, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[]
        {
            @"hh\:mm\:ss\.fff",
            @"h\:mm\:ss\.fff",
            @"mm\:ss\.fff",
            @"m\:ss\.fff",
            @"hh\:mm\:ss",
            @"h\:mm\:ss",
            @"mm\:ss",
            @"m\:ss"
        };

        return TimeSpan.TryParseExact(value, formats, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool Overlaps(ClipCandidate a, ClipCandidate b)
    {
        return a.Start < b.End && b.Start < a.End;
    }

    private static string FormatTimestamp(TimeSpan timeSpan)
    {
        return timeSpan.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 3)] + "...";
    }

    private static (TimeSpan Start, TimeSpan End) NormalizeCandidateRange(
        TimeSpan start,
        TimeSpan end,
        CandidateWindow window)
    {
        var rangeStart = window.Start;
        var rangeEnd = window.End;

        if (rangeEnd <= rangeStart)
        {
            return (start, end);
        }

        start = start < rangeStart ? rangeStart : start;
        end = end > rangeEnd ? rangeEnd : end;

        if (end <= start)
        {
            return (rangeStart, rangeEnd);
        }

        var duration = end - start;
        if (duration < MinClipDuration)
        {
            end = start + MinClipDuration;
            if (end > rangeEnd)
            {
                end = rangeEnd;
                start = end - MinClipDuration;
                if (start < rangeStart)
                {
                    start = rangeStart;
                }
            }
        }

        duration = end - start;
        if (duration > MaxClipDuration)
        {
            end = start + MaxClipDuration;
            if (end > rangeEnd)
            {
                end = rangeEnd;
                start = end - MaxClipDuration;
                if (start < rangeStart)
                {
                    start = rangeStart;
                }
            }
        }

        if (end <= start)
        {
            return (rangeStart, rangeEnd);
        }

        return (start, end);
    }
}
