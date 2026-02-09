using System.Collections.Concurrent;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using DCM.Core;
using DCM.Transcription.PostProcessing;

namespace DCM.App.Services;

internal sealed class DraftTranscriptStore : IDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly ConcurrentDictionary<Guid, string> _lastHashes = new();
    private readonly ConcurrentDictionary<Guid, DraftTranscriptWrite> _pendingWrites = new();
    private readonly ConcurrentQueue<DraftTranscriptWrite> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    public DraftTranscriptStore()
    {
        _worker = Task.Run(ProcessQueueAsync);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string GetTranscriptPath(Guid draftId)
    {
        return Path.Combine(Constants.DraftTranscriptsFolder, $"{draftId:N}.txt");
    }

    public string GetSegmentsPath(Guid draftId)
    {
        return Path.Combine(Constants.TranscriptSegmentsFolder, $"{draftId:N}.json");
    }

    public string? SaveTranscript(Guid draftId, string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            DeleteTranscript(draftId);
            return null;
        }

        var path = GetTranscriptPath(draftId);

        var hash = ComputeHash(transcript);
        if (_lastHashes.TryGetValue(draftId, out var existingHash)
            && string.Equals(existingHash, hash, StringComparison.Ordinal)
            && File.Exists(path))
        {
            return path;
        }

        var write = new DraftTranscriptWrite(draftId, path, transcript, hash);
        _pendingWrites[draftId] = write;
        _queue.Enqueue(write);
        _signal.Release();
        return path;
    }

    public string? LoadTranscript(Guid draftId, string? storedPath)
    {
        var path = !string.IsNullOrWhiteSpace(storedPath) ? storedPath : GetTranscriptPath(draftId);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return File.ReadAllText(path, Utf8NoBom);
        }
        catch
        {
            return null;
        }
    }

    public void DeleteTranscript(Guid draftId)
    {
        var path = GetTranscriptPath(draftId);
        try
        {
            _pendingWrites.TryRemove(draftId, out _);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
        finally
        {
            _lastHashes.TryRemove(draftId, out _);
        }
    }

    /// <summary>
    /// Speichert Transkript-Segmente mit Zeitstempeln als JSON.
    /// </summary>
    public string? SaveSegments(Guid draftId, IReadOnlyList<TranscriptionSegment>? segments)
    {
        if (segments is null || segments.Count == 0)
        {
            DeleteSegments(draftId);
            return null;
        }

        var path = GetSegmentsPath(draftId);

        try
        {
            Directory.CreateDirectory(Constants.TranscriptSegmentsFolder);

            // Konvertiere zu serialisierbarem Format
            var serializableSegments = segments.Select(s => new SerializableSegment
            {
                Text = s.Text,
                Start = FormatTimestamp(s.Start),
                End = FormatTimestamp(s.End),
                Words = s.Words?.Select(w => new SerializableWord
                {
                    Text = w.Text,
                    Start = FormatTimestamp(w.Start),
                    End = FormatTimestamp(w.End),
                    Probability = w.Probability
                }).ToList()
            }).ToList();

            var json = JsonSerializer.Serialize(serializableSegments, JsonOptions);
            File.WriteAllText(path, json, Utf8NoBom);
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Lädt Transkript-Segmente aus JSON.
    /// </summary>
    public List<TranscriptionSegment>? LoadSegments(Guid draftId, string? storedPath = null)
    {
        var path = !string.IsNullOrWhiteSpace(storedPath) ? storedPath : GetSegmentsPath(draftId);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path, Utf8NoBom);
            var serializableSegments = JsonSerializer.Deserialize<List<SerializableSegment>>(json, JsonOptions);

            if (serializableSegments is null)
            {
                return null;
            }

            var result = new List<TranscriptionSegment>(serializableSegments.Count);

            foreach (var segment in serializableSegments)
            {
                if (!TryResolveTimestamp(segment.Start, segment.StartMs, out var start) ||
                    !TryResolveTimestamp(segment.End, segment.EndMs, out var end))
                {
                    continue;
                }

                var words = segment.Words?
                    .Select(w =>
                    {
                        if (!TryResolveTimestamp(w.Start, w.StartMs, out var wordStart) ||
                            !TryResolveTimestamp(w.End, w.EndMs, out var wordEnd))
                        {
                            return null;
                        }

                        return new TranscriptionWord
                        {
                            Text = w.Text ?? string.Empty,
                            Start = wordStart,
                            End = wordEnd,
                            Probability = w.Probability
                        };
                    })
                    .Where(w => w is not null)
                    .Select(w => w!)
                    .ToList();

                result.Add(new TranscriptionSegment
                {
                    Text = segment.Text ?? string.Empty,
                    Start = start,
                    End = end,
                    Words = words
                });
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Löscht gespeicherte Segmente für einen Draft.
    /// </summary>
    public void DeleteSegments(Guid draftId)
    {
        var path = GetSegmentsPath(draftId);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    public int CleanupOrphanedTranscripts(
        IReadOnlyCollection<Guid> activeDraftIds,
        TimeSpan? maxAge = null)
    {
        if (activeDraftIds is null)
        {
            return 0;
        }

        var folder = Constants.DraftTranscriptsFolder;
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var threshold = DateTimeOffset.UtcNow - (maxAge ?? TimeSpan.FromDays(30));
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(folder, "*.txt"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParse(fileName, out var id))
            {
                continue;
            }

            if (activeDraftIds.Contains(id))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(file);
                if (info.Exists && info.LastWriteTimeUtc > threshold.UtcDateTime)
                {
                    continue;
                }

                info.Delete();
                removed++;
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }

        return removed;
    }

    public void FlushPending()
    {
        foreach (var entry in _pendingWrites.Values.ToList())
        {
            WriteNow(entry);
            _pendingWrites.TryRemove(entry.DraftId, out _);
        }
    }

    public void Dispose()
    {
        try
        {
            FlushPending();
        }
        catch
        {
            // Best effort.
        }

        _cts.Cancel();
        _signal.Release();

        try
        {
            _worker.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Ignore shutdown issues.
        }

        _cts.Dispose();
        _signal.Dispose();
    }

    private async Task ProcessQueueAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (_queue.TryDequeue(out var write))
            {
                if (!_pendingWrites.TryGetValue(write.DraftId, out var latest)
                    || !ReferenceEquals(latest, write))
                {
                    continue;
                }

                WriteNow(write);
                _pendingWrites.TryRemove(write.DraftId, out _);
            }
        }
    }

    private void WriteNow(DraftTranscriptWrite write)
    {
        try
        {
            Directory.CreateDirectory(Constants.DraftTranscriptsFolder);
            File.WriteAllText(write.Path, write.Transcript, Utf8NoBom);
            _lastHashes[write.DraftId] = write.Hash;
        }
        catch
        {
            // Ignore write failures.
        }
    }

    private static string ComputeHash(string text)
    {
        var bytes = Utf8NoBom.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private sealed record DraftTranscriptWrite(Guid DraftId, string Path, string Transcript, string Hash);

    // Serialisierbare DTOs für JSON-Persistenz
    private sealed class SerializableSegment
    {
        public string? Text { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public long? StartMs { get; set; }
        public long? EndMs { get; set; }
        public List<SerializableWord>? Words { get; set; }
    }

    private sealed class SerializableWord
    {
        public string? Text { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public long? StartMs { get; set; }
        public long? EndMs { get; set; }
        public float Probability { get; set; }
    }

    private static string FormatTimestamp(TimeSpan timeSpan)
    {
        return timeSpan.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static bool TryResolveTimestamp(string? formatted, long? millis, out TimeSpan value)
    {
        if (TryParseTimestamp(formatted, out value))
        {
            return true;
        }

        if (millis.HasValue)
        {
            value = TimeSpan.FromMilliseconds(millis.Value);
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParseTimestamp(string? formatted, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(formatted))
        {
            return false;
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

        return TimeSpan.TryParseExact(formatted, formats, CultureInfo.InvariantCulture, out value)
               || TimeSpan.TryParse(formatted, CultureInfo.InvariantCulture, out value);
    }
}
