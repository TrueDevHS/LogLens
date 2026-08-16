namespace LogLens.Core.Files;

public sealed record ValidatedSourceFile(
    string FullPath,
    string FileName,
    long Length,
    DateTime LastWriteTimeUtc);
