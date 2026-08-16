using System.Text;

namespace LogLens.Core.Tests.Files;

internal sealed class SyntheticFileScope : IDisposable
{
    public SyntheticFileScope()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "LogLens.Core.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string WriteText(string fileName, string content)
    {
        string path = Path.Combine(DirectoryPath, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public string CreateSizedFile(string fileName, long length)
    {
        string path = Path.Combine(DirectoryPath, fileName);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // A failed cleanup must not hide the test result.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed cleanup must not hide the test result.
        }
    }
}
