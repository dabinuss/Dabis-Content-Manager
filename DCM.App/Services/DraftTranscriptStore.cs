using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using DCM.Core;

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

    public string GetTranscriptPath(Guid draftId)
    {
        return Path.Combine(Constants.DraftTranscriptsFolder, $"{draftId:N}.txt");
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
}
