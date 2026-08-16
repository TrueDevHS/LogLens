namespace LogLens.Core.Files;

public static class SourceFilePolicy
{
    public const long MaximumFileSizeBytes = 25L * 1024 * 1024;
    public const int ReadBufferSizeBytes = 64 * 1024;

    public static bool IsSupportedExtension(string extension) =>
        extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
}
