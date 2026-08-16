using LogLens.Core.Analysis;
using LogLens.Core.Patterns;

namespace LogLens.App.Views;

public sealed class PatternAnalysisCompletedEventArgs(
    LogAnalysisResult analysis,
    PatternAnalysisResult patterns) : EventArgs
{
    public LogAnalysisResult Analysis { get; } =
        analysis ?? throw new ArgumentNullException(nameof(analysis));

    public PatternAnalysisResult Patterns { get; } =
        patterns ?? throw new ArgumentNullException(nameof(patterns));
}
