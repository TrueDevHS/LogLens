using LogLens.Core.Analysis;
using LogLens.Core.Patterns;
using LogLens.Core.Tests.Files;

namespace LogLens.Core.Tests.Patterns;

[TestClass]
public sealed class PatternAnalysisIntegrationTests
{
    [TestMethod]
    public async Task AnalysisAndPatternsLeaveSourceBytesAndMetadataUnchanged()
    {
        using var files = new SyntheticFileScope();
        string path = files.WriteText(
            "patterns.log",
            "ERROR exact repeat\nERROR exact repeat\nERROR exact repeat");
        byte[] bytesBefore = File.ReadAllBytes(path);
        long lengthBefore = new FileInfo(path).Length;
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(path);
        FileAttributes attributesBefore = File.GetAttributes(path);
        var analysisService = new LogAnalysisService();
        var patternService = new PatternAnalysisService();

        LogAnalysisResult analysis = await analysisService.AnalyzeAsync(path);
        PatternAnalysisResult patterns = patternService.Analyze(analysis.Parsing.Entries);

        Assert.AreEqual(1, patterns.TotalRepeatedMessagePatterns);
        CollectionAssert.AreEqual(bytesBefore, File.ReadAllBytes(path));
        Assert.AreEqual(lengthBefore, new FileInfo(path).Length);
        Assert.AreEqual(lastWriteBefore, File.GetLastWriteTimeUtc(path));
        Assert.AreEqual(attributesBefore, File.GetAttributes(path));
        Assert.IsFalse(analysis.Source.SourceChangedDuringRead);
    }
}
