namespace LogLens.Core.Files;

public sealed class SourceFileException : Exception
{
    public SourceFileException(SourceFileErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public SourceFileException(SourceFileErrorKind kind, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public SourceFileErrorKind Kind { get; }
}
