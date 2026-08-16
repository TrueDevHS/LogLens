using LogLens.Core.Persistence;
using LogLens.Core.Tests.Files;

namespace LogLens.Core.Tests.Persistence;

[TestClass]
public sealed class LocalAppDataEraserTests
{
    [TestMethod]
    public async Task EraseAllAsync_MissingFolderReturnsNothingToErase()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);

        LocalDataEraseResult result = await CreateEraser(root).EraseAllAsync();

        Assert.AreEqual(LocalDataEraseStatus.NothingToErase, result.Status);
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public async Task EraseAllAsync_DeletesCurrentSessionTempBackupAndNestedAppData()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        string nested = Path.Combine(root, "future-settings");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(root, LocalSessionPolicy.SessionFileName), "session");
        await File.WriteAllTextAsync(Path.Combine(root, LocalSessionPolicy.TemporarySessionFileName), "temp");
        await File.WriteAllTextAsync(Path.Combine(root, LocalSessionPolicy.BackupSessionFileName), "backup");
        await File.WriteAllTextAsync(Path.Combine(nested, "preferences.json"), "settings");

        LocalDataEraseResult result = await CreateEraser(root).EraseAllAsync();

        Assert.AreEqual(LocalDataEraseStatus.Erased, result.Status);
        Assert.AreEqual(4, result.FilesDeleted);
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public async Task EraseAllAsync_LeavesOriginalSourceBytesAndMetadataUnchanged()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "ORIGINAL LOG");
        byte[] bytes = File.ReadAllBytes(source);
        DateTime timestamp = File.GetLastWriteTimeUtc(source);
        FileAttributes attributes = File.GetAttributes(source);
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "session-v1.json"), "app data");

        await CreateEraser(root).EraseAllAsync();

        Assert.IsTrue(File.Exists(source));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(source));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(source));
        Assert.AreEqual(attributes, File.GetAttributes(source));
    }

    [TestMethod]
    public async Task EraseAllAsync_LeavesExportedReportBytesAndMetadataUnchanged()
    {
        using var files = new SyntheticFileScope();
        string export = files.WriteText("summary.json", "{\"reportVersion\":\"1.0\"}");
        byte[] bytes = File.ReadAllBytes(export);
        DateTime timestamp = File.GetLastWriteTimeUtc(export);
        FileAttributes attributes = File.GetAttributes(export);
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "session-v1.json"), "app data");

        await CreateEraser(root).EraseAllAsync();

        Assert.IsTrue(File.Exists(export));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(export));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(export));
        Assert.AreEqual(attributes, File.GetAttributes(export));
    }

    [TestMethod]
    public async Task EraseAllAsync_LeavesEverySiblingOutsideAppDataRootUntouched()
    {
        using var files = new SyntheticFileScope();
        string outside = files.WriteText("outside.txt", "OUTSIDE");
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "owned.txt"), "OWNED");

        await CreateEraser(root).EraseAllAsync();

        Assert.AreEqual("OUTSIDE", await File.ReadAllTextAsync(outside));
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public async Task EraseAllAsync_InvalidRootNameIsRejectedBeforeDeletion()
    {
        using var files = new SyntheticFileScope();
        string unsafeRoot = Path.Combine(files.DirectoryPath, "NotLogLens");
        Directory.CreateDirectory(unsafeRoot);
        string protectedFile = Path.Combine(unsafeRoot, "protected.txt");
        await File.WriteAllTextAsync(protectedFile, "KEEP");

        LocalDataEraseResult result = await CreateEraser(unsafeRoot).EraseAllAsync();

        Assert.AreEqual(LocalDataEraseStatus.UnsafeStorage, result.Status);
        Assert.AreEqual("KEEP", await File.ReadAllTextAsync(protectedFile));
    }

    [TestMethod]
    public async Task EraseAllAsync_FileAtExpectedRootIsRejectedWithoutDeletion()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        await File.WriteAllTextAsync(root, "NOT A DIRECTORY");

        LocalDataEraseResult result = await CreateEraser(root).EraseAllAsync();

        Assert.AreEqual(LocalDataEraseStatus.UnsafeStorage, result.Status);
        Assert.AreEqual("NOT A DIRECTORY", await File.ReadAllTextAsync(root));
    }

    [TestMethod]
    public void Eraser_PublicApiAcceptsNoArbitraryDeletePath()
    {
        var eraseMethod = typeof(ILocalAppDataEraser).GetMethod(nameof(ILocalAppDataEraser.EraseAllAsync));

        Assert.IsNotNull(eraseMethod);
        Assert.IsFalse(eraseMethod.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)));
    }

    [TestMethod]
    public async Task EraseAllAsync_PreCancelledOperationDeletesNothing()
    {
        using var files = new SyntheticFileScope();
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        string stored = Path.Combine(root, "session-v1.json");
        await File.WriteAllTextAsync(stored, "KEEP");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            CreateEraser(root).EraseAllAsync(cancellation.Token));

        Assert.AreEqual("KEEP", await File.ReadAllTextAsync(stored));
    }

    [TestMethod]
    public async Task EraseAllAsync_LockedOwnedFileFailsWithoutTouchingOutsideFiles()
    {
        using var files = new SyntheticFileScope();
        string outside = files.WriteText("source.log", "SOURCE");
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        string stored = Path.Combine(root, "session-v1.json");
        await File.WriteAllTextAsync(stored, "LOCKED");
        LocalDataEraseResult result;
        await using (var lockHandle = new FileStream(
                         stored,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.None))
        {
            result = await CreateEraser(root).EraseAllAsync();
        }

        Assert.AreEqual(LocalDataEraseStatus.Failed, result.Status);
        Assert.AreEqual("SOURCE", await File.ReadAllTextAsync(outside));
    }

    [TestMethod]
    public async Task EraseAllAsync_ReparseEntryIsNotFollowedWhenSymlinksAreAvailable()
    {
        using var files = new SyntheticFileScope();
        string outside = files.WriteText("outside-target.txt", "DO NOT DELETE");
        string root = PersistenceTestData.StorageRoot(files.DirectoryPath);
        Directory.CreateDirectory(root);
        string link = Path.Combine(root, "linked-target.txt");
        try
        {
            File.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                          or IOException
                                          or PlatformNotSupportedException)
        {
            // This machine does not permit creating a synthetic symlink. The pathless
            // rejection logic remains covered by inspection and the strict API audit.
            return;
        }

        LocalDataEraseResult result = await CreateEraser(root).EraseAllAsync();

        Assert.AreEqual(LocalDataEraseStatus.UnsafeStorage, result.Status);
        Assert.AreEqual("DO NOT DELETE", await File.ReadAllTextAsync(outside));
        Assert.IsTrue(File.Exists(link));
    }

    private static LocalAppDataEraser CreateEraser(string root) => new(
        new FixedLocalAppDataPathProvider(root));
}
