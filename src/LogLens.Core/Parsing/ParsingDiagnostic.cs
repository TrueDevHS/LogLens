namespace LogLens.Core.Parsing;

public enum ParsingDiagnosticLevel
{
    Information,
    Warning
}

public enum ParsingDiagnosticKind
{
    EmptyFile,
    MalformedTimestamp,
    UnusuallyLongLine,
    ControlCharacters,
    EntryLimitReached,
    DiagnosticLimitReached
}

public sealed record ParsingDiagnostic(
    ParsingDiagnosticKind Kind,
    ParsingDiagnosticLevel Level,
    string Message,
    int? LineNumber = null);
