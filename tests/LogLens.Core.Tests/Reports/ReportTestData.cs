using LogLens.Core.Analysis;
using LogLens.Core.Files;
using LogLens.Core.Parsing;
using LogLens.Core.Patterns;
using LogLens.Core.Reports;

namespace LogLens.Core.Tests.Reports;

internal static class ReportTestData
{
    public static readonly DateTimeOffset GeneratedAt = new(
        2026,
        8,
        15,
        10,
        30,
        0,
        TimeSpan.Zero);

    public static ReportGenerationRequest Request(
        IReadOnlyList<ParsedLogEntry>? entries = null,
        IReadOnlyList<ParsingDiagnostic>? diagnostics = null,
        bool isComplete = true,
        bool sourceChangedDuringRead = false,
        string fileName = "synthetic.log")
    {
        entries ??= [];
        diagnostics ??= [];
        int classified = entries.Count(entry => entry.Severity != LogSeverity.Unknown);
        var summary = new LogParsingSummary(
            entries.Count,
            classified,
            entries.Count - classified,
            entries.Count(entry => entry.Severity == LogSeverity.Trace),
            entries.Count(entry => entry.Severity == LogSeverity.Debug),
            entries.Count(entry => entry.Severity == LogSeverity.Information),
            entries.Count(entry => entry.Severity == LogSeverity.Warning),
            entries.Count(entry => entry.Severity == LogSeverity.Error),
            entries.Count(entry => entry.Severity == LogSeverity.Critical),
            entries.Count(entry => entry.Timestamp is not null),
            isComplete);
        var parsing = new LogParsingResult(entries, summary, diagnostics, "UTF-8");
        var before = new SourceFileSnapshot(4_096, GeneratedAt.UtcDateTime.AddMinutes(-2));
        SourceFileSnapshot after = sourceChangedDuringRead
            ? before with { LastWriteTimeUtc = before.LastWriteTimeUtc.AddSeconds(1) }
            : before;
        var source = new SourceFileInspection(
            Path.Combine("C:\\private-logs", fileName),
            fileName,
            before.Length,
            "ABCDEF0123456789",
            before,
            after,
            sourceChangedDuringRead);
        var analysis = new LogAnalysisResult(source, parsing);
        PatternAnalysisResult patterns = new PatternAnalysisService().Analyze(entries);
        return new ReportGenerationRequest(analysis, patterns, "0.1.0", GeneratedAt);
    }

    public static ParsedLogEntry Entry(
        int lineNumber,
        string rawText,
        LogSeverity severity = LogSeverity.Information) => new(
        lineNumber,
        rawText,
        severity,
        null,
        rawText);

    public static ParsedLogEntry TimedEntry(
        int lineNumber,
        string rawText,
        LogSeverity severity,
        DateTime timestamp) => new(
        lineNumber,
        rawText,
        severity,
        new ParsedLogTimestamp(
            timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            DateOnly.FromDateTime(timestamp),
            TimeOnly.FromDateTime(timestamp),
            null),
        rawText);
}
