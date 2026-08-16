using System.Security.Cryptography;
using LogLens.Core.Analysis;
using LogLens.Core.Files;
using LogLens.Core.Parsing;
using LogLens.Core.Persistence;
using LogLens.Core.Querying;

namespace LogLens.Core.Tests.Persistence;

internal sealed class FixedLocalAppDataPathProvider(string root)
    : ILocalAppDataPathProvider
{
    public string GetLogLensDataRoot() => root;
}

internal static class PersistenceTestData
{
    public static readonly DateTimeOffset CreatedAtUtc = new(
        2026,
        8,
        15,
        9,
        0,
        0,
        TimeSpan.Zero);

    public static readonly DateTimeOffset UpdatedAtUtc = CreatedAtUtc.AddMinutes(5);

    public static SessionCaptureRequest Request(
        string sourcePath,
        SessionUiState? uiState = null,
        IReadOnlyList<ParsedLogEntry>? entries = null,
        IReadOnlyList<ParsingDiagnostic>? diagnostics = null,
        bool isComplete = true)
    {
        entries ??=
        [
            TimedEntry(
                1,
                "2026-08-15 09:00:00 INFO service ready ✓",
                LogSeverity.Information,
                new DateTime(2026, 8, 15, 9, 0, 0)),
            TimedEntry(
                2,
                "2026-08-15 09:00:10 ERROR retry failed",
                LogSeverity.Error,
                new DateTime(2026, 8, 15, 9, 0, 10)),
            TimedEntry(
                3,
                "2026-08-15 09:00:10 ERROR retry failed",
                LogSeverity.Error,
                new DateTime(2026, 8, 15, 9, 0, 10)),
            Entry(4, "powershell.exe -Command inert; https://example.invalid", LogSeverity.Unknown)
        ];
        diagnostics ??=
        [
            new ParsingDiagnostic(
                ParsingDiagnosticKind.MalformedTimestamp,
                ParsingDiagnosticLevel.Warning,
                "Synthetic malformed timestamp remained inert.",
                4)
        ];

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
        var before = new SourceFileSnapshot(512, CreatedAtUtc.UtcDateTime.AddMinutes(-1));
        var source = new SourceFileInspection(
            Path.GetFullPath(sourcePath),
            Path.GetFileName(sourcePath),
            before.Length,
            Convert.ToHexString(SHA256.HashData("synthetic source"u8.ToArray())),
            before,
            before,
            SourceChangedDuringRead: false);
        var analysis = new LogAnalysisResult(source, parsing);

        uiState ??= new SessionUiState(
            "Explorer",
            "retry",
            LogSeverityFilter.Error | LogSeverityFilter.Critical,
            TimestampPresenceFilter.HasTimestamp,
            SelectedEntryLineNumber: 3);
        return new SessionCaptureRequest(
            analysis,
            uiState,
            "0.1.0",
            CreatedAtUtc,
            UpdatedAtUtc);
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
        rawText[(rawText.IndexOf(' ', 20) + 1)..]);

    public static string StorageRoot(string parent) =>
        Path.Combine(parent, LocalSessionPolicy.ApplicationDataFolderName);
}
