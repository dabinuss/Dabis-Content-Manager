using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DCM.Core.Logging;

/// <summary>
/// Zentraler Logger mit In-Memory-Buffer und optionalem File-Logging.
/// Thread-safe Singleton-Implementierung.
/// </summary>
public sealed class AppLogger : IAppLogger, IDisposable
{
    private static readonly Lazy<AppLogger> _instance = new(() => new AppLogger());
    public static AppLogger Instance => _instance.Value;

    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly object _fileLock = new();
    private readonly string _logFilePath;
    private readonly BlockingCollection<string> _fileQueue = new(new ConcurrentQueue<string>());
    private readonly CancellationTokenSource _writerCts = new();
    private readonly Task _writerTask;
    private bool _disposed;

    private const int MaxEntriesInMemory = 1000;
    private const int MaxFileQueueSize = 5000;

    public event Action<LogEntry>? EntryAdded;

    private int _errorCount;

    public bool HasErrors => ErrorCount > 0;
    public int ErrorCount => Volatile.Read(ref _errorCount);

    private AppLogger()
    {
        _logFilePath = Path.Combine(Constants.AppDataFolder, Constants.LogFileName);
        _writerTask = Task.Run(ProcessQueue);
        QueueWrite($"=== Log gestartet: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    public void Debug(string message, string? source = null)
    {
        Log(LogLevel.Debug, message, source, null);
    }

    public void Info(string message, string? source = null)
    {
        Log(LogLevel.Info, message, source, null);
    }

    public void Warning(string message, string? source = null, Exception? exception = null)
    {
        Log(LogLevel.Warning, message, source, exception);
    }

    public void Error(string message, string? source = null, Exception? exception = null)
    {
        Log(LogLevel.Error, message, source, exception);
        Interlocked.Increment(ref _errorCount);
    }

    private void Log(LogLevel level, string message, string? source, Exception? exception)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Message = message,
            Source = source,
            Exception = exception
        };

        _entries.Enqueue(entry);

        // Memory begrenzen
        while (_entries.Count > MaxEntriesInMemory && _entries.TryDequeue(out _))
        {
            // Alte Einträge entfernen
        }

        // In Datei schreiben (über Hintergrund-Writer)
        QueueWrite(entry.FormattedMessage);

        // Debug-Ausgabe
        System.Diagnostics.Debug.WriteLine(entry.FormattedMessage);

        // Event auslösen
        try
        {
            EntryAdded?.Invoke(entry);
        }
        catch
        {
            // Event-Handler-Fehler ignorieren
        }
    }

    public IReadOnlyList<LogEntry> GetEntries()
    {
        return _entries.ToArray();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _errorCount, 0);
    }

    private void QueueWrite(string line)
    {
        try
        {
            TrimFileQueueIfNeeded();
            _fileQueue.Add(line, _writerCts.Token);
        }
        catch
        {
            // Logger wird vermutlich beendet
        }
    }

    private void TrimFileQueueIfNeeded()
    {
        // Best-effort: avoid unbounded memory if disk is slow or blocked.
        if (_fileQueue.Count <= MaxFileQueueSize)
        {
            return;
        }

        while (_fileQueue.Count > MaxFileQueueSize && _fileQueue.TryTake(out _))
        {
            // Drop oldest entries to keep memory bounded.
        }
    }

    private void ProcessQueue()
    {
        try
        {
            foreach (var line in _fileQueue.GetConsumingEnumerable(_writerCts.Token))
            {
                WriteToFile(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Beendet
        }
        catch
        {
            // Fehler beim Schreiben ignorieren
        }
    }

    private void WriteToFile(string line)
    {
        try
        {
            lock (_fileLock)
            {
                RotateLogsIfNeeded();
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // File-Logging-Fehler ignorieren
        }
    }

    private void RotateLogsIfNeeded()
    {
        try
        {
            if (Constants.LogFileMaxBytes <= 0 || Constants.LogFileMaxCount <= 1)
            {
                return;
            }

            if (!File.Exists(_logFilePath))
            {
                return;
            }

            var info = new FileInfo(_logFilePath);
            if (!info.Exists || info.Length < Constants.LogFileMaxBytes)
            {
                return;
            }

            for (var i = Constants.LogFileMaxCount - 1; i >= 1; i--)
            {
                var source = $"{_logFilePath}.{i}";
                var target = $"{_logFilePath}.{i + 1}";

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                if (File.Exists(source))
                {
                    File.Move(source, target);
                }
            }

            var first = $"{_logFilePath}.1";
            if (File.Exists(first))
            {
                File.Delete(first);
            }

            File.Move(_logFilePath, first);
        }
        catch
        {
            // Rotation ist best-effort.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _fileQueue.CompleteAdding();
            _writerTask.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Ignorieren
        }
        finally
        {
            _writerCts.Cancel();
            _writerCts.Dispose();
            _fileQueue.Dispose();
        }
    }
}

