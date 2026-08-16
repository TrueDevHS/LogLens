using LogLens.Core.Parsing;

namespace LogLens.Core.Querying;

public sealed record LogEntryQueryResult(
    IReadOnlyList<ParsedLogEntry> Entries,
    int TotalEntries)
{
    public int VisibleEntries => Entries.Count;
}
