using LogLens.Core.Analysis;
using LogLens.Core.Querying;

namespace LogLens.Core.Persistence;

public sealed record SessionUiState(
    string SelectedSection,
    string SearchText,
    LogSeverityFilter SeverityFilter,
    TimestampPresenceFilter TimestampFilter,
    int? SelectedEntryLineNumber)
{
    public static SessionUiState Default { get; } = new(
        "Home",
        string.Empty,
        LogSeverityFilter.All,
        TimestampPresenceFilter.All,
        null);
}

public sealed record SessionCaptureRequest(
    LogAnalysisResult Analysis,
    SessionUiState UiState,
    string ApplicationVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RestoredLocalSession(
    LogAnalysisResult Analysis,
    SessionUiState UiState,
    string ApplicationVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool ContainsRawParsedLogText,
    bool PatternsRequireReconstruction,
    bool ContainsRecentExportMetadata);

public enum SessionSaveStatus
{
    Saved,
    TooLarge,
    AccessDenied,
    UnsafeStorage,
    Failed
}

public sealed record SessionSaveResult(
    SessionSaveStatus Status,
    string Message,
    long BytesWritten = 0)
{
    public bool Succeeded => Status == SessionSaveStatus.Saved;
}

public enum SessionLoadStatus
{
    Restored,
    NoSession,
    Corrupt,
    Incompatible,
    TooLarge,
    AccessDenied,
    UnsafeStorage,
    Failed
}

public sealed record SessionLoadResult(
    SessionLoadStatus Status,
    string Message,
    RestoredLocalSession? Session = null)
{
    public bool Succeeded => Status == SessionLoadStatus.Restored && Session is not null;
}

public sealed record LocalStorageStatus(
    string StorageRoot,
    bool IsAccessible,
    bool SessionExists,
    long ApproximateSizeBytes,
    bool ContainsRawParsedLogText,
    bool ContainsRecentExportMetadata,
    bool HasUnsafeEntries,
    string Message);

public enum LocalDataEraseStatus
{
    Erased,
    NothingToErase,
    UnsafeStorage,
    AccessDenied,
    Failed
}

public sealed record LocalDataEraseResult(
    LocalDataEraseStatus Status,
    string Message,
    int FilesDeleted = 0,
    int DirectoriesDeleted = 0)
{
    public bool Succeeded => Status is LocalDataEraseStatus.Erased
        or LocalDataEraseStatus.NothingToErase;
}
