using System.Text.Json;
using LogLens.Core.Parsing;
using LogLens.Core.Reports;

namespace LogLens.Core.Tests.Reports;

[TestClass]
public sealed class ReportGenerationServiceTests
{
    private readonly ReportGenerationService _service = new();

    [TestMethod]
    public void Generate_NormalTextReportHasProfessionalRequiredSections()
    {
        ParsedLogEntry[] entries =
        [
            ReportTestData.Entry(1, "INFO ready"),
            ReportTestData.Entry(2, "WARN delayed", LogSeverity.Warning),
            ReportTestData.Entry(3, "ERROR failed", LogSeverity.Error)
        ];

        ReportDocument report = _service.Generate(
            ReportTestData.Request(entries),
            ReportFormat.Text);

        StringAssert.StartsWith(report.Content, "LogLens Analysis Summary");
        StringAssert.Contains(report.Content, "Source\n------");
        StringAssert.Contains(report.Content, "Integrity\n---------");
        StringAssert.Contains(report.Content, "Parsing Summary\n---------------");
        StringAssert.Contains(report.Content, "Severity Summary\n----------------");
        StringAssert.Contains(report.Content, "Repeated Messages\n-----------------");
        StringAssert.Contains(report.Content, "Severity Bursts\n---------------");
        StringAssert.Contains(report.Content, "Activity Windows\n----------------");
        StringAssert.Contains(report.Content, "Diagnostics\n-----------");
        StringAssert.Contains(report.Content, "Limitations\n-----------");
        StringAssert.Contains(report.Content, "Generated locally by LogLens");
        Assert.AreEqual(".txt", report.SuggestedExtension);
    }

    [TestMethod]
    public void Generate_NormalJsonReportIsValidAndHasStableRequiredProperties()
    {
        ReportDocument report = _service.Generate(
            ReportTestData.Request([ReportTestData.Entry(1, "INFO ready")]),
            ReportFormat.Json);

        using JsonDocument json = JsonDocument.Parse(report.Content);
        JsonElement root = json.RootElement;
        Assert.AreEqual("1.0", root.GetProperty("reportVersion").GetString());
        Assert.AreEqual("LogLens", root.GetProperty("metadata").GetProperty("application").GetString());
        Assert.AreEqual("synthetic.log", root.GetProperty("source").GetProperty("fileName").GetString());
        Assert.IsTrue(root.TryGetProperty("integrity", out _));
        Assert.IsTrue(root.TryGetProperty("parsing", out _));
        Assert.IsTrue(root.TryGetProperty("severityCounts", out _));
        Assert.IsTrue(root.TryGetProperty("patterns", out _));
        Assert.IsTrue(root.TryGetProperty("diagnostics", out _));
        Assert.IsTrue(root.TryGetProperty("limitations", out _));
        Assert.AreEqual(".json", report.SuggestedExtension);
    }

    [TestMethod]
    public void Generate_EmptyLogReportsTruthfulZerosAndNoFindings()
    {
        ReportDocument report = _service.Generate(
            ReportTestData.Request(),
            ReportFormat.Json);

        Assert.AreEqual(0, report.Model.Parsing.TotalParsedEntries);
        Assert.AreEqual(0, report.Model.Patterns.TotalRepeatedMessagePatterns);
        Assert.HasCount(0, report.Model.Patterns.RepeatedMessages);
        Assert.HasCount(0, report.Model.Patterns.SeverityBursts);
        Assert.HasCount(0, report.Model.Patterns.ActivityWindows);
    }

    [TestMethod]
    public void Generate_NoPatternsTextReportSaysNoneDetected()
    {
        ParsedLogEntry[] entries =
        [
            ReportTestData.Entry(1, "INFO one"),
            ReportTestData.Entry(2, "WARN two", LogSeverity.Warning)
        ];

        ReportDocument report = _service.Generate(
            ReportTestData.Request(entries),
            ReportFormat.Text);

        StringAssert.Contains(report.Content, "Total detected: 0\nNone detected.");
    }

    [TestMethod]
    public void Generate_RepeatedMessageIncludesCountSeverityLinesAndEvidence()
    {
        ParsedLogEntry[] entries = Enumerable.Range(1, 3)
            .Select(line => ReportTestData.Entry(line, "ERROR exact failure", LogSeverity.Error))
            .ToArray();

        ReportRepeatedMessage finding = _service.Generate(
            ReportTestData.Request(entries),
            ReportFormat.Json).Model.Patterns.RepeatedMessages.Single();

        Assert.AreEqual("Error", finding.Severity);
        Assert.AreEqual(3, finding.OccurrenceCount);
        Assert.AreEqual(1, finding.FirstLineNumber);
        Assert.AreEqual(3, finding.LastLineNumber);
        Assert.AreEqual("ERROR exact failure", finding.MessagePreview);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, finding.Evidence.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Generate_SeverityBurstsIncludeNeutralTraceableEvidence()
    {
        DateTime start = new(2026, 8, 15, 10, 0, 0);
        ParsedLogEntry[] entries =
        [
            ReportTestData.TimedEntry(1, "ERROR A", LogSeverity.Error, start),
            ReportTestData.TimedEntry(2, "ERROR B", LogSeverity.Error, start.AddSeconds(20)),
            ReportTestData.TimedEntry(3, "CRITICAL C", LogSeverity.Critical, start.AddSeconds(60))
        ];

        ReportSeverityBurst burst = _service.Generate(
            ReportTestData.Request(entries),
            ReportFormat.Json).Model.Patterns.SeverityBursts.Single();

        Assert.AreEqual("Error/Critical activity burst", burst.Title);
        Assert.AreEqual("Error/Critical", burst.Severity);
        Assert.AreEqual(3, burst.OccurrenceCount);
        Assert.AreEqual("Wall clock", burst.TimeBasis);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, burst.Evidence.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Generate_ActivityWindowsIncludeMinuteAndHourFindings()
    {
        DateTime start = new(2026, 8, 15, 10, 0, 0);
        ParsedLogEntry[] entries =
        [
            ReportTestData.TimedEntry(1, "INFO A", LogSeverity.Information, start),
            ReportTestData.TimedEntry(2, "INFO B", LogSeverity.Information, start.AddSeconds(30)),
            ReportTestData.TimedEntry(3, "INFO C", LogSeverity.Information, start.AddMinutes(2))
        ];

        IReadOnlyList<ReportActivityWindow> windows = _service.Generate(
            ReportTestData.Request(entries),
            ReportFormat.Json).Model.Patterns.ActivityWindows;

        Assert.IsTrue(windows.Any(window =>
            window.Title == "Busiest minute"
            && window.OccurrenceCount == 2
            && window.Duration == "1 minute"));
        Assert.IsTrue(windows.Any(window =>
            window.Title == "Busiest hour"
            && window.OccurrenceCount == 3
            && window.Duration == "1 hour"));
    }

    [TestMethod]
    public void Generate_ParsingDiagnosticsAreIncludedAndCounted()
    {
        ParsingDiagnostic[] diagnostics =
        [
            new(ParsingDiagnosticKind.MalformedTimestamp, ParsingDiagnosticLevel.Warning, "Bad time", 7),
            new(ParsingDiagnosticKind.ControlCharacters, ParsingDiagnosticLevel.Warning, "Control text", 9)
        ];

        AnalysisReport report = _service.Generate(
            ReportTestData.Request(diagnostics: diagnostics),
            ReportFormat.Json).Model;

        Assert.AreEqual(2, report.Parsing.DiagnosticsCount);
        Assert.HasCount(2, report.Diagnostics);
        Assert.AreEqual(7, report.Diagnostics[0].LineNumber);
    }

    [TestMethod]
    public void Generate_PartialParsingIsDisclosed()
    {
        AnalysisReport report = _service.Generate(
            ReportTestData.Request(isComplete: false),
            ReportFormat.Json).Model;

        Assert.IsFalse(report.Parsing.IsComplete);
        Assert.IsTrue(report.Limitations.Any(limitation => limitation.Contains("100,000-entry safety cap", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generate_NoTimestampsIncludesTruthfulUnavailableLimitation()
    {
        AnalysisReport report = _service.Generate(
            ReportTestData.Request([ReportTestData.Entry(1, "INFO no date")]),
            ReportFormat.Json).Model;

        Assert.IsFalse(report.Patterns.TimeAnalysis.IsAvailable);
        Assert.IsTrue(report.Limitations.Any(limitation => limitation.Contains("enough recognised timestamps", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generate_UnicodeContentIsPreservedWithinPreview()
    {
        ParsedLogEntry[] entries =
        [
            ReportTestData.Entry(1, "ERROR 接続に失敗しました 🚦", LogSeverity.Error),
            ReportTestData.Entry(2, "ERROR 接続に失敗しました 🚦", LogSeverity.Error)
        ];

        ReportDocument text = _service.Generate(ReportTestData.Request(entries), ReportFormat.Text);
        ReportDocument json = _service.Generate(ReportTestData.Request(entries), ReportFormat.Json);

        StringAssert.Contains(text.Content, "接続に失敗しました 🚦");
        using JsonDocument parsedJson = JsonDocument.Parse(json.Content);
        Assert.AreEqual(
            "ERROR 接続に失敗しました 🚦",
            parsedJson.RootElement
                .GetProperty("patterns")
                .GetProperty("repeatedMessages")[0]
                .GetProperty("messagePreview")
                .GetString());
    }

    [TestMethod]
    public void Generate_CommandAndUrlLookingContentRemainInertText()
    {
        const string content = "ERROR powershell.exe -Command Invoke-WebRequest https://example.invalid/test";
        ParsedLogEntry[] entries =
        [
            ReportTestData.Entry(1, content, LogSeverity.Error),
            ReportTestData.Entry(2, content, LogSeverity.Error)
        ];

        ReportDocument report = _service.Generate(
            ReportTestData.Request(entries),
            ReportFormat.Text);

        StringAssert.Contains(report.Content, "powershell.exe -Command");
        StringAssert.Contains(report.Content, "https://example.invalid/test");
    }

    [TestMethod]
    public void Generate_FixedInputsProduceDeterministicTextAndJson()
    {
        ReportGenerationRequest request = ReportTestData.Request(
            [ReportTestData.Entry(1, "INFO stable")]);

        string firstText = _service.Generate(request, ReportFormat.Text).Content;
        string secondText = _service.Generate(request, ReportFormat.Text).Content;
        string firstJson = _service.Generate(request, ReportFormat.Json).Content;
        string secondJson = _service.Generate(request, ReportFormat.Json).Content;

        Assert.AreEqual(firstText, secondText);
        Assert.AreEqual(firstJson, secondJson);
    }

    [TestMethod]
    public void Generate_RepeatedFindingCountIsBounded()
    {
        var entries = new List<ParsedLogEntry>();
        int line = 1;
        for (int group = 0; group < ReportGenerationPolicy.MaximumRepeatedMessageFindings + 5; group++)
        {
            entries.Add(ReportTestData.Entry(line++, $"INFO repeated {group:D2}"));
            entries.Add(ReportTestData.Entry(line++, $"INFO repeated {group:D2}"));
        }

        AnalysisReport report = _service.Generate(
            ReportTestData.Request(entries),
            ReportFormat.Json).Model;

        Assert.AreEqual(ReportGenerationPolicy.MaximumRepeatedMessageFindings + 5, report.Patterns.TotalRepeatedMessagePatterns);
        Assert.HasCount(ReportGenerationPolicy.MaximumRepeatedMessageFindings, report.Patterns.RepeatedMessages);
        Assert.IsTrue(report.Limitations.Any(limitation => limitation.Contains("bounded report", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generate_BurstCountIsBounded()
    {
        var entries = new List<ParsedLogEntry>();
        DateTime start = new(2026, 8, 15, 10, 0, 0);
        int line = 1;
        for (int burst = 0; burst < ReportGenerationPolicy.MaximumSeverityBurstFindings + 3; burst++)
        {
            DateTime burstStart = start.AddMinutes(burst * 3);
            entries.Add(ReportTestData.TimedEntry(line++, $"ERROR {burst} A", LogSeverity.Error, burstStart));
            entries.Add(ReportTestData.TimedEntry(line++, $"ERROR {burst} B", LogSeverity.Error, burstStart.AddSeconds(10)));
            entries.Add(ReportTestData.TimedEntry(line++, $"CRITICAL {burst} C", LogSeverity.Critical, burstStart.AddSeconds(20)));
        }

        AnalysisReport report = _service.Generate(
            ReportTestData.Request(entries),
            ReportFormat.Json).Model;

        Assert.AreEqual(ReportGenerationPolicy.MaximumSeverityBurstFindings + 3, report.Patterns.TotalSeverityBursts);
        Assert.HasCount(ReportGenerationPolicy.MaximumSeverityBurstFindings, report.Patterns.SeverityBursts);
    }

    [TestMethod]
    public void Generate_EvidenceAndLongPreviewAreBoundedWithoutChangingInput()
    {
        string rawText = "ERROR " + new string('L', ReportGenerationPolicy.MessagePreviewCharacterLimit + 200);
        ParsedLogEntry[] entries = Enumerable.Range(1, ReportGenerationPolicy.MaximumEvidenceEntriesPerFinding + 5)
            .Select(line => ReportTestData.Entry(line, rawText, LogSeverity.Error))
            .ToArray();
        string[] before = entries.Select(entry => entry.RawText).ToArray();

        ReportRepeatedMessage finding = _service.Generate(
            ReportTestData.Request(entries),
            ReportFormat.Json).Model.Patterns.RepeatedMessages.Single();

        Assert.HasCount(ReportGenerationPolicy.MaximumEvidenceEntriesPerFinding, finding.Evidence);
        Assert.AreEqual(entries.Length, finding.TotalEvidenceEntries);
        Assert.IsLessThanOrEqualTo(finding.MessagePreview.Length, ReportGenerationPolicy.MessagePreviewCharacterLimit);
        Assert.EndsWith("…", finding.MessagePreview);
        CollectionAssert.AreEqual(before, entries.Select(entry => entry.RawText).ToArray());
    }

    [TestMethod]
    public void Generate_DiagnosticsAreBounded()
    {
        ParsingDiagnostic[] diagnostics = Enumerable.Range(
                1,
                ReportGenerationPolicy.MaximumDiagnosticEntries + 4)
            .Select(line => new ParsingDiagnostic(
                ParsingDiagnosticKind.MalformedTimestamp,
                ParsingDiagnosticLevel.Warning,
                $"Diagnostic {line}",
                line))
            .ToArray();

        AnalysisReport report = _service.Generate(
            ReportTestData.Request(diagnostics: diagnostics),
            ReportFormat.Json).Model;

        Assert.AreEqual(diagnostics.Length, report.Parsing.DiagnosticsCount);
        Assert.HasCount(ReportGenerationPolicy.MaximumDiagnosticEntries, report.Diagnostics);
        Assert.IsTrue(report.Limitations.Any(limitation => limitation.Contains("parsing diagnostics exist", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generate_DoesNotIncludeFullSourcePathOrMachineInformation()
    {
        ReportDocument report = _service.Generate(
            ReportTestData.Request(),
            ReportFormat.Json);

        Assert.IsFalse(report.Content.Contains("C:\\private-logs", StringComparison.Ordinal));
        Assert.IsFalse(report.Content.Contains(Environment.UserName, StringComparison.Ordinal));
        Assert.IsFalse(report.Content.Contains(Environment.MachineName, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_PreCancelledOperationDoesNotProduceReport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            _service.Generate(
                ReportTestData.Request(),
                ReportFormat.Text,
                cancellation.Token));
    }
}
