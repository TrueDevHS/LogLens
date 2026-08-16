using System.Globalization;
using System.Text;

namespace LogLens.Core.Parsing;

public sealed class PlainTextLogParser : ILogParser
{
    private readonly LogSeverityDetector _severityDetector;
    private readonly LogTimestampDetector _timestampDetector;

    public PlainTextLogParser()
        : this(new LogSeverityDetector(), new LogTimestampDetector())
    {
    }

    public PlainTextLogParser(
        LogSeverityDetector severityDetector,
        LogTimestampDetector timestampDetector)
    {
        _severityDetector = severityDetector ?? throw new ArgumentNullException(nameof(severityDetector));
        _timestampDetector = timestampDetector ?? throw new ArgumentNullException(nameof(timestampDetector));
    }

    public async Task<LogParsingResult> ParseAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanRead || source.CanWrite)
        {
            throw new LogParsingException(
                LogParsingErrorKind.UnsafeInputStream,
                "LogLens requires a strictly read-only input stream. The file was not parsed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        DetectedTextEncoding detectedEncoding = await StrictTextEncodingDetector.DetectAsync(
            source,
            cancellationToken).ConfigureAwait(false);

        var entries = new List<ParsedLogEntry>();
        var diagnostics = new List<ParsingDiagnostic>();
        int suppressedDiagnostics = 0;
        bool isComplete = true;

        try
        {
            using var reader = new StreamReader(
                source,
                detectedEncoding.Encoding,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: LogParsingPolicy.ReaderBufferSizeCharacters,
                leaveOpen: true);

            while (entries.Count < LogParsingPolicy.MaximumEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? rawLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (rawLine is null)
                {
                    break;
                }

                int lineNumber = entries.Count + 1;
                TimestampDetection timestamp = _timestampDetector.DetectDetailed(rawLine);
                DetectedLogSeverity severity = _severityDetector.DetectDetailed(rawLine);
                string message = ExtractMessage(rawLine, timestamp, severity);

                entries.Add(new ParsedLogEntry(
                    lineNumber,
                    rawLine,
                    severity.Severity,
                    timestamp.Timestamp,
                    message));

                if (timestamp.HasTimestampLikePrefix && timestamp.Timestamp is null)
                {
                    AddDiagnostic(
                        diagnostics,
                        new ParsingDiagnostic(
                            ParsingDiagnosticKind.MalformedTimestamp,
                            ParsingDiagnosticLevel.Warning,
                            "A timestamp-like prefix could not be parsed. The original line was preserved.",
                            lineNumber),
                        ref suppressedDiagnostics);
                }

                if (rawLine.Length > LogParsingPolicy.LongLineWarningThresholdCharacters)
                {
                    AddDiagnostic(
                        diagnostics,
                        new ParsingDiagnostic(
                            ParsingDiagnosticKind.UnusuallyLongLine,
                            ParsingDiagnosticLevel.Warning,
                            $"Line {lineNumber} is unusually long ({rawLine.Length:N0} characters). It was preserved as inert text.",
                            lineNumber),
                        ref suppressedDiagnostics);
                }

                if (ContainsControlLikeCharacters(rawLine))
                {
                    AddDiagnostic(
                        diagnostics,
                        new ParsingDiagnostic(
                            ParsingDiagnosticKind.ControlCharacters,
                            ParsingDiagnosticLevel.Warning,
                            "The line contains unusual control or formatting characters. It was preserved as inert text.",
                            lineNumber),
                        ref suppressedDiagnostics);
                }
            }

            if (entries.Count == LogParsingPolicy.MaximumEntries)
            {
                char[] probe = new char[1];
                int additionalCharacters = await reader.ReadBlockAsync(
                    probe.AsMemory(),
                    cancellationToken).ConfigureAwait(false);

                if (additionalCharacters > 0)
                {
                    isComplete = false;
                    AddDiagnostic(
                        diagnostics,
                        new ParsingDiagnostic(
                            ParsingDiagnosticKind.EntryLimitReached,
                            ParsingDiagnosticLevel.Warning,
                            "The 100,000-entry safety limit was reached. The result is explicitly marked incomplete.",
                            LogParsingPolicy.MaximumEntries),
                        ref suppressedDiagnostics);
                }
            }
        }
        catch (DecoderFallbackException exception)
        {
            throw new LogParsingException(
                LogParsingErrorKind.InvalidEncoding,
                "LogLens detected invalid text encoding. Parsing stopped safely and the original file was not modified.",
                exception);
        }

        if (entries.Count == 0)
        {
            AddDiagnostic(
                diagnostics,
                new ParsingDiagnostic(
                    ParsingDiagnosticKind.EmptyFile,
                    ParsingDiagnosticLevel.Information,
                    "The selected file is empty."),
                ref suppressedDiagnostics);
        }

        if (suppressedDiagnostics > 0)
        {
            diagnostics.Add(new ParsingDiagnostic(
                ParsingDiagnosticKind.DiagnosticLimitReached,
                ParsingDiagnosticLevel.Warning,
                $"{suppressedDiagnostics:N0} additional parsing warnings were suppressed to keep the result bounded."));
        }

        ParsedLogEntry[] normalizedEntries = entries.ToArray();
        var summary = BuildSummary(normalizedEntries, isComplete);
        return new LogParsingResult(
            normalizedEntries,
            summary,
            diagnostics.ToArray(),
            detectedEncoding.DisplayName);
    }

    private static LogParsingSummary BuildSummary(
        IReadOnlyCollection<ParsedLogEntry> entries,
        bool isComplete)
    {
        int trace = 0;
        int debug = 0;
        int information = 0;
        int warning = 0;
        int error = 0;
        int critical = 0;
        int unknown = 0;
        int timestamped = 0;

        foreach (ParsedLogEntry entry in entries)
        {
            switch (entry.Severity)
            {
                case LogSeverity.Trace:
                    trace++;
                    break;
                case LogSeverity.Debug:
                    debug++;
                    break;
                case LogSeverity.Information:
                    information++;
                    break;
                case LogSeverity.Warning:
                    warning++;
                    break;
                case LogSeverity.Error:
                    error++;
                    break;
                case LogSeverity.Critical:
                    critical++;
                    break;
                default:
                    unknown++;
                    break;
            }

            if (entry.Timestamp is not null)
            {
                timestamped++;
            }
        }

        return new LogParsingSummary(
            entries.Count,
            entries.Count - unknown,
            unknown,
            trace,
            debug,
            information,
            warning,
            error,
            critical,
            timestamped,
            isComplete);
    }

    private static string ExtractMessage(
        string rawLine,
        TimestampDetection timestamp,
        DetectedLogSeverity severity)
    {
        int messageStart = timestamp.Timestamp is null ? 0 : timestamp.PrefixLength;
        messageStart = SkipDecorations(rawLine, messageStart);

        if (severity.Severity != LogSeverity.Unknown
            && severity.MatchIndex >= messageStart
            && severity.MatchIndex - messageStart <= 24
            && ContainsOnlyDecorations(rawLine, messageStart, severity.MatchIndex))
        {
            messageStart = SkipDecorations(
                rawLine,
                severity.MatchIndex + severity.MatchLength);
        }

        return rawLine[messageStart..].Trim();
    }

    private static int SkipDecorations(string text, int start)
    {
        int index = Math.Clamp(start, 0, text.Length);
        while (index < text.Length && IsDecoration(text[index]))
        {
            index++;
        }

        return index;
    }

    private static bool ContainsOnlyDecorations(string text, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            if (!IsDecoration(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDecoration(char character) =>
        char.IsWhiteSpace(character)
        || character is '[' or ']' or '(' or ')' or '{' or '}' or '|' or ':' or '-';

    private static bool ContainsControlLikeCharacters(string text)
    {
        foreach (char character in text)
        {
            if ((char.IsControl(character) && character != '\t')
                || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddDiagnostic(
        ICollection<ParsingDiagnostic> diagnostics,
        ParsingDiagnostic diagnostic,
        ref int suppressedDiagnostics)
    {
        if (diagnostics.Count < LogParsingPolicy.MaximumDiagnostics - 1)
        {
            diagnostics.Add(diagnostic);
        }
        else
        {
            suppressedDiagnostics++;
        }
    }
}
