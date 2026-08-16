using System.Security;
using System.Text;

namespace LogLens.Core.Reports;

public sealed class ReportDestinationWriter : IReportDestinationWriter
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<ReportWriteResult> WriteAsync(
        ReportWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Report);
        cancellationToken.ThrowIfCancellationRequested();

        string sourcePath = GetCanonicalPath(request.SourcePath, isSource: true);
        string destinationPath = GetCanonicalPath(request.DestinationPath, isSource: false);

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw Error(
                ReportExportErrorKind.SourceOverwrite,
                "The report cannot overwrite the source log. Choose a different filename or location.");
        }

        ValidateExtension(request.Report.Format, destinationPath);
        ValidateLocalDestination(destinationPath);

        if (Directory.Exists(destinationPath))
        {
            throw Error(
                ReportExportErrorKind.InvalidDestination,
                "Choose a report filename rather than a folder.");
        }

        bool destinationExists = File.Exists(destinationPath);
        if (destinationExists && !request.OverwriteConfirmed)
        {
            throw Error(
                ReportExportErrorKind.DestinationExists,
                "A report already exists at this destination. Confirm replacement in the Save dialog or choose another filename.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await File.WriteAllTextAsync(
                destinationPath,
                request.Report.Content,
                Utf8WithoutBom,
                CancellationToken.None).ConfigureAwait(false);

            return new ReportWriteResult(
                Path.GetFileName(destinationPath),
                Utf8WithoutBom.GetByteCount(request.Report.Content));
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Error(
                ReportExportErrorKind.PermissionDenied,
                "LogLens does not have permission to save the report at this destination. No permissions were changed.",
                exception);
        }
        catch (SecurityException exception)
        {
            throw Error(
                ReportExportErrorKind.PermissionDenied,
                "LogLens does not have permission to save the report at this destination. No permissions were changed.",
                exception);
        }
        catch (IOException exception)
        {
            throw Error(
                ReportExportErrorKind.DestinationUnavailable,
                "The report destination is unavailable or locked. Choose another filename or location.",
                exception);
        }
    }

    private static string GetCanonicalPath(string path, bool isSource)
    {
        if (string.IsNullOrWhiteSpace(path) || IsDevicePath(path))
        {
            throw Error(
                ReportExportErrorKind.InvalidDestination,
                isSource
                    ? "The loaded source path is not valid for export protection."
                    : "Choose a valid local report destination.");
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException
                                          or SecurityException)
        {
            throw Error(
                ReportExportErrorKind.InvalidDestination,
                isSource
                    ? "The loaded source path is not valid for export protection."
                    : "Choose a valid local report destination.",
                exception);
        }
    }

    private static void ValidateExtension(ReportFormat format, string destinationPath)
    {
        string expectedExtension = format == ReportFormat.Json ? ".json" : ".txt";
        if (!Path.GetExtension(destinationPath).Equals(
                expectedExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Error(
                ReportExportErrorKind.UnsupportedFormat,
                "LogLens summary reports can be saved only as .txt or .json using the matching selected format.");
        }
    }

    private static void ValidateLocalDestination(string destinationPath)
    {
        if (destinationPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw Error(
                ReportExportErrorKind.NetworkLocation,
                "Reports can be saved only to a local drive. Choose a local destination.");
        }

        string? root = Path.GetPathRoot(destinationPath);
        string? parent = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(root)
            || string.IsNullOrWhiteSpace(parent)
            || !Directory.Exists(parent))
        {
            throw Error(
                ReportExportErrorKind.InvalidDestination,
                "The selected report folder does not exist. Choose an existing local folder.");
        }

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType == DriveType.Network)
            {
                throw Error(
                    ReportExportErrorKind.NetworkLocation,
                    "Mapped network drives are not supported for reports. Choose a local destination.");
            }

            RejectReparsePoints(destinationPath);
        }
        catch (ReportExportException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or DriveNotFoundException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or SecurityException)
        {
            throw Error(
                ReportExportErrorKind.InvalidDestination,
                "The selected report destination could not be validated. Choose another local folder.",
                exception);
        }
    }

    private static void RejectReparsePoints(string destinationPath)
    {
        string root = Path.GetPathRoot(destinationPath)!;
        string currentPath = root;
        string relativePath = destinationPath[root.Length..];

        foreach (string segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!Directory.Exists(currentPath) && !File.Exists(currentPath))
            {
                continue;
            }

            if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw Error(
                    ReportExportErrorKind.ReparsePoint,
                    "Redirected or linked report paths are not supported. Choose a regular local destination.");
            }
        }
    }

    private static bool IsDevicePath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal)
        || path.StartsWith(@"\\.\", StringComparison.Ordinal)
        || path.StartsWith(@"\??\", StringComparison.Ordinal);

    private static ReportExportException Error(
        ReportExportErrorKind kind,
        string message,
        Exception? innerException = null) =>
        innerException is null
            ? new ReportExportException(kind, message)
            : new ReportExportException(kind, message, innerException);
}
