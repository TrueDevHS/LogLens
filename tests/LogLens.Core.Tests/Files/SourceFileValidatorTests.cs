using LogLens.Core.Files;

namespace LogLens.Core.Tests.Files;

[TestClass]
public sealed class SourceFileValidatorTests
{
    private readonly SourceFileValidator _validator = new();

    [TestMethod]
    [DataRow("application.log")]
    [DataRow("application.txt")]
    [DataRow("APPLICATION.LOG")]
    public void Validate_AcceptsSupportedLocalFiles(string fileName)
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText(fileName, "Synthetic test entry");

        ValidatedSourceFile result = _validator.Validate(path);

        Assert.AreEqual(Path.GetFullPath(path), result.FullPath);
        Assert.AreEqual(fileName, result.FileName);
        Assert.AreEqual(new FileInfo(path).Length, result.Length);
    }

    [TestMethod]
    public void Validate_RejectsUnsupportedExtension()
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText("application.json", "{}");

        SourceFileException exception = Capture(() => _validator.Validate(path));

        Assert.AreEqual(SourceFileErrorKind.UnsupportedExtension, exception.Kind);
    }

    [TestMethod]
    public void Validate_RejectsFileAboveTwentyFiveMebibytes()
    {
        using var files = new SyntheticFileScope();
        string path = files.CreateSizedFile(
            "oversized.log",
            SourceFilePolicy.MaximumFileSizeBytes + 1);

        SourceFileException exception = Capture(() => _validator.Validate(path));

        Assert.AreEqual(SourceFileErrorKind.FileTooLarge, exception.Kind);
        StringAssert.Contains(exception.Message, "25 MiB");
    }

    [TestMethod]
    public void Validate_RejectsMissingFile()
    {
        using var files = new SyntheticFileScope();
        string path = Path.Combine(files.DirectoryPath, "missing.log");

        SourceFileException exception = Capture(() => _validator.Validate(path));

        Assert.AreEqual(SourceFileErrorKind.FileNotFound, exception.Kind);
    }

    [TestMethod]
    public void Validate_RejectsUncPathWithoutAccessingIt()
    {
        SourceFileException exception = Capture(
            () => _validator.Validate(@"\\example.invalid\logs\application.log"));

        Assert.AreEqual(SourceFileErrorKind.NetworkLocation, exception.Kind);
    }

    [TestMethod]
    public void Validate_RejectsDevicePathWithoutAccessingIt()
    {
        SourceFileException exception = Capture(
            () => _validator.Validate(@"\\.\C:\application.log"));

        Assert.AreEqual(SourceFileErrorKind.DevicePath, exception.Kind);
    }

    private static SourceFileException Capture(Action action)
    {
        try
        {
            action();
            Assert.Fail("Expected source-file validation to fail.");
            throw new InvalidOperationException("Unreachable.");
        }
        catch (SourceFileException exception)
        {
            return exception;
        }
    }
}
