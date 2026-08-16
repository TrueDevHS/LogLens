namespace LogLens.Core.Files;

public interface IReadOnlySourceFileLoader
{
    Task<SourceFileInspection> InspectAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<SourceFileReadResult<T>> ReadAsync<T>(
        string path,
        Func<Stream, CancellationToken, Task<T>> readContentAsync,
        CancellationToken cancellationToken = default);
}
