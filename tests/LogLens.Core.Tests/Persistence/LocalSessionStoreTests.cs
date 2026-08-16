using System.Text.Json;
using LogLens.Core.Parsing;
using LogLens.Core.Patterns;
using LogLens.Core.Persistence;
using LogLens.Core.Querying;
using LogLens.Core.Reports;
using LogLens.Core.Tests.Files;

namespace LogLens.Core.Tests.Persistence;

[TestClass]
public sealed class LocalSessionStoreTests
{
    [TestMethod]
    public void LocalAppDataPathProvider_ResolvesExpectedWindowsRoot()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify),
            "LogLens");

        string actual = new LocalAppDataPathProvider().GetLogLensDataRoot();

        Assert.AreEqual(Path.GetFullPath(expected), Path.GetFullPath(actual));
    }

    [TestMethod]
    public async Task SaveAsync_CreatesLogLensFolderAndCurrentSessionFile()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);

        SessionSaveResult result = await store.SaveAsync(
            PersistenceTestData.Request(Path.Combine(files.DirectoryPath, "source.log")));

        Assert.AreEqual(SessionSaveStatus.Saved, result.Status);
        Assert.IsTrue(Directory.Exists(root));
        Assert.IsTrue(File.Exists(Path.Combine(root, LocalSessionPolicy.SessionFileName)));
        Assert.IsGreaterThan(0L, result.BytesWritten);
    }

    [TestMethod]
    public async Task SaveThenLoad_RestoresNormalAnalysedSession()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        SessionCaptureRequest request = PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "source.log"));

        await store.SaveAsync(request);
        SessionLoadResult result = await store.LoadAsync();

        Assert.AreEqual(SessionLoadStatus.Restored, result.Status);
        Assert.IsNotNull(result.Session);
        Assert.AreEqual(request.ApplicationVersion, result.Session.ApplicationVersion);
        Assert.AreEqual(request.CreatedAtUtc, result.Session.CreatedAtUtc);
        Assert.AreEqual(request.UpdatedAtUtc, result.Session.UpdatedAtUtc);
    }

    [TestMethod]
    public async Task SaveAsync_ReplacesTheSingleCurrentSessionWithoutHistoryGrowth()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        await store.SaveAsync(PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "first.log")));
        await store.SaveAsync(PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "second.log")));

        SessionLoadResult restored = await store.LoadAsync();

        Assert.AreEqual("second.log", restored.Session?.Analysis.Source.FileName);
        Assert.HasCount(1, Directory.GetFiles(root));
        Assert.AreEqual(
            LocalSessionPolicy.SessionFileName,
            Path.GetFileName(Directory.GetFiles(root)[0]));
    }

    [TestMethod]
    public async Task LoadAsync_MissingFolderReturnsCleanNoSessionState()
    {
        using var files = new SyntheticFileScope();
        var store = CreateStore(PersistenceTestData.StorageRoot(files.DirectoryPath));

        SessionLoadResult result = await store.LoadAsync();

        Assert.AreEqual(SessionLoadStatus.NoSession, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task LoadAsync_EmptyFolderReturnsCleanNoSessionState()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);

        SessionLoadResult result = await CreateStore(root).LoadAsync();

        Assert.AreEqual(SessionLoadStatus.NoSession, result.Status);
    }

    [TestMethod]
    public async Task LoadAsync_PartialTemporaryFileIsIgnored()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, LocalSessionPolicy.TemporarySessionFileName),
            "{partial");

        SessionLoadResult result = await CreateStore(root).LoadAsync();

        Assert.AreEqual(SessionLoadStatus.NoSession, result.Status);
    }

    [TestMethod]
    public async Task LoadAsync_CorruptJsonReturnsFriendlyCorruptState()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, LocalSessionPolicy.SessionFileName),
            "{not-json");

        SessionLoadResult result = await CreateStore(root).LoadAsync();

        Assert.AreEqual(SessionLoadStatus.Corrupt, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task LoadAsync_IncompatibleVersionIsNotLoaded()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, LocalSessionPolicy.SessionFileName),
            "{\"sessionVersion\":999}");

        SessionLoadResult result = await CreateStore(root).LoadAsync();

        Assert.AreEqual(SessionLoadStatus.Incompatible, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task LoadAsync_OversizedStoredFileIsRejectedBeforeParsing()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        string sessionPath = Path.Combine(root, LocalSessionPolicy.SessionFileName);
        await using (var stream = new FileStream(
                         sessionPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None))
        {
            stream.SetLength(LocalSessionPolicy.MaximumSessionFileBytes + 1);
        }

        SessionLoadResult result = await CreateStore(root).LoadAsync();

        Assert.AreEqual(SessionLoadStatus.TooLarge, result.Status);
    }

    [TestMethod]
    public async Task LoadAsync_LockedSessionFailsGracefully()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        await store.SaveAsync(PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "source.log")));
        string sessionPath = Path.Combine(root, LocalSessionPolicy.SessionFileName);
        await using var lockHandle = new FileStream(
            sessionPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        SessionLoadResult result = await store.LoadAsync();

        Assert.AreEqual(SessionLoadStatus.Failed, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task SaveAsync_UnavailableRootFailsWithoutTouchingSource()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "SOURCE");
        byte[] sourceBefore = File.ReadAllBytes(source);
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        await File.WriteAllTextAsync(root, "This path is a file, not a directory.");

        SessionSaveResult result = await CreateStore(root).SaveAsync(
            PersistenceTestData.Request(source));

        Assert.AreEqual(SessionSaveStatus.Failed, result.Status);
        CollectionAssert.AreEqual(sourceBefore, File.ReadAllBytes(source));
    }

    [TestMethod]
    public async Task GetStatusAsync_ComputesStorageSizeAndInventory()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        SessionSaveResult save = await store.SaveAsync(PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "source.log")));

        LocalStorageStatus status = await store.GetStatusAsync();

        Assert.IsTrue(status.IsAccessible);
        Assert.IsTrue(status.SessionExists);
        Assert.IsTrue(status.ContainsRawParsedLogText);
        Assert.IsFalse(status.ContainsRecentExportMetadata);
        Assert.AreEqual(save.BytesWritten, status.ApproximateSizeBytes);
    }

    [TestMethod]
    public async Task Restore_PreservesDashboardSummaryExactly()
    {
        RestoredLocalSession restored = await SaveAndRestoreAsync();
        LogParsingSummary summary = restored.Analysis.Parsing.Summary;

        Assert.AreEqual(4, summary.TotalEntries);
        Assert.AreEqual(1, summary.InformationCount);
        Assert.AreEqual(2, summary.ErrorCount);
        Assert.AreEqual(1, summary.UnclassifiedEntries);
        Assert.AreEqual(3, summary.TimestampedEntries);
        Assert.IsTrue(summary.IsComplete);
    }

    [TestMethod]
    public async Task Restore_PreservesParsedEntriesRawTextAndTimestamps()
    {
        RestoredLocalSession restored = await SaveAndRestoreAsync();

        Assert.HasCount(4, restored.Analysis.Parsing.Entries);
        Assert.AreEqual(
            "2026-08-15 09:00:00 INFO service ready ✓",
            restored.Analysis.Parsing.Entries[0].RawText);
        Assert.AreEqual(1, restored.Analysis.Parsing.Entries[0].LineNumber);
        Assert.AreEqual(new DateOnly(2026, 8, 15), restored.Analysis.Parsing.Entries[0].Timestamp?.Date);
        Assert.AreEqual(
            "powershell.exe -Command inert; https://example.invalid",
            restored.Analysis.Parsing.Entries[3].RawText);
    }

    [TestMethod]
    public async Task Restore_PreservesDiagnostics()
    {
        RestoredLocalSession restored = await SaveAndRestoreAsync();

        Assert.HasCount(1, restored.Analysis.Parsing.Diagnostics);
        Assert.AreEqual(ParsingDiagnosticKind.MalformedTimestamp,
            restored.Analysis.Parsing.Diagnostics[0].Kind);
        Assert.AreEqual(4, restored.Analysis.Parsing.Diagnostics[0].LineNumber);
    }

    [TestMethod]
    public async Task Restore_PreservesSearchSeverityTimestampPageAndSelectionState()
    {
        RestoredLocalSession restored = await SaveAndRestoreAsync();

        Assert.AreEqual("Explorer", restored.UiState.SelectedSection);
        Assert.AreEqual("retry", restored.UiState.SearchText);
        Assert.AreEqual(
            LogSeverityFilter.Error | LogSeverityFilter.Critical,
            restored.UiState.SeverityFilter);
        Assert.AreEqual(TimestampPresenceFilter.HasTimestamp, restored.UiState.TimestampFilter);
        Assert.AreEqual(3, restored.UiState.SelectedEntryLineNumber);
    }

    [TestMethod]
    public async Task Restore_QueryStateReproducesVisibleResultCount()
    {
        RestoredLocalSession restored = await SaveAndRestoreAsync();
        var query = new LogEntryQuery(
            restored.UiState.SearchText,
            restored.UiState.SeverityFilter,
            restored.UiState.TimestampFilter);

        LogEntryQueryResult result = new LogEntryQueryService().Query(
            restored.Analysis.Parsing.Entries,
            query);

        Assert.AreEqual(2, result.VisibleEntries);
        Assert.AreEqual(4, result.TotalEntries);
        Assert.IsTrue(result.Entries.All(entry => entry.Severity == LogSeverity.Error));
    }

    [TestMethod]
    public async Task Restore_ReconstructsPatternsDeterministically()
    {
        RestoredLocalSession restored = await SaveAndRestoreAsync();

        PatternAnalysisResult patterns = new PatternAnalysisService().Analyze(
            restored.Analysis.Parsing.Entries);

        Assert.AreEqual(1, patterns.TotalRepeatedMessagePatterns);
        Assert.AreEqual(2, patterns.TopRepeatedMessages[0].OccurrenceCount);
        Assert.AreEqual("2026-08-15 09:00:10 ERROR retry failed",
            patterns.TopRepeatedMessages[0].RawText);
        Assert.IsTrue(restored.PatternsRequireReconstruction);
    }

    [TestMethod]
    public async Task Restore_ContainsEnoughDataForReportExportReadiness()
    {
        RestoredLocalSession restored = await SaveAndRestoreAsync();
        PatternAnalysisResult patterns = new PatternAnalysisService().Analyze(
            restored.Analysis.Parsing.Entries);
        var request = new ReportGenerationRequest(
            restored.Analysis,
            patterns,
            "0.1.0",
            PersistenceTestData.UpdatedAtUtc);

        ReportDocument report = new ReportGenerationService().Generate(
            request,
            ReportFormat.Json);

        using JsonDocument json = JsonDocument.Parse(report.Content);
        Assert.AreEqual(4,
            json.RootElement.GetProperty("parsing").GetProperty("totalParsedEntries").GetInt32());
    }

    [TestMethod]
    public async Task Restore_DoesNotRequireOrRereadOriginalSource()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "ORIGINAL SOURCE");
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        await store.SaveAsync(PersistenceTestData.Request(source));
        File.Delete(source);

        SessionLoadResult result = await store.LoadAsync();

        Assert.AreEqual(SessionLoadStatus.Restored, result.Status);
        Assert.AreEqual(source, result.Session?.Analysis.Source.FullPath);
    }

    [TestMethod]
    public async Task StoredJson_ExplicitlyDisclosesRawTextAndIntendedInventory()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        await store.SaveAsync(PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "source.log")));

        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, LocalSessionPolicy.SessionFileName)));
        JsonElement inventory = json.RootElement.GetProperty("inventory");
        Assert.IsTrue(inventory.GetProperty("containsRawParsedLogText").GetBoolean());
        Assert.IsTrue(inventory.GetProperty("containsOriginalSourcePath").GetBoolean());
        Assert.IsTrue(inventory.GetProperty("patternsRequireReconstruction").GetBoolean());
        Assert.IsFalse(inventory.GetProperty("containsRecentExportMetadata").GetBoolean());
        Assert.AreEqual(
            "2026-08-15 09:00:00 INFO service ready ✓",
            json.RootElement.GetProperty("parsing").GetProperty("entries")[0]
                .GetProperty("rawText").GetString());
    }

    [TestMethod]
    public async Task StoredJson_HasNoUnrelatedMachineTelemetryOrNetworkFields()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        await store.SaveAsync(PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "source.log")));
        string json = await File.ReadAllTextAsync(
            Path.Combine(root, LocalSessionPolicy.SessionFileName));

        foreach (string forbiddenProperty in new[]
                 {
                     "\"userName\"", "\"computerName\"", "\"environmentVariables\"",
                     "\"browserData\"", "\"telemetry\"", "\"analytics\"",
                     "\"network\"", "\"reportContents\""
                 })
        {
            Assert.DoesNotContain(forbiddenProperty, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public async Task SaveAsync_OversizedRawSessionKeepsPriorValidSnapshot()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        SessionCaptureRequest original = PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "original.log"));
        await store.SaveAsync(original);
        string huge = new('X', checked((int)LocalSessionPolicy.MaximumPersistedRawCharacters + 1));
        SessionCaptureRequest oversized = PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "oversized.log"),
            entries: [PersistenceTestData.Entry(1, huge)]);

        SessionSaveResult save = await store.SaveAsync(oversized);
        SessionLoadResult restored = await store.LoadAsync();

        Assert.AreEqual(SessionSaveStatus.TooLarge, save.Status);
        Assert.AreEqual("original.log", restored.Session?.Analysis.Source.FileName);
    }

    [TestMethod]
    public async Task SaveAsync_OversizedSearchStateIsExplainedWithoutChangingLiveQuerySupport()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var state = new SessionUiState(
            "Explorer",
            new string('S', LocalSessionPolicy.MaximumSearchTextCharacters + 1),
            LogSeverityFilter.All,
            TimestampPresenceFilter.All,
            null);

        SessionSaveResult result = await CreateStore(root).SaveAsync(
            PersistenceTestData.Request(
                Path.Combine(files.DirectoryPath, "source.log"),
                state));

        Assert.AreEqual(SessionSaveStatus.TooLarge, result.Status);
        StringAssert.Contains(result.Message, "search text");
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public async Task SaveAsync_LockedExistingSessionDoesNotCorruptPriorSnapshot()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        await store.SaveAsync(PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "original.log")));
        string sessionPath = Path.Combine(root, LocalSessionPolicy.SessionFileName);
        SessionSaveResult failed;
        await using (var lockHandle = new FileStream(
                         sessionPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.None))
        {
            failed = await store.SaveAsync(PersistenceTestData.Request(
                Path.Combine(files.DirectoryPath, "replacement.log")));
        }

        SessionLoadResult restored = await store.LoadAsync();

        Assert.AreEqual(SessionSaveStatus.Failed, failed.Status);
        Assert.AreEqual("original.log", restored.Session?.Analysis.Source.FileName);
    }

    [TestMethod]
    public async Task SaveAsync_WritesTempAndSessionOnlyInsideConfiguredRoot()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);

        await store.SaveAsync(PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "source.log")));

        string[] topLevelFiles = Directory.GetFiles(files.DirectoryPath);
        Assert.HasCount(0, topLevelFiles);
        string[] storedFiles = Directory.GetFiles(root);
        Assert.HasCount(1, storedFiles);
        Assert.AreEqual(LocalSessionPolicy.SessionFileName, Path.GetFileName(storedFiles[0]));
    }

    [TestMethod]
    public async Task SaveAsync_DoesNotWriteSourceOrExportFiles()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "SOURCE BYTES");
        string export = files.WriteText("summary.txt", "EXPORTED REPORT");
        byte[] sourceBefore = File.ReadAllBytes(source);
        byte[] exportBefore = File.ReadAllBytes(export);
        DateTime sourceTime = File.GetLastWriteTimeUtc(source);
        DateTime exportTime = File.GetLastWriteTimeUtc(export);
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);

        await CreateStore(root).SaveAsync(PersistenceTestData.Request(source));

        CollectionAssert.AreEqual(sourceBefore, File.ReadAllBytes(source));
        CollectionAssert.AreEqual(exportBefore, File.ReadAllBytes(export));
        Assert.AreEqual(sourceTime, File.GetLastWriteTimeUtc(source));
        Assert.AreEqual(exportTime, File.GetLastWriteTimeUtc(export));
    }

    [TestMethod]
    public async Task SaveAsync_PreCancelledOperationCreatesNoStorage()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            CreateStore(root).SaveAsync(
                PersistenceTestData.Request(Path.Combine(files.DirectoryPath, "source.log")),
                cancellation.Token));

        Assert.IsFalse(Directory.Exists(root));
    }

    private static LocalSessionStore CreateStore(string root) => new(
        new FixedLocalAppDataPathProvider(root));

    private static async Task<RestoredLocalSession> SaveAndRestoreAsync()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        var store = CreateStore(root);
        await store.SaveAsync(PersistenceTestData.Request(
            Path.Combine(files.DirectoryPath, "source.log")));
        SessionLoadResult result = await store.LoadAsync();
        Assert.IsNotNull(result.Session);
        return result.Session;
    }
}
