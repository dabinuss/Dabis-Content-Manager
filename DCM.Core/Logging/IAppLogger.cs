namespace DCM.Core.Logging;

/// <summary>
/// Interface für den zentralen App-Logger.
/// </summary>
public interface IAppLogger
{
    void Debug(string message, string? source = null);
    void Info(string message, string? source = null);
    void Warning(string message, string? source = null, Exception? exception = null);
    void Error(string message, string? source = null, Exception? exception = null);

    IReadOnlyList<LogEntry> GetEntries();
    event Action<LogEntry>? EntryAdded;

    void Clear();

    bool HasErrors { get; }
    int ErrorCount { get; }
}