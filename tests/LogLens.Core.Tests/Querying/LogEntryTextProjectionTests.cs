using LogLens.Core.Querying;

namespace LogLens.Core.Tests.Querying;

[TestClass]
public sealed class LogEntryTextProjectionTests
{
    [TestMethod]
    public void CreatePreview_ShortTextIsUnchanged()
    {
        const string text = "INFO Short inert text";

        string preview = LogEntryTextProjection.CreatePreview(text);

        Assert.AreEqual(text, preview);
    }

    [TestMethod]
    public void CreatePreview_LongTextIsBoundedAndMarkedWithEllipsis()
    {
        string text = new('x', LogEntryTextProjection.PreviewCharacterLimit * 2);

        string preview = LogEntryTextProjection.CreatePreview(text);

        Assert.IsLessThanOrEqualTo(LogEntryTextProjection.PreviewCharacterLimit, preview.Length);
        StringAssert.EndsWith(preview, "…");
        Assert.AreEqual(new string('x', preview.Length - 1), preview[..^1]);
    }

    [TestMethod]
    public void CreateBounded_DoesNotSplitUnicodeSurrogatePair()
    {
        const int limit = 10;
        string text = new string('a', limit - 2) + "🔍" + "tail";

        BoundedEntryText result = LogEntryTextProjection.CreateBounded(text, limit);

        Assert.IsTrue(result.IsTruncated);
        Assert.DoesNotContain('�', result.Text);
        Assert.HasCount(0, result.Text.Where(char.IsSurrogate).ToArray());
        StringAssert.EndsWith(result.Text, "…");
    }

    [TestMethod]
    public void CreateDetail_PreservesCompleteModelTextWhileBoundingDisplayProjection()
    {
        string rawText = new('r', LogEntryTextProjection.DetailCharacterLimit + 100);

        BoundedEntryText result = LogEntryTextProjection.CreateDetail(rawText);

        Assert.IsTrue(result.IsTruncated);
        Assert.AreEqual(rawText.Length, result.OriginalCharacterCount);
        Assert.AreEqual(LogEntryTextProjection.DetailCharacterLimit, result.Text.Length);
        Assert.AreEqual(LogEntryTextProjection.DetailCharacterLimit + 100, rawText.Length);
    }
}
