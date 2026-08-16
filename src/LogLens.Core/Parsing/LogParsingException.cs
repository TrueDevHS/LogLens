namespace LogLens.Core.Parsing;

public enum LogParsingErrorKind
{
    InvalidEncoding,
    UnsupportedEncoding,
    UnsafeInputStream
}

public sealed class LogParsingException : Exception
{
    public LogParsingException(LogParsingErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public LogParsingException(LogParsingErrorKind kind, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public LogParsingErrorKind Kind { get; }
}
