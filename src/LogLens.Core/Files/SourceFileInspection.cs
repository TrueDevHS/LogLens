namespace LogLens.Core.Files;

public sealed record SourceFileInspection(
    string FullPath,
    string FileName,
    long Length,
    string Sha256,
    SourceFileSnapshot BeforeRead,
    SourceFileSnapshot? AfterRead,
    bool SourceChangedDuringRead);
