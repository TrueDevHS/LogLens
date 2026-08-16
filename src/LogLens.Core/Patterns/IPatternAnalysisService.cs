using LogLens.Core.Parsing;

namespace LogLens.Core.Patterns;

public interface IPatternAnalysisService
{
    PatternAnalysisResult Analyze(
        IReadOnlyList<ParsedLogEntry> entries,
        CancellationToken cancellationToken = default);
}
