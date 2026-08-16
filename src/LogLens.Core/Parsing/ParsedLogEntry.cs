namespace LogLens.Core.Parsing;

public sealed record ParsedLogEntry(
    int LineNumber,
    string RawText,
    LogSeverity Severity,
    ParsedLogTimestamp? Timestamp,
    string Message);
