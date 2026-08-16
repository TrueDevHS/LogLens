namespace LogLens.Core.Patterns;

public sealed record PatternAnalysisResult(
    int EntriesAnalyzed,
    IReadOnlyList<RepeatedMessageFinding> TopRepeatedMessages,
    IReadOnlyList<RepeatedMessageFinding> RepeatedSeverityLeaders,
    int TotalRepeatedMessagePatterns,
    IReadOnlyList<SeverityBurstFinding> SeverityBursts,
    int TotalSeverityBurstCount,
    IReadOnlyList<ActivityWindowFinding> ActivityWindows,
    IReadOnlyList<SeverityFrequency> SeverityDistribution,
    PatternTimeAnalysisStatus TimeAnalysisStatus);
