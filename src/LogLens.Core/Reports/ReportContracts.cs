using LogLens.Core.Analysis;
using LogLens.Core.Patterns;

namespace LogLens.Core.Reports;

public enum ReportFormat
{
    Text,
    Json
}

public sealed record ReportGenerationRequest(
    LogAnalysisResult Analysis,
    PatternAnalysisResult Patterns,
    string ApplicationVersion,
    DateTimeOffset GeneratedAtUtc);

public sealed record ReportDocument(
    ReportFormat Format,
    string SuggestedExtension,
    string Content,
    AnalysisReport Model);

public sealed record AnalysisReport(
    string ReportVersion,
    ReportMetadata Metadata,
    ReportSource Source,
    ReportIntegrity Integrity,
    ReportParsingSummary Parsing,
    ReportSeverityCounts SeverityCounts,
    ReportPatternSummary Patterns,
    IReadOnlyList<ReportDiagnostic> Diagnostics,
    IReadOnlyList<string> Limitations);

public sealed record ReportMetadata(
    string Application,
    string ApplicationVersion,
    DateTimeOffset GeneratedAtUtc,
    string ProcessingStatement);

public sealed record ReportSource(
    string FileName,
    long SizeBytes,
    string DetectedEncoding,
    string Sha256);

public sealed record ReportIntegrity(
    bool OpenedReadOnly,
    bool SourceChangedDuringAnalysis,
    string Status);

public sealed record ReportParsingSummary(
    int TotalParsedEntries,
    int ClassifiedEntries,
    int UnclassifiedEntries,
    int TimestampedEntries,
    int DiagnosticsCount,
    bool IsComplete);

public sealed record ReportSeverityCounts(
    int Trace,
    int Debug,
    int Information,
    int Warning,
    int Error,
    int CriticalFatal,
    int Unknown);

public sealed record ReportPatternSummary(
    int TotalRepeatedMessagePatterns,
    IReadOnlyList<ReportRepeatedMessage> RepeatedMessages,
    int TotalSeverityBursts,
    IReadOnlyList<ReportSeverityBurst> SeverityBursts,
    IReadOnlyList<ReportActivityWindow> ActivityWindows,
    ReportTimeAnalysis TimeAnalysis);

public sealed record ReportRepeatedMessage(
    string Title,
    string Severity,
    int OccurrenceCount,
    int FirstLineNumber,
    int LastLineNumber,
    string MessagePreview,
    int TotalEvidenceEntries,
    IReadOnlyList<ReportEvidenceEntry> Evidence);

public sealed record ReportSeverityBurst(
    string Title,
    string Severity,
    int OccurrenceCount,
    string Start,
    string End,
    string TimeBasis,
    int TotalEvidenceEntries,
    IReadOnlyList<ReportEvidenceEntry> Evidence);

public sealed record ReportActivityWindow(
    string Title,
    int OccurrenceCount,
    string Start,
    string Duration,
    string TimeBasis,
    int TotalEvidenceEntries,
    IReadOnlyList<ReportEvidenceEntry> Evidence);

public sealed record ReportTimeAnalysis(
    bool IsAvailable,
    int RecognizedTimestampEntries,
    int ComparableTimestampEntries,
    int ExcludedTimestampEntries,
    string? TimeBasis,
    string Explanation);

public sealed record ReportEvidenceEntry(
    int LineNumber,
    string? Timestamp,
    string Severity,
    string MessagePreview);

public sealed record ReportDiagnostic(
    string Kind,
    string Level,
    int? LineNumber,
    string Message);

public sealed record ReportWriteRequest(
    ReportDocument Report,
    string SourcePath,
    string DestinationPath,
    bool OverwriteConfirmed);

public sealed record ReportWriteResult(
    string DestinationFileName,
    long BytesWritten);
