namespace LogLens.Core.Files;

public enum SourceFileErrorKind
{
    InvalidPath,
    FileNotFound,
    UnsupportedExtension,
    NetworkLocation,
    DevicePath,
    ReparsePoint,
    NotRegularFile,
    FileTooLarge,
    PermissionDenied,
    FileUnavailable,
    ReadFailure,
    UnsafeStream
}
