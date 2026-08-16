namespace LogLens.Core.Files;

public sealed record SourceFileReadResult<T>(
    ValidatedSourceFile Source,
    SourceFileSnapshot BeforeRead,
    SourceFileSnapshot? AfterRead,
    T Data)
{
    public string Sha256 { get; init; } = string.Empty;

    public bool SourceChangedDuringRead => !BeforeRead.Matches(AfterRead);
}
