namespace LogLens.Core.Parsing;

public sealed record LogParsingResult(
    IReadOnlyList<ParsedLogEntry> Entries,
    LogParsingSummary Summary,
    IReadOnlyList<ParsingDiagnostic> Diagnostics,
    string DetectedEncoding);
