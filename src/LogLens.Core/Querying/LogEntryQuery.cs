namespace LogLens.Core.Querying;

public sealed record LogEntryQuery(
    string? SearchText,
    LogSeverityFilter Severities,
    TimestampPresenceFilter TimestampPresence)
{
    public static LogEntryQuery ShowAll { get; } = new(
        string.Empty,
        LogSeverityFilter.All,
        TimestampPresenceFilter.All);
}
