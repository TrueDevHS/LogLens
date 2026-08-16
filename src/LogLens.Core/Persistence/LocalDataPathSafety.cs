using System.Security;

namespace LogLens.Core.Persistence;

internal static class LocalDataPathSafety
{
    public static string GetCanonicalRoot(ILocalAppDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        string suppliedRoot = pathProvider.GetLogLensDataRoot();
        if (string.IsNullOrWhiteSpace(suppliedRoot) || IsDevicePath(suppliedRoot))
        {
            throw new InvalidOperationException("The LogLens local-data path is invalid.");
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(suppliedRoot));
        if (!Path.IsPathFullyQualified(root)
            || root.StartsWith(@"\\", StringComparison.Ordinal)
            || !Path.GetFileName(root).Equals(
                LocalSessionPolicy.ApplicationDataFolderName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The LogLens local-data root must be a local folder named LogLens.");
        }

        string? driveRoot = Path.GetPathRoot(root);
        if (string.IsNullOrWhiteSpace(driveRoot)
            || new DriveInfo(driveRoot).DriveType == DriveType.Network)
        {
            throw new InvalidOperationException(
                "LogLens local data must be stored on a local drive.");
        }

        return root;
    }

    public static string GetKnownFilePath(string root, string fileName)
    {
        string path = Path.GetFullPath(Path.Combine(root, fileName));
        EnsureInsideRoot(root, path, allowRoot: false);
        return path;
    }

    public static void EnsureRootCanBeCreated(string root)
    {
        string? parent = Path.GetDirectoryName(root);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                "The Windows local application-data folder is unavailable.");
        }

        RejectReparsePointsOnExistingPath(parent);
    }

    public static void EnsureSafeExistingRoot(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        RejectReparsePointsOnExistingPath(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new SecurityException(
                "The LogLens local-data folder is redirected or linked.");
        }
    }

    public static void EnsureInsideRoot(string root, string candidate, bool allowRoot)
    {
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string canonicalCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (allowRoot && string.Equals(
                canonicalRoot,
                canonicalCandidate,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string prefix = canonicalRoot + Path.DirectorySeparatorChar;
        if (!canonicalCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException(
                "A local-data operation attempted to leave the LogLens storage boundary.");
        }
    }

    public static void RejectReparseEntry(string root, string path)
    {
        EnsureInsideRoot(root, path, allowRoot: false);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new SecurityException(
                "Linked or redirected entries are not allowed inside LogLens local data.");
        }
    }

    private static void RejectReparsePointsOnExistingPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string pathRoot = Path.GetPathRoot(fullPath)!;
        string current = pathRoot;
        string relative = fullPath[pathRoot.Length..];

        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                break;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new SecurityException(
                    "The LogLens local-data path contains a linked or redirected entry.");
            }
        }
    }

    private static bool IsDevicePath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal)
        || path.StartsWith(@"\\.\", StringComparison.Ordinal)
        || path.StartsWith(@"\??\", StringComparison.Ordinal);
}
