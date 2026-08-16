namespace LogLens.Core.Reports;

public enum ReportExportErrorKind
{
    InvalidDestination,
    UnsupportedFormat,
    SourceOverwrite,
    NetworkLocation,
    ReparsePoint,
    DestinationExists,
    PermissionDenied,
    DestinationUnavailable
}

public sealed class ReportExportException : Exception
{
    public ReportExportException(ReportExportErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public ReportExportException(
        ReportExportErrorKind kind,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public ReportExportErrorKind Kind { get; }
}
