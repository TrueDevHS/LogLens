namespace LogLens.Core.Patterns;

public sealed record SeverityBurstFinding(
    string Title,
    string Explanation,
    PatternSeverityGroup SeverityGroup,
    int OccurrenceCount,
    PatternTimeRange TimeRange,
    PatternEvidence Evidence);
