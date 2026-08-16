namespace LogLens.Core.Patterns;

public static class PatternAnalysisPolicy
{
    public const int MinimumRepeatedMessageOccurrences = 2;
    public const int MaximumRepeatedMessageFindings = 20;
    public const int MaximumSeverityBurstFindings = 20;
    public const int MaximumEvidenceEntriesPerFinding = 200;
    public const int ErrorCriticalBurstMinimumEntries = 3;
    public const int WarningBurstMinimumEntries = 4;

    public static TimeSpan SeverityBurstWindow { get; } = TimeSpan.FromSeconds(60);
}
