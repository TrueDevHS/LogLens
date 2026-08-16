using System.Security;
using System.Text.Json;
using LogLens.Core.Analysis;
using LogLens.Core.Files;
using LogLens.Core.Parsing;
using LogLens.Core.Querying;

namespace LogLens.Core.Persistence;

public sealed class LocalSessionStore : ILocalSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        MaxDepth = 32
    };

    private readonly ILocalAppDataPathProvider _pathProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public LocalSessionStore()
        : this(new LocalAppDataPathProvider())
    {
    }

    public LocalSessionStore(ILocalAppDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider
            ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public string StorageRoot => LocalDataPathSafety.GetCanonicalRoot(_pathProvider);

    public async Task<SessionSaveResult> SaveAsync(
        SessionCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Analysis);
        ArgumentNullException.ThrowIfNull(request.UiState);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.UiState.SearchText is { Length: > LocalSessionPolicy.MaximumSearchTextCharacters })
        {
            return new SessionSaveResult(
                SessionSaveStatus.TooLarge,
                $"The current search text exceeds the {LocalSessionPolicy.MaximumSearchTextCharacters:N0}-character session-storage limit. Search still works normally, but shorten it before closing if you want this state restored.");
        }

        long rawCharacters = await Task.Run(
            () => CountRawCharacters(request.Analysis.Parsing.Entries),
            cancellationToken).ConfigureAwait(false);
        if (rawCharacters > LocalSessionPolicy.MaximumPersistedRawCharacters)
        {
            return new SessionSaveResult(
                SessionSaveStatus.TooLarge,
                $"This session contains more than {LocalSessionPolicy.MaximumPersistedRawCharacters:N0} raw log characters and was not stored. The current analysis remains available until LogLens closes.");
        }

        byte[] content;
        try
        {
            content = await Task.Run(
                () => JsonSerializer.SerializeToUtf8Bytes(
                    CreateDocument(request),
                    JsonOptions),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or JsonException)
        {
            return new SessionSaveResult(
                SessionSaveStatus.Failed,
                "The current analysis could not be prepared for local session storage.");
        }

        if (content.LongLength > LocalSessionPolicy.MaximumSessionFileBytes)
        {
            return new SessionSaveResult(
                SessionSaveStatus.TooLarge,
                $"The local session would exceed the {FormatMebibytes(LocalSessionPolicy.MaximumSessionFileBytes)} storage limit and was not stored. The current analysis remains available until LogLens closes.");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string root = StorageRoot;
            LocalDataPathSafety.EnsureRootCanBeCreated(root);
            LocalDataPathSafety.EnsureSafeExistingRoot(root);
            Directory.CreateDirectory(root);
            LocalDataPathSafety.EnsureSafeExistingRoot(root);

            string sessionPath = LocalDataPathSafety.GetKnownFilePath(
                root,
                LocalSessionPolicy.SessionFileName);
            string temporaryPath = LocalDataPathSafety.GetKnownFilePath(
                root,
                LocalSessionPolicy.TemporarySessionFileName);
            string backupPath = LocalDataPathSafety.GetKnownFilePath(
                root,
                LocalSessionPolicy.BackupSessionFileName);

            await WriteAtomicallyAsync(
                root,
                sessionPath,
                temporaryPath,
                backupPath,
                content,
                cancellationToken).ConfigureAwait(false);

            return new SessionSaveResult(
                SessionSaveStatus.Saved,
                "The current LogLens session was stored locally.",
                content.LongLength);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecurityException)
        {
            return new SessionSaveResult(
                SessionSaveStatus.UnsafeStorage,
                "LogLens local data was not saved because its storage path contains a linked or redirected entry.");
        }
        catch (UnauthorizedAccessException)
        {
            return new SessionSaveResult(
                SessionSaveStatus.AccessDenied,
                "Windows denied access to the LogLens local-data folder. The current analysis remains usable in memory.");
        }
        catch (Exception exception) when (exception is IOException
                                          or ArgumentException
                                          or InvalidOperationException
                                          or NotSupportedException)
        {
            return new SessionSaveResult(
                SessionSaveStatus.Failed,
                "LogLens could not save the local session. The current analysis remains usable, and any previous valid session was kept where possible.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<SessionLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string root = StorageRoot;
            if (File.Exists(root))
            {
                return new SessionLoadResult(
                    SessionLoadStatus.UnsafeStorage,
                    "The LogLens local-data location is not a regular folder and was not loaded.");
            }

            if (!Directory.Exists(root))
            {
                return new SessionLoadResult(
                    SessionLoadStatus.NoSession,
                    "No stored LogLens session exists.");
            }

            LocalDataPathSafety.EnsureSafeExistingRoot(root);
            string sessionPath = LocalDataPathSafety.GetKnownFilePath(
                root,
                LocalSessionPolicy.SessionFileName);
            if (!File.Exists(sessionPath))
            {
                return new SessionLoadResult(
                    SessionLoadStatus.NoSession,
                    "No stored LogLens session exists.");
            }

            LocalDataPathSafety.RejectReparseEntry(root, sessionPath);
            long length = new FileInfo(sessionPath).Length;
            if (length > LocalSessionPolicy.MaximumSessionFileBytes)
            {
                return new SessionLoadResult(
                    SessionLoadStatus.TooLarge,
                    "The stored LogLens session exceeds the supported local storage limit and was not loaded.");
            }

            byte[] content = await File.ReadAllBytesAsync(
                sessionPath,
                cancellationToken).ConfigureAwait(false);
            PersistedSessionDocument? document = await Task.Run(
                () => JsonSerializer.Deserialize<PersistedSessionDocument>(
                    content,
                    JsonOptions),
                cancellationToken).ConfigureAwait(false);

            if (document is null)
            {
                return Corrupt();
            }

            if (document.SessionVersion != LocalSessionPolicy.CurrentSessionVersion)
            {
                return new SessionLoadResult(
                    SessionLoadStatus.Incompatible,
                    "The stored LogLens session was created with an incompatible format and was not loaded.");
            }

            RestoredLocalSession restored = RestoreAndValidate(document);
            return new SessionLoadResult(
                SessionLoadStatus.Restored,
                "Restored from local LogLens data.",
                restored);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            return Corrupt();
        }
        catch (InvalidDataException)
        {
            return Corrupt();
        }
        catch (SecurityException)
        {
            return new SessionLoadResult(
                SessionLoadStatus.UnsafeStorage,
                "The LogLens local-data folder contains a linked or redirected entry and was not loaded.");
        }
        catch (UnauthorizedAccessException)
        {
            return new SessionLoadResult(
                SessionLoadStatus.AccessDenied,
                "Windows denied access to the stored LogLens session. The application started without restoring it.");
        }
        catch (Exception exception) when (exception is IOException
                                          or ArgumentException
                                          or InvalidOperationException
                                          or NotSupportedException)
        {
            return new SessionLoadResult(
                SessionLoadStatus.Failed,
                "The stored LogLens session is unavailable or locked. The application started without restoring it.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<LocalStorageStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string root = StorageRoot;
            if (File.Exists(root))
            {
                return new LocalStorageStatus(
                    root,
                    IsAccessible: false,
                    SessionExists: false,
                    ApproximateSizeBytes: 0,
                    ContainsRawParsedLogText: false,
                    ContainsRecentExportMetadata: false,
                    HasUnsafeEntries: true,
                    "The LogLens local-data location is not a regular folder.");
            }

            if (!Directory.Exists(root))
            {
                return new LocalStorageStatus(
                    root,
                    IsAccessible: true,
                    SessionExists: false,
                    ApproximateSizeBytes: 0,
                    ContainsRawParsedLogText: false,
                    ContainsRecentExportMetadata: false,
                    HasUnsafeEntries: false,
                    "No LogLens session is currently stored.");
            }

            LocalDataPathSafety.EnsureSafeExistingRoot(root);
            (long size, bool unsafeEntries) = CalculateStorageSize(root, cancellationToken);
            string sessionPath = LocalDataPathSafety.GetKnownFilePath(
                root,
                LocalSessionPolicy.SessionFileName);
            bool sessionExists = File.Exists(sessionPath)
                && (File.GetAttributes(sessionPath) & FileAttributes.ReparsePoint) == 0;

            return new LocalStorageStatus(
                root,
                IsAccessible: !unsafeEntries,
                sessionExists,
                size,
                ContainsRawParsedLogText: sessionExists,
                ContainsRecentExportMetadata: false,
                HasUnsafeEntries: unsafeEntries,
                unsafeEntries
                    ? "Linked or redirected entries were detected and were not followed."
                    : sessionExists
                        ? "A LogLens session file is stored locally."
                        : "No LogLens session is currently stored.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or SecurityException
                                          or ArgumentException
                                          or InvalidOperationException
                                          or NotSupportedException)
        {
            string root;
            try
            {
                root = StorageRoot;
            }
            catch
            {
                root = "%LocalAppData%\\LogLens";
            }

            return new LocalStorageStatus(
                root,
                IsAccessible: false,
                SessionExists: false,
                ApproximateSizeBytes: 0,
                ContainsRawParsedLogText: false,
                ContainsRecentExportMetadata: false,
                HasUnsafeEntries: exception is SecurityException,
                "LogLens could not inspect its local-data folder.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static async Task WriteAtomicallyAsync(
        string root,
        string sessionPath,
        string temporaryPath,
        string backupPath,
        byte[] content,
        CancellationToken cancellationToken)
    {
        DeleteKnownFileIfPresent(root, temporaryPath);

        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.Asynchronous
                    | FileOptions.SequentialScan
                    | FileOptions.WriteThrough
            };

            await using (var stream = new FileStream(temporaryPath, options))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(sessionPath))
            {
                LocalDataPathSafety.RejectReparseEntry(root, sessionPath);
                DeleteKnownFileIfPresent(root, backupPath);
                File.Replace(
                    temporaryPath,
                    sessionPath,
                    backupPath,
                    ignoreMetadataErrors: true);
                DeleteKnownFileIfPresent(root, backupPath);
            }
            else
            {
                File.Move(temporaryPath, sessionPath);
            }
        }
        catch
        {
            try
            {
                DeleteKnownFileIfPresent(root, temporaryPath);
            }
            catch
            {
                // A partial temp file remains inside the app-owned root and is ignored on load.
            }

            throw;
        }
    }

    private static void DeleteKnownFileIfPresent(string root, string path)
    {
        LocalDataPathSafety.EnsureInsideRoot(root, path, allowRoot: false);
        if (!File.Exists(path))
        {
            return;
        }

        LocalDataPathSafety.RejectReparseEntry(root, path);
        File.Delete(path);
    }

    private static PersistedSessionDocument CreateDocument(SessionCaptureRequest request)
    {
        ValidateCaptureRequest(request);
        LogAnalysisResult analysis = request.Analysis;
        return new PersistedSessionDocument(
            LocalSessionPolicy.CurrentSessionVersion,
            request.CreatedAtUtc,
            request.UpdatedAtUtc,
            request.ApplicationVersion,
            new PersistedDataInventory(
                ContainsRawParsedLogText: true,
                ContainsOriginalSourcePath: true,
                PatternsRequireReconstruction: true,
                ContainsRecentExportMetadata: false),
            new PersistedSource(
                analysis.Source.FullPath,
                analysis.Source.FileName,
                analysis.Source.Length,
                analysis.Source.Sha256,
                new PersistedSourceSnapshot(
                    analysis.Source.BeforeRead.Length,
                    analysis.Source.BeforeRead.LastWriteTimeUtc),
                analysis.Source.AfterRead is null
                    ? null
                    : new PersistedSourceSnapshot(
                        analysis.Source.AfterRead.Length,
                        analysis.Source.AfterRead.LastWriteTimeUtc),
                analysis.Source.SourceChangedDuringRead),
            new PersistedParsing(
                CreateSummary(analysis.Parsing.Summary),
                analysis.Parsing.Entries.Select(CreateEntry).ToArray(),
                analysis.Parsing.Diagnostics.Select(CreateDiagnostic).ToArray(),
                analysis.Parsing.DetectedEncoding),
            new PersistedUiState(
                request.UiState.SelectedSection,
                request.UiState.SearchText,
                (int)request.UiState.SeverityFilter,
                (int)request.UiState.TimestampFilter,
                request.UiState.SelectedEntryLineNumber));
    }

    private static RestoredLocalSession RestoreAndValidate(
        PersistedSessionDocument document)
    {
        if (document.Inventory is null
            || !document.Inventory.ContainsRawParsedLogText
            || !document.Inventory.ContainsOriginalSourcePath
            || !document.Inventory.PatternsRequireReconstruction
            || document.Inventory.ContainsRecentExportMetadata)
        {
            throw new InvalidDataException("The persisted data inventory is invalid.");
        }

        ValidateDocumentStringsAndTimes(document);
        PersistedParsing parsing = document.Parsing
            ?? throw new InvalidDataException("Parsing data is missing.");
        PersistedParsingSummary persistedSummary = parsing.Summary
            ?? throw new InvalidDataException("The parsing summary is missing.");
        PersistedEntry[] persistedEntries = parsing.Entries
            ?? throw new InvalidDataException("Parsed entries are missing.");
        PersistedDiagnostic[] persistedDiagnostics = parsing.Diagnostics
            ?? throw new InvalidDataException("Parsing diagnostics are missing.");

        ValidateSummary(persistedSummary, persistedEntries.Length);
        if (persistedDiagnostics.Length > LogParsingPolicy.MaximumDiagnostics)
        {
            throw new InvalidDataException("Too many parsing diagnostics were stored.");
        }

        var entries = new ParsedLogEntry[persistedEntries.Length];
        int previousLine = 0;
        for (int index = 0; index < persistedEntries.Length; index++)
        {
            PersistedEntry entry = persistedEntries[index]
                ?? throw new InvalidDataException("A parsed entry is missing.");
            if (entry.LineNumber <= previousLine
                || entry.RawText is null
                || entry.Message is null
                || !Enum.IsDefined(entry.Severity))
            {
                throw new InvalidDataException("A parsed entry is invalid.");
            }

            ParsedLogTimestamp? timestamp = null;
            if (entry.Timestamp is not null)
            {
                if (entry.Timestamp.RawText is null)
                {
                    throw new InvalidDataException("A parsed timestamp is invalid.");
                }

                timestamp = new ParsedLogTimestamp(
                    entry.Timestamp.RawText,
                    entry.Timestamp.Date,
                    entry.Timestamp.Time,
                    entry.Timestamp.UtcOffset);
            }

            entries[index] = new ParsedLogEntry(
                entry.LineNumber,
                entry.RawText,
                entry.Severity,
                timestamp,
                entry.Message);
            previousLine = entry.LineNumber;
        }

        var diagnostics = new ParsingDiagnostic[persistedDiagnostics.Length];
        for (int index = 0; index < persistedDiagnostics.Length; index++)
        {
            PersistedDiagnostic diagnostic = persistedDiagnostics[index]
                ?? throw new InvalidDataException("A parsing diagnostic is missing.");
            if (!Enum.IsDefined(diagnostic.Kind)
                || !Enum.IsDefined(diagnostic.Level)
                || diagnostic.Message is null
                || diagnostic.LineNumber is <= 0)
            {
                throw new InvalidDataException("A parsing diagnostic is invalid.");
            }

            diagnostics[index] = new ParsingDiagnostic(
                diagnostic.Kind,
                diagnostic.Level,
                diagnostic.Message,
                diagnostic.LineNumber);
        }

        PersistedSource source = document.Source
            ?? throw new InvalidDataException("Source metadata is missing.");
        ValidateSource(source);
        var before = new SourceFileSnapshot(
            source.BeforeRead.Length,
            source.BeforeRead.LastWriteTimeUtc);
        SourceFileSnapshot? after = source.AfterRead is null
            ? null
            : new SourceFileSnapshot(
                source.AfterRead.Length,
                source.AfterRead.LastWriteTimeUtc);
        if (source.SourceChangedDuringRead == before.Matches(after))
        {
            throw new InvalidDataException("Source integrity metadata is inconsistent.");
        }

        var inspection = new SourceFileInspection(
            source.FullPath,
            source.FileName,
            source.Length,
            source.Sha256,
            before,
            after,
            source.SourceChangedDuringRead);
        var summary = RestoreSummary(persistedSummary);
        var parsingResult = new LogParsingResult(
            entries,
            summary,
            diagnostics,
            parsing.DetectedEncoding);
        SessionUiState uiState = RestoreUiState(document.Ui);

        return new RestoredLocalSession(
            new LogAnalysisResult(inspection, parsingResult),
            uiState,
            document.ApplicationVersion,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            ContainsRawParsedLogText: true,
            PatternsRequireReconstruction: true,
            ContainsRecentExportMetadata: false);
    }

    private static void ValidateCaptureRequest(SessionCaptureRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationVersion)
            || request.ApplicationVersion.Length > 64
            || request.UpdatedAtUtc < request.CreatedAtUtc)
        {
            throw new ArgumentException("Session metadata is invalid.", nameof(request));
        }

        ValidateUiState(request.UiState);
        if (request.Analysis.Parsing.Entries.Count > LogParsingPolicy.MaximumEntries
            || request.Analysis.Parsing.Diagnostics.Count > LogParsingPolicy.MaximumDiagnostics)
        {
            throw new ArgumentException("The analysis exceeds the supported bounds.", nameof(request));
        }
    }

    private static void ValidateDocumentStringsAndTimes(PersistedSessionDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.ApplicationVersion)
            || document.ApplicationVersion.Length > 64
            || document.UpdatedAtUtc < document.CreatedAtUtc
            || document.Source is null
            || document.Parsing is null
            || document.Ui is null
            || string.IsNullOrWhiteSpace(document.Parsing.DetectedEncoding))
        {
            throw new InvalidDataException("Stored session metadata is invalid.");
        }
    }

    private static void ValidateSource(PersistedSource source)
    {
        if (string.IsNullOrWhiteSpace(source.FullPath)
            || !Path.IsPathFullyQualified(source.FullPath)
            || source.FullPath.StartsWith(@"\\", StringComparison.Ordinal)
            || source.FullPath.StartsWith(@"\\?\", StringComparison.Ordinal)
            || source.FullPath.StartsWith(@"\\.\", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(source.FileName)
            || !string.Equals(
                Path.GetFileName(source.FileName),
                source.FileName,
                StringComparison.Ordinal)
            || source.Length < 0
            || source.BeforeRead is null
            || source.BeforeRead.Length < 0
            || source.AfterRead?.Length < 0
            || !IsSha256(source.Sha256))
        {
            throw new InvalidDataException("Stored source metadata is invalid.");
        }
    }

    private static void ValidateSummary(PersistedParsingSummary summary, int entryCount)
    {
        int severityTotal = summary.TraceCount
            + summary.DebugCount
            + summary.InformationCount
            + summary.WarningCount
            + summary.ErrorCount
            + summary.CriticalCount
            + summary.UnclassifiedEntries;
        if (entryCount > LogParsingPolicy.MaximumEntries
            || summary.TotalEntries != entryCount
            || summary.ClassifiedEntries + summary.UnclassifiedEntries != entryCount
            || severityTotal != entryCount
            || summary.TimestampedEntries < 0
            || summary.TimestampedEntries > entryCount
            || summary.TraceCount < 0
            || summary.DebugCount < 0
            || summary.InformationCount < 0
            || summary.WarningCount < 0
            || summary.ErrorCount < 0
            || summary.CriticalCount < 0
            || summary.UnclassifiedEntries < 0)
        {
            throw new InvalidDataException("The stored parsing summary is inconsistent.");
        }
    }

    private static SessionUiState RestoreUiState(PersistedUiState ui)
    {
        if (ui is null)
        {
            throw new InvalidDataException("Stored UI state is missing.");
        }

        var state = new SessionUiState(
            ui.SelectedSection,
            ui.SearchText,
            (LogSeverityFilter)ui.SeverityFilter,
            (TimestampPresenceFilter)ui.TimestampFilter,
            ui.SelectedEntryLineNumber);
        try
        {
            ValidateUiState(state);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Stored UI state is invalid.", exception);
        }

        return state;
    }

    private static void ValidateUiState(SessionUiState state)
    {
        if (!LocalSessionPolicy.IsSupportedSection(state.SelectedSection)
            || state.SearchText is null
            || state.SearchText.Length > LocalSessionPolicy.MaximumSearchTextCharacters
            || state.SeverityFilter == LogSeverityFilter.None
            || (state.SeverityFilter & ~LogSeverityFilter.All) != 0
            || !Enum.IsDefined(state.TimestampFilter)
            || state.SelectedEntryLineNumber is <= 0)
        {
            throw new ArgumentException("The session UI state is invalid.", nameof(state));
        }
    }

    private static PersistedParsingSummary CreateSummary(LogParsingSummary summary) => new(
        summary.TotalEntries,
        summary.ClassifiedEntries,
        summary.UnclassifiedEntries,
        summary.TraceCount,
        summary.DebugCount,
        summary.InformationCount,
        summary.WarningCount,
        summary.ErrorCount,
        summary.CriticalCount,
        summary.TimestampedEntries,
        summary.IsComplete);

    private static LogParsingSummary RestoreSummary(PersistedParsingSummary summary) => new(
        summary.TotalEntries,
        summary.ClassifiedEntries,
        summary.UnclassifiedEntries,
        summary.TraceCount,
        summary.DebugCount,
        summary.InformationCount,
        summary.WarningCount,
        summary.ErrorCount,
        summary.CriticalCount,
        summary.TimestampedEntries,
        summary.IsComplete);

    private static PersistedEntry CreateEntry(ParsedLogEntry entry) => new(
        entry.LineNumber,
        entry.RawText,
        entry.Severity,
        entry.Timestamp is null
            ? null
            : new PersistedTimestamp(
                entry.Timestamp.RawText,
                entry.Timestamp.Date,
                entry.Timestamp.Time,
                entry.Timestamp.UtcOffset),
        entry.Message);

    private static PersistedDiagnostic CreateDiagnostic(ParsingDiagnostic diagnostic) => new(
        diagnostic.Kind,
        diagnostic.Level,
        diagnostic.Message,
        diagnostic.LineNumber);

    private static long CountRawCharacters(IReadOnlyList<ParsedLogEntry> entries)
    {
        long total = 0;
        foreach (ParsedLogEntry entry in entries)
        {
            total = checked(total + entry.RawText.Length);
            if (total > LocalSessionPolicy.MaximumPersistedRawCharacters)
            {
                break;
            }
        }

        return total;
    }

    private static (long Size, bool UnsafeEntries) CalculateStorageSize(
        string root,
        CancellationToken cancellationToken)
    {
        long total = 0;
        bool unsafeEntries = false;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                LocalDataPathSafety.EnsureInsideRoot(root, entry, allowRoot: false);
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    unsafeEntries = true;
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    total = checked(total + new FileInfo(entry).Length);
                }
            }
        }

        return (total, unsafeEntries);
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => char.IsAsciiHexDigit(character));

    private static string FormatMebibytes(long bytes) =>
        $"{bytes / (1024 * 1024):N0} MiB";

    private static SessionLoadResult Corrupt() => new(
        SessionLoadStatus.Corrupt,
        "The stored LogLens session is malformed or incomplete and was not loaded. You can erase LogLens local data safely from About & Privacy.");
}

internal sealed record PersistedSessionDocument(
    int SessionVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string ApplicationVersion,
    PersistedDataInventory Inventory,
    PersistedSource Source,
    PersistedParsing Parsing,
    PersistedUiState Ui);

internal sealed record PersistedDataInventory(
    bool ContainsRawParsedLogText,
    bool ContainsOriginalSourcePath,
    bool PatternsRequireReconstruction,
    bool ContainsRecentExportMetadata);

internal sealed record PersistedSource(
    string FullPath,
    string FileName,
    long Length,
    string Sha256,
    PersistedSourceSnapshot BeforeRead,
    PersistedSourceSnapshot? AfterRead,
    bool SourceChangedDuringRead);

internal sealed record PersistedSourceSnapshot(
    long Length,
    DateTime LastWriteTimeUtc);

internal sealed record PersistedParsing(
    PersistedParsingSummary Summary,
    PersistedEntry[] Entries,
    PersistedDiagnostic[] Diagnostics,
    string DetectedEncoding);

internal sealed record PersistedParsingSummary(
    int TotalEntries,
    int ClassifiedEntries,
    int UnclassifiedEntries,
    int TraceCount,
    int DebugCount,
    int InformationCount,
    int WarningCount,
    int ErrorCount,
    int CriticalCount,
    int TimestampedEntries,
    bool IsComplete);

internal sealed record PersistedEntry(
    int LineNumber,
    string RawText,
    LogSeverity Severity,
    PersistedTimestamp? Timestamp,
    string Message);

internal sealed record PersistedTimestamp(
    string RawText,
    DateOnly? Date,
    TimeOnly Time,
    TimeSpan? UtcOffset);

internal sealed record PersistedDiagnostic(
    ParsingDiagnosticKind Kind,
    ParsingDiagnosticLevel Level,
    string Message,
    int? LineNumber);

internal sealed record PersistedUiState(
    string SelectedSection,
    string SearchText,
    int SeverityFilter,
    int TimestampFilter,
    int? SelectedEntryLineNumber);
