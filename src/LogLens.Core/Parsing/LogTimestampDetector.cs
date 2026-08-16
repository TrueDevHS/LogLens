using System.Globalization;
using System.Text.RegularExpressions;

namespace LogLens.Core.Parsing;

internal sealed record TimestampDetection(
    ParsedLogTimestamp? Timestamp,
    int PrefixLength,
    bool HasTimestampLikePrefix)
{
    public static TimestampDetection None { get; } = new(null, 0, false);
}

public sealed partial class LogTimestampDetector
{
    private static readonly string[] FullDateTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm:ss.FFFFFFF"
    ];

    private static readonly string[] OffsetDateTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    private static readonly string[] TimeFormats =
    [
        "HH:mm:ss",
        "HH:mm:ss.FFFFFFF"
    ];

    public ParsedLogTimestamp? Detect(string text) => DetectDetailed(text).Timestamp;

    internal TimestampDetection DetectDetailed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        TimestampDetection bracketed = DetectBracketed(text);
        if (bracketed.HasTimestampLikePrefix)
        {
            return bracketed;
        }

        foreach (Regex regex in TimestampRegexes)
        {
            Match match = regex.Match(text);
            if (!match.Success)
            {
                continue;
            }

            Group candidateGroup = match.Groups["timestamp"];
            ParsedLogTimestamp? timestamp = ParseCandidate(candidateGroup.Value);
            return new TimestampDetection(timestamp, match.Length, true);
        }

        return TimestampDetection.None;
    }

    private static IReadOnlyList<Regex> TimestampRegexes { get; } =
    [
        IsoTimestampRegex(),
        YearFirstTimestampRegex(),
        DayFirstTimestampRegex(),
        TimeOnlyTimestampRegex()
    ];

    private static TimestampDetection DetectBracketed(string text)
    {
        int firstNonWhitespace = 0;
        while (firstNonWhitespace < text.Length && char.IsWhiteSpace(text[firstNonWhitespace]))
        {
            firstNonWhitespace++;
        }

        if (firstNonWhitespace >= text.Length || text[firstNonWhitespace] != '[')
        {
            return TimestampDetection.None;
        }

        int closingBracket = text.IndexOf(']', firstNonWhitespace + 1);
        if (closingBracket < 0 || closingBracket - firstNonWhitespace > 80)
        {
            return TimestampDetection.None;
        }

        string candidate = text[(firstNonWhitespace + 1)..closingBracket].Trim();
        if (!TimestampShapeRegex().IsMatch(candidate))
        {
            return TimestampDetection.None;
        }

        return new TimestampDetection(
            ParseCandidate(candidate),
            closingBracket + 1,
            true);
    }

    private static ParsedLogTimestamp? ParseCandidate(string candidate)
    {
        if (HasExplicitOffset(candidate)
            && DateTimeOffset.TryParseExact(
                candidate,
                OffsetDateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset dateTimeOffset))
        {
            return new ParsedLogTimestamp(
                candidate,
                DateOnly.FromDateTime(dateTimeOffset.DateTime),
                TimeOnly.FromDateTime(dateTimeOffset.DateTime),
                dateTimeOffset.Offset);
        }

        if (DateTime.TryParseExact(
                candidate,
                FullDateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime dateTime))
        {
            return new ParsedLogTimestamp(
                candidate,
                DateOnly.FromDateTime(dateTime),
                TimeOnly.FromDateTime(dateTime),
                null);
        }

        if (TimeOnly.TryParseExact(
                candidate,
                TimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly time))
        {
            return new ParsedLogTimestamp(candidate, null, time, null);
        }

        return null;
    }

    private static bool HasExplicitOffset(string candidate)
    {
        if (candidate.EndsWith('Z') || candidate.EndsWith('z'))
        {
            return true;
        }

        if (candidate.Length < 6)
        {
            return false;
        }

        int offsetStart = candidate.Length - 6;
        return (candidate[offsetStart] == '+' || candidate[offsetStart] == '-')
               && candidate[offsetStart + 3] == ':';
    }

    [GeneratedRegex(
        @"^\s*(?<timestamp>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})?)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex IsoTimestampRegex();

    [GeneratedRegex(
        @"^\s*(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex YearFirstTimestampRegex();

    [GeneratedRegex(
        @"^\s*(?<timestamp>\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex DayFirstTimestampRegex();

    [GeneratedRegex(
        @"^\s*(?<timestamp>\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex TimeOnlyTimestampRegex();

    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}(?:T| )\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})?|\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}(?:\.\d{1,7})?|\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex TimestampShapeRegex();
}
