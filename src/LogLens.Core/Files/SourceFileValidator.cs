using System.Security;

namespace LogLens.Core.Files;

public sealed class SourceFileValidator : ISourceFileValidator
{
    public ValidatedSourceFile Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw Error(
                SourceFileErrorKind.InvalidPath,
                "Choose a valid local .log or .txt file.");
        }

        if (IsDevicePath(path))
        {
            throw Error(
                SourceFileErrorKind.DevicePath,
                "Device paths are not supported. Choose a regular local file.");
        }

        if (IsUncPath(path))
        {
            throw Error(
                SourceFileErrorKind.NetworkLocation,
                "Network files are not supported. Copy the log to a local drive and choose it there.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException
                                          or SecurityException)
        {
            throw Error(
                SourceFileErrorKind.InvalidPath,
                "The selected path is not valid. Choose a local .log or .txt file.",
                exception);
        }

        if (!SourceFilePolicy.IsSupportedExtension(Path.GetExtension(fullPath)))
        {
            throw Error(
                SourceFileErrorKind.UnsupportedExtension,
                "LogLens currently supports only .log and .txt files.");
        }

        RejectNetworkDrive(fullPath);

        FileAttributes attributes;
        try
        {
            RejectReparsePoints(fullPath);
            attributes = File.GetAttributes(fullPath);
        }
        catch (SourceFileException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                          or DirectoryNotFoundException)
        {
            throw Error(
                SourceFileErrorKind.FileNotFound,
                "The selected file could not be found. It may have been moved or deleted.",
                exception);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                          or SecurityException)
        {
            throw Error(
                SourceFileErrorKind.PermissionDenied,
                "LogLens does not have permission to read this file. No permissions were changed.",
                exception);
        }
        catch (IOException exception)
        {
            throw Error(
                SourceFileErrorKind.FileUnavailable,
                "The selected file is unavailable or locked by another application.",
                exception);
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw Error(
                SourceFileErrorKind.NotRegularFile,
                "Folders cannot be opened. Choose a single .log or .txt file.");
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Error(
                SourceFileErrorKind.ReparsePoint,
                "Redirected or linked files are not supported. Choose a regular local file.");
        }

        try
        {
            var file = new FileInfo(fullPath);
            file.Refresh();

            if (!file.Exists)
            {
                throw Error(
                    SourceFileErrorKind.FileNotFound,
                    "The selected file could not be found. It may have been moved or deleted.");
            }

            if (file.Length > SourceFilePolicy.MaximumFileSizeBytes)
            {
                throw Error(
                    SourceFileErrorKind.FileTooLarge,
                    "This file is larger than LogLens's current 25 MiB limit. Choose a smaller file.");
            }

            return new ValidatedSourceFile(
                file.FullName,
                file.Name,
                file.Length,
                file.LastWriteTimeUtc);
        }
        catch (SourceFileException)
        {
            throw;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                          or SecurityException)
        {
            throw Error(
                SourceFileErrorKind.PermissionDenied,
                "LogLens does not have permission to read this file. No permissions were changed.",
                exception);
        }
        catch (IOException exception)
        {
            throw Error(
                SourceFileErrorKind.FileUnavailable,
                "The selected file is unavailable or locked by another application.",
                exception);
        }
    }

    private static void RejectNetworkDrive(string fullPath)
    {
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw Error(
                SourceFileErrorKind.InvalidPath,
                "The selected path is not valid. Choose a local .log or .txt file.");
        }

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType == DriveType.Network)
            {
                throw Error(
                    SourceFileErrorKind.NetworkLocation,
                    "Mapped network drives are not supported. Copy the log to a local drive first.");
            }
        }
        catch (SourceFileException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or DriveNotFoundException
                                          or IOException)
        {
            throw Error(
                SourceFileErrorKind.InvalidPath,
                "The selected drive is not available. Choose a file on a local drive.",
                exception);
        }
    }

    private static void RejectReparsePoints(string fullPath)
    {
        string root = Path.GetPathRoot(fullPath)!;
        string currentPath = root;
        string relativePath = fullPath[root.Length..];

        foreach (string segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            FileAttributes attributes = File.GetAttributes(currentPath);

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw Error(
                    SourceFileErrorKind.ReparsePoint,
                    "Redirected or linked paths are not supported. Choose a regular local file.");
            }
        }
    }

    private static bool IsDevicePath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal)
        || path.StartsWith(@"\\.\", StringComparison.Ordinal)
        || path.StartsWith(@"\??\", StringComparison.Ordinal);

    private static bool IsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal);

    private static SourceFileException Error(
        SourceFileErrorKind kind,
        string message,
        Exception? innerException = null) =>
        innerException is null
            ? new SourceFileException(kind, message)
            : new SourceFileException(kind, message, innerException);
}
