namespace LogLens.Core.Querying;

public sealed record BoundedEntryText(
    string Text,
    int OriginalCharacterCount,
    bool IsTruncated);

public static class LogEntryTextProjection
{
    public const int PreviewCharacterLimit = 240;
    public const int DetailCharacterLimit = 32_768;

    public static string CreatePreview(string text) =>
        CreateBounded(text, PreviewCharacterLimit).Text;

    public static BoundedEntryText CreateDetail(string text) =>
        CreateBounded(text, DetailCharacterLimit);

    public static BoundedEntryText CreateBounded(string text, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (maximumCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                "The display limit must be at least one character.");
        }

        if (text.Length <= maximumCharacters)
        {
            return new BoundedEntryText(text, text.Length, false);
        }

        int prefixLength = maximumCharacters - 1;
        if (prefixLength > 0
            && prefixLength < text.Length
            && char.IsHighSurrogate(text[prefixLength - 1])
            && char.IsLowSurrogate(text[prefixLength]))
        {
            prefixLength--;
        }

        return new BoundedEntryText(
            string.Concat(text.AsSpan(0, prefixLength), "…"),
            text.Length,
            true);
    }
}
