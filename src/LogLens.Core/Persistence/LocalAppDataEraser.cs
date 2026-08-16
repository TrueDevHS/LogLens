using System.Security;

namespace LogLens.Core.Persistence;

public sealed class LocalAppDataEraser : ILocalAppDataEraser
{
    private readonly ILocalAppDataPathProvider _pathProvider;

    public LocalAppDataEraser()
        : this(new LocalAppDataPathProvider())
    {
    }

    public LocalAppDataEraser(ILocalAppDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider
            ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<LocalDataEraseResult> EraseAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await Task.Run(
                () => EraseCore(cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SecurityException
                                          or InvalidOperationException)
        {
            return new LocalDataEraseResult(
                LocalDataEraseStatus.UnsafeStorage,
                "LogLens local data was not erased because a linked or redirected entry was detected. No linked target was followed.");
        }
        catch (UnauthorizedAccessException)
        {
            return new LocalDataEraseResult(
                LocalDataEraseStatus.AccessDenied,
                "Windows denied access to part of the LogLens local-data folder. Original logs and exported reports were not touched.");
        }
        catch (Exception exception) when (exception is IOException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            return new LocalDataEraseResult(
                LocalDataEraseStatus.Failed,
                "LogLens could not erase all of its local application data. Original logs and exported reports were not touched.");
        }
    }

    private LocalDataEraseResult EraseCore(CancellationToken cancellationToken)
    {
        string root = LocalDataPathSafety.GetCanonicalRoot(_pathProvider);
        if (File.Exists(root))
        {
            throw new SecurityException(
                "The LogLens local-data location is not a regular folder.");
        }

        if (!Directory.Exists(root))
        {
            return new LocalDataEraseResult(
                LocalDataEraseStatus.NothingToErase,
                "No LogLens local application data was present.");
        }

        LocalDataPathSafety.EnsureSafeExistingRoot(root);
        var files = new List<string>();
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                LocalDataPathSafety.EnsureInsideRoot(root, entry, allowRoot: false);
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new SecurityException(
                        "A reparse-point entry was detected inside LogLens local data.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(entry);
                    pending.Push(entry);
                }
                else
                {
                    files.Add(entry);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (string file in files)
        {
            LocalDataPathSafety.EnsureInsideRoot(root, file, allowRoot: false);
            LocalDataPathSafety.RejectReparseEntry(root, file);
            File.Delete(file);
        }

        foreach (string directory in directories
                     .OrderByDescending(path => path.Length))
        {
            LocalDataPathSafety.EnsureInsideRoot(root, directory, allowRoot: false);
            LocalDataPathSafety.RejectReparseEntry(root, directory);
            Directory.Delete(directory, recursive: false);
        }

        LocalDataPathSafety.EnsureSafeExistingRoot(root);
        Directory.Delete(root, recursive: false);
        return new LocalDataEraseResult(
            LocalDataEraseStatus.Erased,
            "All LogLens-owned local application data was erased. Original logs and exported reports were not touched.",
            files.Count,
            directories.Count + 1);
    }
}
