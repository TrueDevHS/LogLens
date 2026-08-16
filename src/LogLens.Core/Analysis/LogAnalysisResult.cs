using LogLens.Core.Files;
using LogLens.Core.Parsing;

namespace LogLens.Core.Analysis;

public sealed record LogAnalysisResult(
    SourceFileInspection Source,
    LogParsingResult Parsing);
