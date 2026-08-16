using System.Security.Cryptography;
using LogLens.Core.Analysis;
using LogLens.Core.Files;
using LogLens.Core.Tests.Files;

namespace LogLens.Core.Tests.Analysis;

[TestClass]
public sealed class LogAnalysisServiceTests
{
    private readonly LogAnalysisService _service = new();

    [TestMethod]
    public async Task AnalyzeAsync_UsesReadOnlyPipelineAndReturnsParsedSummary()
    {
        using var files = new SyntheticFileScope();
        const string text = """
            2026-08-14 10:00:00 INFO Started
            2026-08-14 10:00:01 WARN Slow response
            2026-08-14 10:00:02 ERROR Failed request
            unclassified detail
            """;
        string path = files.WriteText("analysis.log", text);
        string expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        LogAnalysisResult result = await _service.AnalyzeAsync(path);

        Assert.AreEqual("analysis.log", result.Source.FileName);
        Assert.AreEqual(expectedHash, result.Source.Sha256);
        Assert.IsFalse(result.Source.SourceChangedDuringRead);
        Assert.AreEqual(4, result.Parsing.Summary.TotalEntries);
        Assert.AreEqual(1, result.Parsing.Summary.InformationCount);
        Assert.AreEqual(1, result.Parsing.Summary.WarningCount);
        Assert.AreEqual(1, result.Parsing.Summary.ErrorCount);
        Assert.AreEqual(1, result.Parsing.Summary.UnclassifiedEntries);
    }

    [TestMethod]
    public async Task AnalyzeAsync_LeavesSourceBytesAndMetadataUnchangedAfterParsing()
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText(
            "integrity.log",
            "2026-08-14T10:00:00Z INFO Synthetic integrity check\nERROR Still inert");
        byte[] bytesBefore = File.ReadAllBytes(path);
        long lengthBefore = new FileInfo(path).Length;
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(path);
        FileAttributes attributesBefore = File.GetAttributes(path);

        LogAnalysisResult result = await _service.AnalyzeAsync(path);

        CollectionAssert.AreEqual(bytesBefore, File.ReadAllBytes(path));
        Assert.AreEqual(lengthBefore, new FileInfo(path).Length);
        Assert.AreEqual(lastWriteBefore, File.GetLastWriteTimeUtc(path));
        Assert.AreEqual(attributesBefore, File.GetAttributes(path));
        Assert.IsTrue(result.Source.BeforeRead.Matches(result.Source.AfterRead));
        Assert.IsFalse(result.Source.SourceChangedDuringRead);

        using var exclusiveHandle = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        Assert.IsFalse(exclusiveHandle.SafeFileHandle.IsInvalid);
    }

    [TestMethod]
    public async Task AnalyzeAsync_CancellationLeavesSourceUnchangedAndReleasesHandle()
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText("cancelled.log", new string('S', 128 * 1024));
        byte[] bytesBefore = File.ReadAllBytes(path);
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => _service.AnalyzeAsync(path, cancellation.Token));

        CollectionAssert.AreEqual(bytesBefore, File.ReadAllBytes(path));
        Assert.AreEqual(lastWriteBefore, File.GetLastWriteTimeUtc(path));

        using var exclusiveHandle = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        Assert.IsFalse(exclusiveHandle.SafeFileHandle.IsInvalid);
    }
}
