namespace LogLens.Core.Patterns;

public enum ActivityWindowType
{
    BusiestMinute,
    BusiestHour,
    MostErrorCriticalMinute
}

public sealed record ActivityWindowFinding(
    ActivityWindowType Type,
    string Title,
    string Explanation,
    int OccurrenceCount,
    PatternActivityWindow Window,
    PatternEvidence Evidence);
