namespace LogLens.Core.Persistence;

public interface ILocalSessionStore
{
    string StorageRoot { get; }

    Task<SessionSaveResult> SaveAsync(
        SessionCaptureRequest request,
        CancellationToken cancellationToken = default);

    Task<SessionLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<LocalStorageStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
