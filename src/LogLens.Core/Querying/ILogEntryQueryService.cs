using LogLens.Core.Parsing;

namespace LogLens.Core.Querying;

public interface ILogEntryQueryService
{
    LogEntryQueryResult Query(
        IReadOnlyList<ParsedLogEntry> entries,
        LogEntryQuery query,
        CancellationToken cancellationToken = default);
}
