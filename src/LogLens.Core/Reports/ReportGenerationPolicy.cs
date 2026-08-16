using LogLens.Core.Querying;

namespace LogLens.Core.Reports;

public static class ReportGenerationPolicy
{
    public const int MaximumRepeatedMessageFindings = 15;
    public const int MaximumSeverityBurstFindings = 10;
    public const int MaximumEvidenceEntriesPerFinding = 10;
    public const int MaximumDiagnosticEntries = 20;
    public const int MessagePreviewCharacterLimit = LogEntryTextProjection.PreviewCharacterLimit;
}
