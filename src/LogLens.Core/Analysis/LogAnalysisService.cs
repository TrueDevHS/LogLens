using LogLens.Core.Files;
using LogLens.Core.Parsing;

namespace LogLens.Core.Analysis;

public sealed class LogAnalysisService : ILogAnalysisService
{
    private readonly IReadOnlySourceFileLoader _sourceLoader;
    private readonly ILogParser _parser;

    public LogAnalysisService()
        : this(new ReadOnlySourceFileLoader(), new PlainTextLogParser())
    {
    }

    public LogAnalysisService(
        IReadOnlySourceFileLoader sourceLoader,
        ILogParser parser)
    {
        _sourceLoader = sourceLoader ?? throw new ArgumentNullException(nameof(sourceLoader));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<LogAnalysisResult> AnalyzeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        SourceFileReadResult<LogParsingResult> readResult = await _sourceLoader.ReadAsync(
            path,
            _parser.ParseAsync,
            cancellationToken).ConfigureAwait(false);

        var inspection = new SourceFileInspection(
            readResult.Source.FullPath,
            readResult.Source.FileName,
            readResult.BeforeRead.Length,
            readResult.Sha256,
            readResult.BeforeRead,
            readResult.AfterRead,
            readResult.SourceChangedDuringRead);

        return new LogAnalysisResult(inspection, readResult.Data);
    }
}
