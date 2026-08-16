using LogLens.Core.Parsing;

namespace LogLens.Core.Patterns;

public sealed record PatternEvidence(
    IReadOnlyList<ParsedLogEntry> Entries,
    int TotalEntryCount)
{
    public bool IsTruncated => Entries.Count < TotalEntryCount;
}
