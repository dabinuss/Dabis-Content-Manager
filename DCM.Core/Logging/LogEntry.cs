namespace DCM.Core.Logging;

/// <summary>
/// Repräsentiert einen einzelnen Log-Eintrag.
/// </summary>
public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Source { get; init; }
    public Exception? Exception { get; init; }

    public string FormattedMessage
    {
        get
        {
            var levelStr = Level switch
            {
                LogLevel.Debug => "DBG",
                LogLevel.Info => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                _ => "???"
            };

            var source = string.IsNullOrWhiteSpace(Source) ? "" : $"[{Source}] ";
            var exceptionPart = Exception is not null ? $" | {Exception.GetType().Name}: {Exception.Message}" : "";

            return $"{Timestamp:HH:mm:ss.fff} [{levelStr}] {source}{Message}{exceptionPart}";
        }
    }
}