using System.Security.Cryptography;
using System.Text;
using LogLens.Core.Files;

namespace LogLens.Core.Tests.Files;

[TestClass]
public sealed class ReadOnlySourceFileLoaderTests
{
    private readonly ReadOnlySourceFileLoader _loader = new();

    [TestMethod]
    [DataRow("application.log")]
    [DataRow("application.txt")]
    public async Task InspectAsync_ReadsSupportedFileAndCalculatesSha256(string fileName)
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText(fileName, "2026-08-14 10:30:00 INFO Synthetic entry");
        string expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        SourceFileInspection inspection = await _loader.InspectAsync(path);

        Assert.AreEqual(fileName, inspection.FileName);
        Assert.AreEqual(expectedHash, inspection.Sha256);
        Assert.IsFalse(inspection.SourceChangedDuringRead);
        Assert.IsTrue(inspection.BeforeRead.Matches(inspection.AfterRead));
    }

    [TestMethod]
    public async Task ReadAsync_ProvidesStrictlyReadOnlyStream()
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText("readonly.log", "Synthetic content");

        SourceFileReadResult<long> result = await _loader.ReadAsync(
            path,
            (stream, _) =>
            {
                Assert.IsTrue(stream.CanRead);
                Assert.IsFalse(stream.CanWrite);
                Assert.ThrowsExactly<NotSupportedException>(() => stream.WriteByte(42));
                return Task.FromResult(stream.Length);
            });

        Assert.AreEqual(new FileInfo(path).Length, result.Data);
    }

    [TestMethod]
    public async Task InspectAsync_LeavesSourceBytesAndMetadataUnchanged()
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText("integrity.log", "Synthetic integrity check");
        byte[] bytesBefore = File.ReadAllBytes(path);
        var metadataBefore = new SourceFileSnapshot(
            new FileInfo(path).Length,
            File.GetLastWriteTimeUtc(path));
        FileAttributes attributesBefore = File.GetAttributes(path);

        SourceFileInspection inspection = await _loader.InspectAsync(path);

        CollectionAssert.AreEqual(bytesBefore, File.ReadAllBytes(path));
        Assert.AreEqual(metadataBefore.Length, new FileInfo(path).Length);
        Assert.AreEqual(metadataBefore.LastWriteTimeUtc, File.GetLastWriteTimeUtc(path));
        Assert.AreEqual(attributesBefore, File.GetAttributes(path));
        Assert.IsFalse(inspection.SourceChangedDuringRead);
    }

    [TestMethod]
    public async Task ReadAsync_LeavesSourceUnchangedWhenDownstreamReadFails()
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText("failure.log", "Synthetic failure case");
        byte[] bytesBefore = File.ReadAllBytes(path);
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(path);

        try
        {
            await _loader.ReadAsync<int>(
                path,
                (_, _) => throw new InvalidDataException("Synthetic downstream failure."));
            Assert.Fail("Expected the synthetic downstream read to fail.");
        }
        catch (InvalidDataException)
        {
            // Expected: the loader must still dispose its read-only handle.
        }

        CollectionAssert.AreEqual(bytesBefore, File.ReadAllBytes(path));
        Assert.AreEqual(lastWriteBefore, File.GetLastWriteTimeUtc(path));
    }

    [TestMethod]
    public async Task ReadAsync_CancellationDisposesHandleAndLeavesSourceUnchanged()
    {
        using var files = new SyntheticFileScope();
        string content = new('S', 256 * 1024);
        string path = files.WriteText("cancelled.log", content);
        byte[] bytesBefore = File.ReadAllBytes(path);
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(path);
        using var cancellation = new CancellationTokenSource();
        var readingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<SourceFileReadResult<int>> loadTask = _loader.ReadAsync(
            path,
            async (stream, cancellationToken) =>
            {
                byte[] buffer = new byte[128];
                await stream.ReadExactlyAsync(buffer, cancellationToken);
                readingStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            },
            cancellation.Token);

        await readingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        try
        {
            await loadTask;
            Assert.Fail("Expected loading to be cancelled.");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        CollectionAssert.AreEqual(bytesBefore, File.ReadAllBytes(path));
        Assert.AreEqual(lastWriteBefore, File.GetLastWriteTimeUtc(path));

        using var exclusiveHandle = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        Assert.IsFalse(exclusiveHandle.SafeFileHandle.IsInvalid);
    }

    [TestMethod]
    public async Task InspectAsync_ReturnsFriendlyFailureForLockedFile()
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText("locked.log", "Synthetic locked file");
        await using var exclusiveHandle = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        SourceFileException exception = await CaptureSourceFileExceptionAsync(
            () => _loader.InspectAsync(path));

        Assert.AreEqual(SourceFileErrorKind.FileUnavailable, exception.Kind);
        StringAssert.Contains(exception.Message, "locked");
    }

    [TestMethod]
    public async Task ReadAsync_DetectsExternalMetadataChange()
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText("changing.log", "Initial synthetic content");

        SourceFileReadResult<int> result = await _loader.ReadAsync(
            path,
            async (stream, cancellationToken) =>
            {
                byte[] buffer = new byte[8];
                await stream.ReadExactlyAsync(buffer, cancellationToken);
                await File.AppendAllTextAsync(
                    path,
                    " - externally changed by synthetic test",
                    Encoding.UTF8,
                    cancellationToken);
                return 0;
            });

        Assert.IsTrue(result.SourceChangedDuringRead);
        Assert.IsFalse(result.BeforeRead.Matches(result.AfterRead));
    }

    private static async Task<SourceFileException> CaptureSourceFileExceptionAsync(
        Func<Task> action)
    {
        try
        {
            await action();
            Assert.Fail("Expected source-file loading to fail.");
            throw new InvalidOperationException("Unreachable.");
        }
        catch (SourceFileException exception)
        {
            return exception;
        }
    }
}
