namespace LogLens.Core.Parsing;

public static class LogParsingPolicy
{
    public const int MaximumEntries = 100_000;
    public const int LongLineWarningThresholdCharacters = 65_536;
    public const int MaximumDiagnostics = 1_000;
    public const int ReaderBufferSizeCharacters = 64 * 1024;
}
