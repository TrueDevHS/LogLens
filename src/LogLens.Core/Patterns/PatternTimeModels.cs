namespace LogLens.Core.Patterns;

public enum PatternTimeBasis
{
    WallClock,
    Utc
}

public sealed record PatternTimeAnalysisStatus(
    bool IsAvailable,
    int RecognizedTimestampEntries,
    int ComparableTimestampEntries,
    int ExcludedTimestampEntries,
    PatternTimeBasis? Basis,
    string Explanation);

public sealed record PatternTimeRange(
    DateTime Start,
    DateTime End,
    PatternTimeBasis Basis);

public sealed record PatternActivityWindow(
    DateTime Start,
    TimeSpan Duration,
    PatternTimeBasis Basis)
{
    public DateTime EndExclusive => Start + Duration;
}
