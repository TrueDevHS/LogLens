using System.Buffers;
using System.Security;
using System.Security.Cryptography;

namespace LogLens.Core.Files;

public sealed class ReadOnlySourceFileLoader : IReadOnlySourceFileLoader
{
    private readonly ISourceFileValidator _validator;

    public ReadOnlySourceFileLoader()
        : this(new SourceFileValidator())
    {
    }

    public ReadOnlySourceFileLoader(ISourceFileValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<SourceFileInspection> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        SourceFileReadResult<bool> result = await ReadAsync(
            path,
            static (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(true);
            },
            cancellationToken).ConfigureAwait(false);

        return new SourceFileInspection(
            result.Source.FullPath,
            result.Source.FileName,
            result.BeforeRead.Length,
            result.Sha256,
            result.BeforeRead,
            result.AfterRead,
            result.SourceChangedDuringRead);
    }

    public async Task<SourceFileReadResult<T>> ReadAsync<T>(
        string path,
        Func<Stream, CancellationToken, Task<T>> readContentAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readContentAsync);
        cancellationToken.ThrowIfCancellationRequested();

        ValidatedSourceFile source = _validator.Validate(path);
        FileStream stream = OpenReadOnly(source.FullPath);

        await using (stream.ConfigureAwait(false))
        {
            if (!stream.CanRead || stream.CanWrite)
            {
                throw new SourceFileException(
                    SourceFileErrorKind.UnsafeStream,
                    "LogLens could not establish a strictly read-only file handle. The file was not analysed.");
            }

            long openLength;
            try
            {
                openLength = stream.Length;
            }
            catch (IOException exception)
            {
                throw new SourceFileException(
                    SourceFileErrorKind.ReadFailure,
                    "LogLens could not inspect this file. The original file was not modified.",
                    exception);
            }

            if (openLength > SourceFilePolicy.MaximumFileSizeBytes)
            {
                throw new SourceFileException(
                    SourceFileErrorKind.FileTooLarge,
                    "This file is larger than LogLens's current 25 MiB limit. Choose a smaller file.");
            }

            var beforeRead = new SourceFileSnapshot(openLength, source.LastWriteTimeUtc);
            T data;

            try
            {
                data = await readContentAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SourceFileException)
            {
                throw;
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new SourceFileException(
                    SourceFileErrorKind.PermissionDenied,
                    "LogLens lost permission to read this file. No permissions were changed.",
                    exception);
            }
            catch (IOException exception)
            {
                throw new SourceFileException(
                    SourceFileErrorKind.ReadFailure,
                    "LogLens could not finish reading this file. The original file was not modified.",
                    exception);
            }

            stream.Position = 0;
            string sha256 = await ComputeSha256Async(stream, cancellationToken).ConfigureAwait(false);
            SourceFileSnapshot? afterRead = CaptureSnapshot(source.FullPath);
            return new SourceFileReadResult<T>(source, beforeRead, afterRead, data)
            {
                Sha256 = sha256
            };
        }
    }

    private static FileStream OpenReadOnly(string fullPath)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            BufferSize = SourceFilePolicy.ReadBufferSizeBytes,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        try
        {
            return new FileStream(fullPath, options);
        }
        catch (FileNotFoundException exception)
        {
            throw new SourceFileException(
                SourceFileErrorKind.FileNotFound,
                "The selected file could not be found. It may have been moved or deleted.",
                exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new SourceFileException(
                SourceFileErrorKind.FileNotFound,
                "The selected file could not be found. It may have been moved or deleted.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SourceFileException(
                SourceFileErrorKind.PermissionDenied,
                "LogLens does not have permission to read this file. No permissions were changed.",
                exception);
        }
        catch (SecurityException exception)
        {
            throw new SourceFileException(
                SourceFileErrorKind.PermissionDenied,
                "LogLens does not have permission to read this file. No permissions were changed.",
                exception);
        }
        catch (IOException exception)
        {
            throw new SourceFileException(
                SourceFileErrorKind.FileUnavailable,
                "The selected file is unavailable or locked by another application.",
                exception);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException)
        {
            throw new SourceFileException(
                SourceFileErrorKind.InvalidPath,
                "The selected path is not valid. Choose a local .log or .txt file.",
                exception);
        }
    }

    private static SourceFileSnapshot? CaptureSnapshot(string fullPath)
    {
        try
        {
            var file = new FileInfo(fullPath);
            file.Refresh();
            return file.Exists
                ? new SourceFileSnapshot(file.Length, file.LastWriteTimeUtc)
                : null;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or SecurityException)
        {
            return null;
        }
    }

    private static async Task<string> ComputeSha256Async(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(SourceFilePolicy.ReadBufferSizeBytes);
        long bytesReadTotal = 0;

        try
        {
            while (true)
            {
                int bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, SourceFilePolicy.ReadBufferSizeBytes),
                    cancellationToken).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    break;
                }

                bytesReadTotal += bytesRead;
                if (bytesReadTotal > SourceFilePolicy.MaximumFileSizeBytes)
                {
                    throw new SourceFileException(
                        SourceFileErrorKind.FileTooLarge,
                        "The source grew beyond LogLens's 25 MiB limit while it was being read. Analysis stopped safely.");
                }

                hash.AppendData(buffer, 0, bytesRead);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
