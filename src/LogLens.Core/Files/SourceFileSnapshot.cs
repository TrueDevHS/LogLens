namespace LogLens.Core.Files;

public sealed record SourceFileSnapshot(long Length, DateTime LastWriteTimeUtc)
{
    public bool Matches(SourceFileSnapshot? other) =>
        other is not null
        && Length == other.Length
        && LastWriteTimeUtc == other.LastWriteTimeUtc;
}
