using System.Collections.Concurrent;
using System.Text;
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

    public event Action<LogEntry>? EntryAdded;

    public bool HasErrors => ErrorCount > 0;
    public int ErrorCount { get; private set; }

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
        ErrorCount++;
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
        ErrorCount = 0;
    }

    private void QueueWrite(string line)
    {
        try
        {
            _fileQueue.Add(line, _writerCts.Token);
        }
        catch
        {
            // Logger wird vermutlich beendet
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
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // File-Logging-Fehler ignorieren
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
            _writerTask.Wait(TimeSpan.FromSeconds(2));
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
