using LogLens.Core.Parsing;

namespace LogLens.Core.Querying;

public sealed class LogEntryQueryService : ILogEntryQueryService
{
    public LogEntryQueryResult Query(
        IReadOnlyList<ParsedLogEntry> entries,
        LogEntryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);
        cancellationToken.ThrowIfCancellationRequested();

        string searchText = query.SearchText ?? string.Empty;
        if (searchText.Length == 0
            && query.Severities == LogSeverityFilter.All
            && query.TimestampPresence == TimestampPresenceFilter.All)
        {
            return new LogEntryQueryResult(entries, entries.Count);
        }

        var matches = new List<ParsedLogEntry>();
        foreach (ParsedLogEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!MatchesSeverity(entry.Severity, query.Severities)
                || !MatchesTimestamp(entry, query.TimestampPresence)
                || !MatchesSearch(entry.RawText, searchText))
            {
                continue;
            }

            matches.Add(entry);
        }

        return new LogEntryQueryResult(matches.ToArray(), entries.Count);
    }

    private static bool MatchesSearch(string rawText, string searchText) =>
        searchText.Length == 0
        || rawText.Contains(searchText, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesSeverity(
        LogSeverity severity,
        LogSeverityFilter selectedSeverities)
    {
        LogSeverityFilter severityFlag = severity switch
        {
            LogSeverity.Trace => LogSeverityFilter.Trace,
            LogSeverity.Debug => LogSeverityFilter.Debug,
            LogSeverity.Information => LogSeverityFilter.Information,
            LogSeverity.Warning => LogSeverityFilter.Warning,
            LogSeverity.Error => LogSeverityFilter.Error,
            LogSeverity.Critical => LogSeverityFilter.Critical,
            _ => LogSeverityFilter.Unknown
        };

        return (selectedSeverities & severityFlag) != 0;
    }

    private static bool MatchesTimestamp(
        ParsedLogEntry entry,
        TimestampPresenceFilter timestampPresence) => timestampPresence switch
        {
            TimestampPresenceFilter.All => true,
            TimestampPresenceFilter.HasTimestamp => entry.Timestamp is not null,
            TimestampPresenceFilter.NoTimestamp => entry.Timestamp is null,
            _ => false
        };

    private static void ValidateQuery(LogEntryQuery query)
    {
        if ((query.Severities & ~LogSeverityFilter.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "The severity selection contains unsupported values.");
        }

        if (!Enum.IsDefined(query.TimestampPresence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "The timestamp-presence selection is not supported.");
        }
    }
}
