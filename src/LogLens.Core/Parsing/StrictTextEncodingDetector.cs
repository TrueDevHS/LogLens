using System.Text;

namespace LogLens.Core.Parsing;

internal sealed record DetectedTextEncoding(
    Encoding Encoding,
    int PreambleLength,
    string DisplayName);

internal static class StrictTextEncodingDetector
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly Encoding StrictUtf16LittleEndian = new UnicodeEncoding(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    private static readonly Encoding StrictUtf16BigEndian = new UnicodeEncoding(
        bigEndian: true,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    public static async Task<DetectedTextEncoding> DetectAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        if (!source.CanSeek)
        {
            throw new LogParsingException(
                LogParsingErrorKind.UnsafeInputStream,
                "LogLens could not safely inspect this stream. The original file was not modified.");
        }

        long initialPosition = source.Position;
        byte[] prefix = new byte[4];
        int prefixLength = 0;

        while (prefixLength < prefix.Length)
        {
            int bytesRead = await source.ReadAsync(
                prefix.AsMemory(prefixLength, prefix.Length - prefixLength),
                cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                break;
            }

            prefixLength += bytesRead;
        }

        if (prefixLength >= 4
            && ((prefix[0] == 0xFF && prefix[1] == 0xFE && prefix[2] == 0x00 && prefix[3] == 0x00)
                || (prefix[0] == 0x00 && prefix[1] == 0x00 && prefix[2] == 0xFE && prefix[3] == 0xFF)))
        {
            source.Position = initialPosition;
            throw new LogParsingException(
                LogParsingErrorKind.UnsupportedEncoding,
                "This file uses UTF-32, which LogLens does not currently support. The original file was not modified.");
        }

        DetectedTextEncoding detected = prefixLength >= 3
                                        && prefix[0] == 0xEF
                                        && prefix[1] == 0xBB
                                        && prefix[2] == 0xBF
            ? new DetectedTextEncoding(StrictUtf8, 3, "UTF-8")
            : prefixLength >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE
                ? new DetectedTextEncoding(StrictUtf16LittleEndian, 2, "UTF-16 LE")
                : prefixLength >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF
                    ? new DetectedTextEncoding(StrictUtf16BigEndian, 2, "UTF-16 BE")
                    : new DetectedTextEncoding(StrictUtf8, 0, "UTF-8");

        source.Position = initialPosition + detected.PreambleLength;
        return detected;
    }
}
