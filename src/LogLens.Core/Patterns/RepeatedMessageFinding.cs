using LogLens.Core.Parsing;

namespace LogLens.Core.Patterns;

public sealed record RepeatedMessageFinding(
    string Title,
    string Explanation,
    string RawText,
    PatternSeverityGroup SeverityGroup,
    int OccurrenceCount,
    ParsedLogEntry FirstOccurrence,
    ParsedLogEntry LastOccurrence,
    PatternEvidence Evidence);
