using System.Text.RegularExpressions;

namespace LogLens.Core.Parsing;

internal sealed record DetectedLogSeverity(
    LogSeverity Severity,
    int MatchIndex,
    int MatchLength)
{
    public static DetectedLogSeverity Unknown { get; } = new(LogSeverity.Unknown, -1, 0);
}

public sealed partial class LogSeverityDetector
{
    public LogSeverity Detect(string text) => DetectDetailed(text).Severity;

    internal DetectedLogSeverity DetectDetailed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Match match = SeverityRegex().Match(text);
        if (!match.Success)
        {
            return DetectedLogSeverity.Unknown;
        }

        string level = match.Groups["level"].Value;
        LogSeverity severity = level.ToUpperInvariant() switch
        {
            "TRACE" => LogSeverity.Trace,
            "DEBUG" => LogSeverity.Debug,
            "INFO" or "INFORMATION" => LogSeverity.Information,
            "WARN" or "WARNING" => LogSeverity.Warning,
            "ERROR" or "ERR" => LogSeverity.Error,
            "CRITICAL" or "FATAL" => LogSeverity.Critical,
            _ => LogSeverity.Unknown
        };

        return new DetectedLogSeverity(severity, match.Index, match.Length);
    }

    [GeneratedRegex(
        @"\b(?<level>INFORMATION|INFO|WARNING|WARN|CRITICAL|FATAL|ERROR|ERR|DEBUG|TRACE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SeverityRegex();
}
