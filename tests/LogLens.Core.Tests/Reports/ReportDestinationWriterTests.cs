using System.Text;
using LogLens.Core.Reports;
using LogLens.Core.Tests.Files;

namespace LogLens.Core.Tests.Reports;

[TestClass]
public sealed class ReportDestinationWriterTests
{
    private readonly ReportDestinationWriter _writer = new();
    private readonly ReportGenerationService _generator = new();

    [TestMethod]
    public async Task WriteAsync_CreatesNewTextReportAtExplicitDestination()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "INFO source");
        string destination = Path.Combine(files.DirectoryPath, "summary.txt");
        ReportDocument report = CreateReport(ReportFormat.Text);

        ReportWriteResult result = await _writer.WriteAsync(
            new ReportWriteRequest(report, source, destination, OverwriteConfirmed: false));

        Assert.AreEqual("summary.txt", result.DestinationFileName);
        Assert.IsGreaterThan(0L, result.BytesWritten);
        Assert.AreEqual(report.Content, await File.ReadAllTextAsync(destination, Encoding.UTF8));
    }

    [TestMethod]
    public async Task WriteAsync_CreatesNewValidJsonReport()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "INFO source");
        string destination = Path.Combine(files.DirectoryPath, "summary.json");
        ReportDocument report = CreateReport(ReportFormat.Json);

        await _writer.WriteAsync(
            new ReportWriteRequest(report, source, destination, OverwriteConfirmed: false));

        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(destination));
        Assert.AreEqual("1.0", json.RootElement.GetProperty("reportVersion").GetString());
    }

    [TestMethod]
    public async Task WriteAsync_DestinationEqualToSourceIsRejectedWithoutModification()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.txt", "ORIGINAL SOURCE");
        byte[] bytesBefore = File.ReadAllBytes(source);
        DateTime timestampBefore = File.GetLastWriteTimeUtc(source);
        FileAttributes attributesBefore = File.GetAttributes(source);

        ReportExportException exception = await Assert.ThrowsExactlyAsync<ReportExportException>(() =>
            _writer.WriteAsync(new ReportWriteRequest(
                CreateReport(ReportFormat.Text),
                source,
                source,
                OverwriteConfirmed: true)));

        Assert.AreEqual(ReportExportErrorKind.SourceOverwrite, exception.Kind);
        CollectionAssert.AreEqual(bytesBefore, File.ReadAllBytes(source));
        Assert.AreEqual(timestampBefore, File.GetLastWriteTimeUtc(source));
        Assert.AreEqual(attributesBefore, File.GetAttributes(source));
    }

    [TestMethod]
    public async Task WriteAsync_CanonicalEquivalentSourcePathIsRejected()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.txt", "ORIGINAL SOURCE");
        string equivalent = Path.Combine(files.DirectoryPath, "unused", "..", "source.txt");

        ReportExportException exception = await Assert.ThrowsExactlyAsync<ReportExportException>(() =>
            _writer.WriteAsync(new ReportWriteRequest(
                CreateReport(ReportFormat.Text),
                source,
                equivalent,
                OverwriteConfirmed: true)));

        Assert.AreEqual(ReportExportErrorKind.SourceOverwrite, exception.Kind);
        Assert.AreEqual("ORIGINAL SOURCE", File.ReadAllText(source));
    }

    [TestMethod]
    public async Task WriteAsync_SuccessLeavesSourceBytesLengthTimestampAndAttributesUnchanged()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "INFO source\nERROR inert");
        string destination = Path.Combine(files.DirectoryPath, "summary.txt");
        byte[] bytesBefore = File.ReadAllBytes(source);
        long lengthBefore = new FileInfo(source).Length;
        DateTime timestampBefore = File.GetLastWriteTimeUtc(source);
        FileAttributes attributesBefore = File.GetAttributes(source);

        await _writer.WriteAsync(new ReportWriteRequest(
            CreateReport(ReportFormat.Text),
            source,
            destination,
            OverwriteConfirmed: false));

        CollectionAssert.AreEqual(bytesBefore, File.ReadAllBytes(source));
        Assert.AreEqual(lengthBefore, new FileInfo(source).Length);
        Assert.AreEqual(timestampBefore, File.GetLastWriteTimeUtc(source));
        Assert.AreEqual(attributesBefore, File.GetAttributes(source));
    }

    [TestMethod]
    public async Task WriteAsync_ExistingDestinationRequiresExplicitOverwriteConfirmation()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "INFO source");
        string destination = files.WriteText("summary.txt", "EXISTING REPORT");

        ReportExportException exception = await Assert.ThrowsExactlyAsync<ReportExportException>(() =>
            _writer.WriteAsync(new ReportWriteRequest(
                CreateReport(ReportFormat.Text),
                source,
                destination,
                OverwriteConfirmed: false)));

        Assert.AreEqual(ReportExportErrorKind.DestinationExists, exception.Kind);
        Assert.AreEqual("EXISTING REPORT", File.ReadAllText(destination));
    }

    [TestMethod]
    public async Task WriteAsync_ConfirmedOverwriteReplacesOnlyDestination()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "SOURCE");
        string destination = files.WriteText("summary.txt", "OLD REPORT");
        ReportDocument report = CreateReport(ReportFormat.Text);

        await _writer.WriteAsync(new ReportWriteRequest(
            report,
            source,
            destination,
            OverwriteConfirmed: true));

        Assert.AreEqual("SOURCE", File.ReadAllText(source));
        Assert.AreEqual(report.Content, File.ReadAllText(destination));
    }

    [TestMethod]
    public async Task WriteAsync_InvalidMissingDestinationFolderIsRejected()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "SOURCE");
        string destination = Path.Combine(files.DirectoryPath, "missing", "summary.txt");

        ReportExportException exception = await Assert.ThrowsExactlyAsync<ReportExportException>(() =>
            _writer.WriteAsync(new ReportWriteRequest(
                CreateReport(ReportFormat.Text),
                source,
                destination,
                OverwriteConfirmed: false)));

        Assert.AreEqual(ReportExportErrorKind.InvalidDestination, exception.Kind);
        Assert.IsFalse(File.Exists(destination));
        Assert.AreEqual("SOURCE", File.ReadAllText(source));
    }

    [TestMethod]
    public async Task WriteAsync_MismatchedExtensionIsRejected()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "SOURCE");
        string destination = Path.Combine(files.DirectoryPath, "summary.json");

        ReportExportException exception = await Assert.ThrowsExactlyAsync<ReportExportException>(() =>
            _writer.WriteAsync(new ReportWriteRequest(
                CreateReport(ReportFormat.Text),
                source,
                destination,
                OverwriteConfirmed: false)));

        Assert.AreEqual(ReportExportErrorKind.UnsupportedFormat, exception.Kind);
        Assert.IsFalse(File.Exists(destination));
    }

    [TestMethod]
    public async Task WriteAsync_ReadOnlyDestinationReportsPermissionFailureAndLeavesSourceUnchanged()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "SOURCE");
        string destination = files.WriteText("readonly.txt", "READ ONLY REPORT");
        File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);

        try
        {
            ReportExportException exception = await Assert.ThrowsExactlyAsync<ReportExportException>(() =>
                _writer.WriteAsync(new ReportWriteRequest(
                    CreateReport(ReportFormat.Text),
                    source,
                    destination,
                    OverwriteConfirmed: true)));

            Assert.AreEqual(ReportExportErrorKind.PermissionDenied, exception.Kind);
            Assert.AreEqual("SOURCE", File.ReadAllText(source));
            Assert.AreEqual("READ ONLY REPORT", File.ReadAllText(destination));
        }
        finally
        {
            File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
        }
    }

    [TestMethod]
    public async Task WriteAsync_LockedDestinationReportsWriteFailureAndLeavesSourceUnchanged()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "SOURCE");
        string destination = files.WriteText("locked.txt", "LOCKED REPORT");
        using var lockHandle = new FileStream(
            destination,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        ReportExportException exception = await Assert.ThrowsExactlyAsync<ReportExportException>(() =>
            _writer.WriteAsync(new ReportWriteRequest(
                CreateReport(ReportFormat.Text),
                source,
                destination,
                OverwriteConfirmed: true)));

        Assert.AreEqual(ReportExportErrorKind.DestinationUnavailable, exception.Kind);
        Assert.AreEqual("SOURCE", File.ReadAllText(source));
    }

    [TestMethod]
    public async Task WriteAsync_PreCancelledOperationCreatesNothingAndLeavesSourceUnchanged()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "SOURCE");
        string destination = Path.Combine(files.DirectoryPath, "cancelled.txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            _writer.WriteAsync(
                new ReportWriteRequest(
                    CreateReport(ReportFormat.Text),
                    source,
                    destination,
                    OverwriteConfirmed: false),
                cancellation.Token));

        Assert.IsFalse(File.Exists(destination));
        Assert.AreEqual("SOURCE", File.ReadAllText(source));
    }

    [TestMethod]
    public async Task WriteAsync_UncDestinationIsRejectedBeforeWriting()
    {
        using var files = new SyntheticFileScope();
        string source = files.WriteText("source.log", "SOURCE");

        ReportExportException exception = await Assert.ThrowsExactlyAsync<ReportExportException>(() =>
            _writer.WriteAsync(new ReportWriteRequest(
                CreateReport(ReportFormat.Text),
                source,
                @"\\server\share\summary.txt",
                OverwriteConfirmed: false)));

        Assert.AreEqual(ReportExportErrorKind.NetworkLocation, exception.Kind);
        Assert.AreEqual("SOURCE", File.ReadAllText(source));
    }

    private ReportDocument CreateReport(ReportFormat format) =>
        _generator.Generate(ReportTestData.Request(), format);
}
