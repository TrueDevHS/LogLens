namespace LogLens.Core.Persistence;

public static class LocalSessionPolicy
{
    public const int CurrentSessionVersion = 1;
    public const string ApplicationDataFolderName = "LogLens";
    public const string SessionFileName = "session-v1.json";
    public const string TemporarySessionFileName = "session-v1.tmp";
    public const string BackupSessionFileName = "session-v1.bak";
    public const long MaximumSessionFileBytes = 64L * 1024 * 1024;
    public const long MaximumPersistedRawCharacters = 16L * 1024 * 1024;
    public const int MaximumSearchTextCharacters = 4_096;
    public const string FinalErasePhrase = "ERASE LOGLENS DATA";

    private static readonly HashSet<string> SupportedSections = new(
        ["Home", "Dashboard", "Explorer", "Patterns", "About"],
        StringComparer.Ordinal);

    public static bool IsSupportedSection(string section) =>
        SupportedSections.Contains(section);
}
