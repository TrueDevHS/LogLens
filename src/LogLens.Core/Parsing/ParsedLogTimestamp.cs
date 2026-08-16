namespace LogLens.Core.Parsing;

public sealed record ParsedLogTimestamp(
    string RawText,
    DateOnly? Date,
    TimeOnly Time,
    TimeSpan? UtcOffset);
