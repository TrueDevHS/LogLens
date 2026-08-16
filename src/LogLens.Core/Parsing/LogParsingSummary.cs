namespace LogLens.Core.Parsing;

public sealed record LogParsingSummary(
    int TotalEntries,
    int ClassifiedEntries,
    int UnclassifiedEntries,
    int TraceCount,
    int DebugCount,
    int InformationCount,
    int WarningCount,
    int ErrorCount,
    int CriticalCount,
    int TimestampedEntries,
    bool IsComplete)
{
    public bool HasDetectedTimestamps => TimestampedEntries > 0;

    public int GetCount(LogSeverity severity) => severity switch
    {
        LogSeverity.Trace => TraceCount,
        LogSeverity.Debug => DebugCount,
        LogSeverity.Information => InformationCount,
        LogSeverity.Warning => WarningCount,
        LogSeverity.Error => ErrorCount,
        LogSeverity.Critical => CriticalCount,
        _ => UnclassifiedEntries
    };
}
