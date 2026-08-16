namespace LogLens.Core.Analysis;

public interface ILogAnalysisService
{
    Task<LogAnalysisResult> AnalyzeAsync(
        string path,
        CancellationToken cancellationToken = default);
}
