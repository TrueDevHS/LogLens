using System.Text;
using LogLens.Core.Parsing;

namespace LogLens.Core.Tests.Parsing;

[TestClass]
public sealed class PlainTextLogParserTests
{
    private readonly PlainTextLogParser _parser = new();

    [TestMethod]
    public async Task ParseAsync_EmptyInputReturnsTruthfulEmptySummary()
    {
        LogParsingResult result = await ParseAsync(string.Empty);

        Assert.AreEqual(0, result.Summary.TotalEntries);
        Assert.AreEqual(0, result.Summary.ClassifiedEntries);
        Assert.AreEqual(0, result.Summary.UnclassifiedEntries);
        Assert.IsTrue(result.Summary.IsComplete);
        Assert.HasCount(0, result.Entries);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Kind == ParsingDiagnosticKind.EmptyFile));
    }

    [TestMethod]
    public async Task ParseAsync_OneLinePreservesRawTextAndLineNumber()
    {
        const string rawLine = "2026-08-14 09:10:11 INFO Service started";

        LogParsingResult result = await ParseAsync(rawLine);

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(1, result.Entries[0].LineNumber);
        Assert.AreEqual(rawLine, result.Entries[0].RawText);
        Assert.AreEqual("Service started", result.Entries[0].Message);
        Assert.AreEqual(LogSeverity.Information, result.Entries[0].Severity);
    }

    [TestMethod]
    public async Task ParseAsync_OrdinaryMultiLineLogProducesAccurateCounts()
    {
        const string text = """
            TRACE trace entry
            debug debug entry
            Info info entry
            INFORMATION information entry
            warn warning entry
            WARNING warning entry
            error error entry
            ERR compact error entry
            critical critical entry
            FATAL fatal entry
            ordinary unclassified entry
            """;

        LogParsingResult result = await ParseAsync(text);

        Assert.AreEqual(11, result.Summary.TotalEntries);
        Assert.AreEqual(10, result.Summary.ClassifiedEntries);
        Assert.AreEqual(1, result.Summary.UnclassifiedEntries);
        Assert.AreEqual(1, result.Summary.TraceCount);
        Assert.AreEqual(1, result.Summary.DebugCount);
        Assert.AreEqual(2, result.Summary.InformationCount);
        Assert.AreEqual(2, result.Summary.WarningCount);
        Assert.AreEqual(2, result.Summary.ErrorCount);
        Assert.AreEqual(2, result.Summary.CriticalCount);
    }

    [TestMethod]
    [DataRow("trace", LogSeverity.Trace)]
    [DataRow("DeBuG", LogSeverity.Debug)]
    [DataRow("info", LogSeverity.Information)]
    [DataRow("InFoRmAtIoN", LogSeverity.Information)]
    [DataRow("warn", LogSeverity.Warning)]
    [DataRow("WaRnInG", LogSeverity.Warning)]
    [DataRow("error", LogSeverity.Error)]
    [DataRow("ErR", LogSeverity.Error)]
    [DataRow("critical", LogSeverity.Critical)]
    [DataRow("FaTaL", LogSeverity.Critical)]
    public async Task ParseAsync_RecognisesSeverityCaseInsensitively(
        string token,
        LogSeverity expected)
    {
        LogParsingResult result = await ParseAsync($"[{token}] Synthetic message");

        Assert.AreEqual(expected, result.Entries[0].Severity);
    }

    [TestMethod]
    public async Task ParseAsync_DoesNotMatchSeverityInsideAnotherWord()
    {
        LogParsingResult result = await ParseAsync("A warningly vague informationalism");

        Assert.AreEqual(LogSeverity.Unknown, result.Entries[0].Severity);
        Assert.AreEqual(1, result.Summary.UnclassifiedEntries);
    }

    [TestMethod]
    [DataRow("2026-08-14T10:11:12Z INFO ISO UTC")]
    [DataRow("2026-08-14T10:11:12.1234567+01:00 INFO ISO offset")]
    [DataRow("2026-08-14 10:11:12 WARN Year first")]
    [DataRow("14/08/2026 10:11:12 ERROR Day first")]
    [DataRow("10:11:12 DEBUG Time only")]
    [DataRow("[2026-08-14 10:11:12] INFO Bracketed")]
    public async Task ParseAsync_RecognisesSupportedLeadingTimestamp(string line)
    {
        LogParsingResult result = await ParseAsync(line);

        Assert.IsNotNull(result.Entries[0].Timestamp);
        Assert.AreEqual(1, result.Summary.TimestampedEntries);
        Assert.IsTrue(result.Summary.HasDetectedTimestamps);
    }

    [TestMethod]
    public async Task ParseAsync_SupportsMixedRecognisedTimestampFormats()
    {
        const string text = """
            2026-08-14T10:11:12Z INFO First
            2026-08-14 10:12:13 WARN Second
            14/08/2026 10:13:14 ERROR Third
            10:14:15 DEBUG Fourth
            no timestamp here
            """;

        LogParsingResult result = await ParseAsync(text);

        Assert.AreEqual(5, result.Summary.TotalEntries);
        Assert.AreEqual(4, result.Summary.TimestampedEntries);
    }

    [TestMethod]
    public async Task ParseAsync_MissingTimestampIsValid()
    {
        LogParsingResult result = await ParseAsync("INFO No timestamp is present");

        Assert.IsNull(result.Entries[0].Timestamp);
        Assert.AreEqual(0, result.Summary.TimestampedEntries);
        Assert.IsFalse(result.Diagnostics.Any(diagnostic =>
            diagnostic.Kind == ParsingDiagnosticKind.MalformedTimestamp));
    }

    [TestMethod]
    public async Task ParseAsync_MalformedTimestampIsPreservedAndDiagnosed()
    {
        const string rawLine = "2026-99-41 27:88:66 ERROR Invalid timestamp";

        LogParsingResult result = await ParseAsync(rawLine);

        Assert.IsNull(result.Entries[0].Timestamp);
        Assert.AreEqual(rawLine, result.Entries[0].RawText);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Kind == ParsingDiagnosticKind.MalformedTimestamp
            && diagnostic.LineNumber == 1));
    }

    [TestMethod]
    public async Task ParseAsync_BlankLinesRemainRepresentedAndNumbered()
    {
        LogParsingResult result = await ParseAsync("INFO First\n\nERROR Third");

        Assert.AreEqual(3, result.Summary.TotalEntries);
        Assert.AreEqual(2, result.Entries[1].LineNumber);
        Assert.AreEqual(string.Empty, result.Entries[1].RawText);
        Assert.AreEqual(LogSeverity.Unknown, result.Entries[1].Severity);
    }

    [TestMethod]
    public async Task ParseAsync_UnusuallyLongLineIsFullyPreservedAndDiagnosed()
    {
        string rawLine = "INFO " + new string('å', LogParsingPolicy.LongLineWarningThresholdCharacters);

        LogParsingResult result = await ParseAsync(rawLine);

        Assert.AreEqual(rawLine, result.Entries[0].RawText);
        Assert.AreEqual(rawLine.Length, result.Entries[0].RawText.Length);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Kind == ParsingDiagnosticKind.UnusuallyLongLine));
    }

    [TestMethod]
    public async Task ParseAsync_UnicodeTextIsPreservedExactly()
    {
        const string rawLine = "INFO Café — 日本語 🔍 مرحبا";

        LogParsingResult result = await ParseAsync(rawLine);

        Assert.AreEqual(rawLine, result.Entries[0].RawText);
    }

    [TestMethod]
    public async Task ParseAsync_CommandAndScriptLookingTextRemainsInert()
    {
        const string rawLine = "powershell.exe -Command \"Remove-Item C:\\Temp\\sample.txt\"; <script>alert('x')</script>";

        LogParsingResult result = await ParseAsync(rawLine);

        Assert.AreEqual(rawLine, result.Entries[0].RawText);
        Assert.AreEqual(rawLine, result.Entries[0].Message);
        Assert.AreEqual(LogSeverity.Unknown, result.Entries[0].Severity);
    }

    [TestMethod]
    public async Task ParseAsync_UrlRemainsPlainInertText()
    {
        const string rawLine = "INFO See https://example.invalid/path?q=value but do not open it";

        LogParsingResult result = await ParseAsync(rawLine);

        Assert.AreEqual(rawLine, result.Entries[0].RawText);
        StringAssert.Contains(result.Entries[0].Message, "https://example.invalid/path?q=value");
    }

    [TestMethod]
    public async Task ParseAsync_ControlCharactersDoNotCrashAndArePreserved()
    {
        const string rawLine = "INFO inert\0content\u200Bstill text";

        LogParsingResult result = await ParseAsync(rawLine);

        Assert.AreEqual(rawLine, result.Entries[0].RawText);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Kind == ParsingDiagnosticKind.ControlCharacters));
    }

    [TestMethod]
    public async Task ParseAsync_StrictUtf8RejectsMalformedByteSequenceSafely()
    {
        byte[] malformedUtf8 = [0x49, 0x4E, 0x46, 0x4F, 0x20, 0xC3, 0x28];
        await using var stream = new MemoryStream(malformedUtf8, writable: false);

        LogParsingException exception = await Assert.ThrowsExactlyAsync<LogParsingException>(
            () => _parser.ParseAsync(stream));

        Assert.AreEqual(LogParsingErrorKind.InvalidEncoding, exception.Kind);
        StringAssert.Contains(exception.Message, "not modified");
    }

    [TestMethod]
    [DataRow(false, "UTF-16 LE")]
    [DataRow(true, "UTF-16 BE")]
    public async Task ParseAsync_SupportsBomMarkedUtf16(bool bigEndian, string displayName)
    {
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true, throwOnInvalidBytes: true);
        byte[] preamble = encoding.GetPreamble();
        byte[] body = encoding.GetBytes("INFO Unicode encoding");
        byte[] bytes = [.. preamble, .. body];
        await using var stream = new MemoryStream(bytes, writable: false);

        LogParsingResult result = await _parser.ParseAsync(stream);

        Assert.AreEqual(displayName, result.DetectedEncoding);
        Assert.AreEqual("INFO Unicode encoding", result.Entries[0].RawText);
    }

    [TestMethod]
    public async Task ParseAsync_RejectsUtf32WithFriendlyError()
    {
        var encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes("INFO UTF-32")];
        await using var stream = new MemoryStream(bytes, writable: false);

        LogParsingException exception = await Assert.ThrowsExactlyAsync<LogParsingException>(
            () => _parser.ParseAsync(stream));

        Assert.AreEqual(LogParsingErrorKind.UnsupportedEncoding, exception.Kind);
        StringAssert.Contains(exception.Message, "UTF-32");
    }

    [TestMethod]
    public async Task ParseAsync_RefusesWritableInputStream()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("INFO writable"), writable: true);

        LogParsingException exception = await Assert.ThrowsExactlyAsync<LogParsingException>(
            () => _parser.ParseAsync(stream));

        Assert.AreEqual(LogParsingErrorKind.UnsafeInputStream, exception.Kind);
    }

    [TestMethod]
    public async Task ParseAsync_ObservesCancellation()
    {
        await using var stream = CreateReadOnlyUtf8Stream("INFO cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cancellation.Token));
    }

    [TestMethod]
    public async Task ParseAsync_EntryLimitProducesExplicitlyIncompleteBoundedResult()
    {
        string text = string.Join('\n', Enumerable.Repeat("INFO bounded", LogParsingPolicy.MaximumEntries + 1));

        LogParsingResult result = await ParseAsync(text);

        Assert.AreEqual(LogParsingPolicy.MaximumEntries, result.Summary.TotalEntries);
        Assert.IsFalse(result.Summary.IsComplete);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Kind == ParsingDiagnosticKind.EntryLimitReached));
    }

    private async Task<LogParsingResult> ParseAsync(string text)
    {
        await using MemoryStream stream = CreateReadOnlyUtf8Stream(text);
        return await _parser.ParseAsync(stream);
    }

    private static MemoryStream CreateReadOnlyUtf8Stream(string text) =>
        new(Encoding.UTF8.GetBytes(text), writable: false);
}
