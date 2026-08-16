using System.Globalization;
using System.Text;
using System.Text.Json;
using LogLens.Core.Parsing;
using LogLens.Core.Patterns;
using LogLens.Core.Querying;

namespace LogLens.Core.Reports;

public sealed class ReportGenerationService : IReportGenerationService
{
    private const string ReportVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ReportDocument Generate(
        ReportGenerationRequest request,
        ReportFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Analysis);
        ArgumentNullException.ThrowIfNull(request.Patterns);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApplicationVersion);
        cancellationToken.ThrowIfCancellationRequested();

        AnalysisReport model = BuildModel(request, cancellationToken);
        string content = format switch
        {
            ReportFormat.Text => RenderText(model, cancellationToken),
            ReportFormat.Json => JsonSerializer.Serialize(model, JsonOptions) + "\n",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported report format.")
        };

        return new ReportDocument(
            format,
            format == ReportFormat.Json ? ".json" : ".txt",
            content,
            model);
    }

    private static AnalysisReport BuildModel(
        ReportGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var analysis = request.Analysis;
        var summary = analysis.Parsing.Summary;
        PatternAnalysisResult patterns = request.Patterns;
        cancellationToken.ThrowIfCancellationRequested();

        ReportRepeatedMessage[] repeatedMessages = SelectRepeatedFindings(patterns)
            .Select(CreateRepeatedMessage)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        ReportSeverityBurst[] bursts = patterns.SeverityBursts
            .Take(ReportGenerationPolicy.MaximumSeverityBurstFindings)
            .Select(CreateSeverityBurst)
            .ToArray();
        ReportActivityWindow[] activityWindows = patterns.ActivityWindows
            .Select(CreateActivityWindow)
            .ToArray();
        ReportDiagnostic[] diagnostics = analysis.Parsing.Diagnostics
            .Take(ReportGenerationPolicy.MaximumDiagnosticEntries)
            .Select(CreateDiagnostic)
            .ToArray();

        var timeAnalysis = new ReportTimeAnalysis(
            patterns.TimeAnalysisStatus.IsAvailable,
            patterns.TimeAnalysisStatus.RecognizedTimestampEntries,
            patterns.TimeAnalysisStatus.ComparableTimestampEntries,
            patterns.TimeAnalysisStatus.ExcludedTimestampEntries,
            patterns.TimeAnalysisStatus.Basis is PatternTimeBasis basis
                ? FormatTimeBasis(basis)
                : null,
            patterns.TimeAnalysisStatus.Explanation);

        var reportPatterns = new ReportPatternSummary(
            patterns.TotalRepeatedMessagePatterns,
            repeatedMessages,
            patterns.TotalSeverityBurstCount,
            bursts,
            activityWindows,
            timeAnalysis);

        return new AnalysisReport(
            ReportVersion,
            new ReportMetadata(
                "LogLens",
                request.ApplicationVersion,
                request.GeneratedAtUtc.ToUniversalTime(),
                "Generated locally by LogLens. No report data was uploaded or transmitted."),
            new ReportSource(
                analysis.Source.FileName,
                analysis.Source.Length,
                analysis.Parsing.DetectedEncoding,
                analysis.Source.Sha256),
            new ReportIntegrity(
                OpenedReadOnly: true,
                analysis.Source.SourceChangedDuringRead,
                analysis.Source.SourceChangedDuringRead
                    ? "The source length or modified time changed during analysis; results may be incomplete."
                    : "No source length or modified-time change was detected during analysis."),
            new ReportParsingSummary(
                summary.TotalEntries,
                summary.ClassifiedEntries,
                summary.UnclassifiedEntries,
                summary.TimestampedEntries,
                analysis.Parsing.Diagnostics.Count,
                summary.IsComplete),
            new ReportSeverityCounts(
                summary.TraceCount,
                summary.DebugCount,
                summary.InformationCount,
                summary.WarningCount,
                summary.ErrorCount,
                summary.CriticalCount,
                summary.UnclassifiedEntries),
            reportPatterns,
            diagnostics,
            BuildLimitations(analysis.Parsing.Diagnostics.Count, reportPatterns, summary.IsComplete));
    }

    private static IReadOnlyList<RepeatedMessageFinding> SelectRepeatedFindings(
        PatternAnalysisResult patterns)
    {
        var selected = new List<RepeatedMessageFinding>();

        foreach (RepeatedMessageFinding leader in patterns.RepeatedSeverityLeaders.Where(finding =>
                     finding.SeverityGroup is PatternSeverityGroup.Warning
                         or PatternSeverityGroup.Error
                         or PatternSeverityGroup.Critical))
        {
            AddByReference(selected, leader);
        }

        foreach (RepeatedMessageFinding finding in patterns.TopRepeatedMessages)
        {
            if (selected.Count >= ReportGenerationPolicy.MaximumRepeatedMessageFindings)
            {
                break;
            }

            AddByReference(selected, finding);
        }

        return selected
            .OrderByDescending(finding => finding.OccurrenceCount)
            .ThenBy(finding => finding.FirstOccurrence.LineNumber)
            .ThenBy(finding => finding.RawText, StringComparer.Ordinal)
            .Take(ReportGenerationPolicy.MaximumRepeatedMessageFindings)
            .ToArray();
    }

    private static void AddByReference(
        ICollection<RepeatedMessageFinding> findings,
        RepeatedMessageFinding finding)
    {
        if (!findings.Any(candidate => ReferenceEquals(candidate, finding)))
        {
            findings.Add(finding);
        }
    }

    private static ReportRepeatedMessage CreateRepeatedMessage(RepeatedMessageFinding finding) => new(
        finding.Title,
        FormatSeverityGroup(finding.SeverityGroup),
        finding.OccurrenceCount,
        finding.FirstOccurrence.LineNumber,
        finding.LastOccurrence.LineNumber,
        CreatePreview(finding.RawText),
        finding.Evidence.TotalEntryCount,
        CreateEvidence(finding.Evidence));

    private static ReportSeverityBurst CreateSeverityBurst(SeverityBurstFinding finding) => new(
        finding.Title,
        FormatSeverityGroup(finding.SeverityGroup),
        finding.OccurrenceCount,
        FormatPatternTime(finding.TimeRange.Start, finding.TimeRange.Basis),
        FormatPatternTime(finding.TimeRange.End, finding.TimeRange.Basis),
        FormatTimeBasis(finding.TimeRange.Basis),
        finding.Evidence.TotalEntryCount,
        CreateEvidence(finding.Evidence));

    private static ReportActivityWindow CreateActivityWindow(ActivityWindowFinding finding) => new(
        finding.Title,
        finding.OccurrenceCount,
        FormatPatternTime(finding.Window.Start, finding.Window.Basis),
        FormatDuration(finding.Window.Duration),
        FormatTimeBasis(finding.Window.Basis),
        finding.Evidence.TotalEntryCount,
        CreateEvidence(finding.Evidence));

    private static IReadOnlyList<ReportEvidenceEntry> CreateEvidence(PatternEvidence evidence) =>
        evidence.Entries
            .Take(ReportGenerationPolicy.MaximumEvidenceEntriesPerFinding)
            .Select(entry => new ReportEvidenceEntry(
                entry.LineNumber,
                entry.Timestamp?.RawText,
                FormatSeverity(entry.Severity),
                CreatePreview(string.IsNullOrEmpty(entry.Message) ? entry.RawText : entry.Message)))
            .ToArray();

    private static ReportDiagnostic CreateDiagnostic(ParsingDiagnostic diagnostic) => new(
        diagnostic.Kind.ToString(),
        diagnostic.Level.ToString(),
        diagnostic.LineNumber,
        CreatePreview(diagnostic.Message));

    private static IReadOnlyList<string> BuildLimitations(
        int totalDiagnosticCount,
        ReportPatternSummary patterns,
        bool parsingIsComplete)
    {
        var limitations = new List<string>
        {
            $"The report exports at most {ReportGenerationPolicy.MaximumRepeatedMessageFindings} repeated-message findings, {ReportGenerationPolicy.MaximumSeverityBurstFindings} severity bursts and {ReportGenerationPolicy.MaximumEvidenceEntriesPerFinding} evidence entries per finding.",
            $"Message and diagnostic previews are limited to {ReportGenerationPolicy.MessagePreviewCharacterLimit} characters. The complete source log is not included.",
            "Repeated-message matching is exact and case-sensitive; timestamps, identifiers, numbers, paths and whitespace are not removed.",
            "Severity bursts and activity windows use fixed deterministic thresholds and are not threat, malware or incident detections.",
            "Line-ending characters are not retained per normalised entry.",
            "UI display limits do not imply loss of complete raw entry text held by Core for the loaded analysis."
        };

        if (!parsingIsComplete)
        {
            limitations.Add(
                "Parsing was partial. Counts and findings cannot describe source entries beyond the 100,000-entry safety cap.");
        }

        if (!patterns.TimeAnalysis.IsAvailable)
        {
            limitations.Add(patterns.TimeAnalysis.Explanation);
        }

        if (patterns.TotalRepeatedMessagePatterns > patterns.RepeatedMessages.Count)
        {
            limitations.Add(
                $"{patterns.TotalRepeatedMessagePatterns:N0} repeated-message patterns were detected; {patterns.RepeatedMessages.Count:N0} are included in this bounded report.");
        }

        if (patterns.TotalSeverityBursts > patterns.SeverityBursts.Count)
        {
            limitations.Add(
                $"{patterns.TotalSeverityBursts:N0} severity bursts were detected; {patterns.SeverityBursts.Count:N0} are included in this bounded report.");
        }

        if (totalDiagnosticCount > ReportGenerationPolicy.MaximumDiagnosticEntries)
        {
            limitations.Add(
                $"{totalDiagnosticCount:N0} parsing diagnostics exist; the first {ReportGenerationPolicy.MaximumDiagnosticEntries:N0} are included.");
        }

        return limitations.ToArray();
    }

    private static string RenderText(
        AnalysisReport report,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder(8 * 1024);
        void Heading(string value)
        {
            text.AppendLine(value);
            text.AppendLine(new string('-', value.Length));
        }

        text.AppendLine("LogLens Analysis Summary");
        text.AppendLine("========================");
        text.AppendLine();
        text.AppendLine(report.Metadata.ProcessingStatement);
        text.AppendLine($"Report version: {report.ReportVersion}");
        text.AppendLine($"Application version: {report.Metadata.ApplicationVersion}");
        text.AppendLine($"Generated at (UTC): {report.Metadata.GeneratedAtUtc:O}");
        text.AppendLine();

        Heading("Source");
        text.AppendLine($"Filename: {ToSingleLine(report.Source.FileName)}");
        text.AppendLine($"Size: {report.Source.SizeBytes.ToString(CultureInfo.InvariantCulture)} bytes");
        text.AppendLine($"Detected encoding: {ToSingleLine(report.Source.DetectedEncoding)}");
        text.AppendLine($"SHA-256: {report.Source.Sha256}");
        text.AppendLine();

        Heading("Integrity");
        text.AppendLine($"Opened read-only: {YesNo(report.Integrity.OpenedReadOnly)}");
        text.AppendLine($"Source changed during analysis: {YesNo(report.Integrity.SourceChangedDuringAnalysis)}");
        text.AppendLine($"Status: {ToSingleLine(report.Integrity.Status)}");
        text.AppendLine();

        Heading("Parsing Summary");
        text.AppendLine($"Total parsed entries: {Number(report.Parsing.TotalParsedEntries)}");
        text.AppendLine($"Classified entries: {Number(report.Parsing.ClassifiedEntries)}");
        text.AppendLine($"Unclassified entries: {Number(report.Parsing.UnclassifiedEntries)}");
        text.AppendLine($"Timestamped entries: {Number(report.Parsing.TimestampedEntries)}");
        text.AppendLine($"Parsing diagnostics: {Number(report.Parsing.DiagnosticsCount)}");
        text.AppendLine($"Parsing complete: {YesNo(report.Parsing.IsComplete)}");
        text.AppendLine();

        Heading("Severity Summary");
        text.AppendLine($"Trace: {Number(report.SeverityCounts.Trace)}");
        text.AppendLine($"Debug: {Number(report.SeverityCounts.Debug)}");
        text.AppendLine($"Information: {Number(report.SeverityCounts.Information)}");
        text.AppendLine($"Warning: {Number(report.SeverityCounts.Warning)}");
        text.AppendLine($"Error: {Number(report.SeverityCounts.Error)}");
        text.AppendLine($"Critical/Fatal: {Number(report.SeverityCounts.CriticalFatal)}");
        text.AppendLine($"Unknown/Unclassified: {Number(report.SeverityCounts.Unknown)}");
        text.AppendLine();

        cancellationToken.ThrowIfCancellationRequested();
        Heading("Repeated Messages");
        text.AppendLine($"Total detected: {Number(report.Patterns.TotalRepeatedMessagePatterns)}");
        AppendRepeatedMessages(text, report.Patterns.RepeatedMessages);
        text.AppendLine();

        Heading("Severity Bursts");
        text.AppendLine($"Total detected: {Number(report.Patterns.TotalSeverityBursts)}");
        AppendBursts(text, report.Patterns.SeverityBursts);
        text.AppendLine();

        Heading("Activity Windows");
        text.AppendLine($"Time analysis: {ToSingleLine(report.Patterns.TimeAnalysis.Explanation)}");
        AppendActivityWindows(text, report.Patterns.ActivityWindows);
        text.AppendLine();

        Heading("Diagnostics");
        text.AppendLine($"Total detected: {Number(report.Parsing.DiagnosticsCount)}");
        if (report.Diagnostics.Count == 0)
        {
            text.AppendLine("None.");
        }
        else
        {
            foreach (ReportDiagnostic diagnostic in report.Diagnostics)
            {
                string line = diagnostic.LineNumber is int number ? $"line {Number(number)}" : "file level";
                text.AppendLine($"- [{diagnostic.Level}] {diagnostic.Kind} ({line}): {ToSingleLine(diagnostic.Message)}");
            }
        }

        text.AppendLine();
        Heading("Limitations");
        foreach (string limitation in report.Limitations)
        {
            text.AppendLine($"- {ToSingleLine(limitation)}");
        }

        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void AppendRepeatedMessages(
        StringBuilder text,
        IReadOnlyList<ReportRepeatedMessage> findings)
    {
        if (findings.Count == 0)
        {
            text.AppendLine("None detected.");
            return;
        }

        foreach (ReportRepeatedMessage finding in findings)
        {
            text.AppendLine($"- [{finding.Severity}] {Number(finding.OccurrenceCount)} occurrences, lines {Number(finding.FirstLineNumber)}-{Number(finding.LastLineNumber)}: {ToSingleLine(finding.MessagePreview)}");
            AppendEvidence(text, finding.Evidence, finding.TotalEvidenceEntries);
        }
    }

    private static void AppendBursts(
        StringBuilder text,
        IReadOnlyList<ReportSeverityBurst> findings)
    {
        if (findings.Count == 0)
        {
            text.AppendLine("None detected.");
            return;
        }

        foreach (ReportSeverityBurst finding in findings)
        {
            text.AppendLine($"- {finding.Title}: {Number(finding.OccurrenceCount)} {finding.Severity} entries from {finding.Start} to {finding.End} ({finding.TimeBasis}).");
            AppendEvidence(text, finding.Evidence, finding.TotalEvidenceEntries);
        }
    }

    private static void AppendActivityWindows(
        StringBuilder text,
        IReadOnlyList<ReportActivityWindow> findings)
    {
        if (findings.Count == 0)
        {
            text.AppendLine("Unavailable or no supported windows detected.");
            return;
        }

        foreach (ReportActivityWindow finding in findings)
        {
            text.AppendLine($"- {finding.Title}: {Number(finding.OccurrenceCount)} entries from {finding.Start} for {finding.Duration} ({finding.TimeBasis}).");
            AppendEvidence(text, finding.Evidence, finding.TotalEvidenceEntries);
        }
    }

    private static void AppendEvidence(
        StringBuilder text,
        IReadOnlyList<ReportEvidenceEntry> evidence,
        int totalEvidenceEntries)
    {
        foreach (ReportEvidenceEntry entry in evidence)
        {
            string timestamp = entry.Timestamp is null ? "timestamp unavailable" : ToSingleLine(entry.Timestamp);
            text.AppendLine($"  Evidence line {Number(entry.LineNumber)} | {timestamp} | {entry.Severity} | {ToSingleLine(entry.MessagePreview)}");
        }

        if (evidence.Count < totalEvidenceEntries)
        {
            text.AppendLine($"  Evidence limited to {Number(evidence.Count)} of {Number(totalEvidenceEntries)} matching entries.");
        }
    }

    private static string CreatePreview(string value) =>
        LogEntryTextProjection.CreateBounded(
            value,
            ReportGenerationPolicy.MessagePreviewCharacterLimit).Text;

    private static string FormatPatternTime(DateTime value, PatternTimeBasis basis)
    {
        string formatted = value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        return basis == PatternTimeBasis.Utc ? formatted + "Z" : formatted;
    }

    private static string FormatTimeBasis(PatternTimeBasis basis) =>
        basis == PatternTimeBasis.Utc ? "UTC" : "Wall clock";

    private static string FormatSeverityGroup(PatternSeverityGroup severity) => severity switch
    {
        PatternSeverityGroup.Information => "Information",
        PatternSeverityGroup.Critical => "Critical/Fatal",
        PatternSeverityGroup.ErrorCritical => "Error/Critical",
        PatternSeverityGroup.Unknown => "Unknown/Unclassified",
        PatternSeverityGroup.Mixed => "Mixed severity",
        _ => severity.ToString()
    };

    private static string FormatSeverity(LogSeverity severity) => severity switch
    {
        LogSeverity.Critical => "Critical/Fatal",
        LogSeverity.Unknown => "Unknown/Unclassified",
        _ => severity.ToString()
    };

    private static string FormatDuration(TimeSpan value)
    {
        if (value == TimeSpan.FromMinutes(1))
        {
            return "1 minute";
        }

        if (value == TimeSpan.FromHours(1))
        {
            return "1 hour";
        }

        return value.ToString("c", CultureInfo.InvariantCulture);
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string ToSingleLine(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            switch (character)
            {
                case '\r':
                    result.Append("\\r");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        result.Append("\\u");
                        result.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        result.Append(character);
                    }

                    break;
            }
        }

        return result.ToString();
    }
}
