using LogLens.Core.Files;

namespace LogLens.Core.Tests.Files;

[TestClass]
public sealed class SourceFileSnapshotTests
{
    [TestMethod]
    public void Matches_ReturnsTrueForIdenticalMetadata()
    {
        var timestamp = new DateTime(2026, 8, 14, 10, 30, 0, DateTimeKind.Utc);
        var first = new SourceFileSnapshot(1_024, timestamp);
        var second = new SourceFileSnapshot(1_024, timestamp);

        Assert.IsTrue(first.Matches(second));
    }

    [TestMethod]
    public void Matches_ReturnsFalseWhenLengthOrTimestampChanges()
    {
        var timestamp = new DateTime(2026, 8, 14, 10, 30, 0, DateTimeKind.Utc);
        var original = new SourceFileSnapshot(1_024, timestamp);

        Assert.IsFalse(original.Matches(new SourceFileSnapshot(2_048, timestamp)));
        Assert.IsFalse(original.Matches(new SourceFileSnapshot(1_024, timestamp.AddSeconds(1))));
        Assert.IsFalse(original.Matches(null));
    }
}
