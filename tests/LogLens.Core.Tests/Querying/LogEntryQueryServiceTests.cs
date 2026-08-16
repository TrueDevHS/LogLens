using LogLens.Core.Parsing;
using LogLens.Core.Querying;

namespace LogLens.Core.Tests.Querying;

[TestClass]
public sealed class LogEntryQueryServiceTests
{
    private readonly LogEntryQueryService _service = new();

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    public void Query_EmptySearchReturnsAllEntries(string? searchText)
    {
        ParsedLogEntry[] entries = CreateEntries();

        LogEntryQueryResult result = Query(entries, searchText);

        Assert.AreEqual(entries.Length, result.TotalEntries);
        Assert.AreEqual(entries.Length, result.VisibleEntries);
        CollectionAssert.AreEqual(entries, result.Entries.ToArray());
    }

    [TestMethod]
    public void Query_SearchIsCaseInsensitiveAndCultureIndependent()
    {
        LogEntryQueryResult result = Query(CreateEntries(), "ALPHA SERVICE");

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(1, result.Entries[0].LineNumber);
    }

    [TestMethod]
    public void Query_UsesExactSubstringRatherThanTokenOrFuzzyMatching()
    {
        ParsedLogEntry[] entries = CreateEntries();

        LogEntryQueryResult exact = Query(entries, "service started");
        LogEntryQueryResult partial = Query(entries, "vice star");
        LogEntryQueryResult fuzzy = Query(entries, "servce started");

        Assert.HasCount(1, exact.Entries);
        Assert.HasCount(1, partial.Entries);
        Assert.HasCount(0, fuzzy.Entries);
    }

    [TestMethod]
    public void Query_SearchesUnicodeText()
    {
        LogEntryQueryResult result = Query(CreateEntries(), "CAFÉ Δ");

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(4, result.Entries[0].LineNumber);
    }

    [TestMethod]
    public void Query_SearchesPunctuationExactly()
    {
        LogEntryQueryResult result = Query(CreateEntries(), "?code=E-42");

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(3, result.Entries[0].LineNumber);
    }

    [TestMethod]
    public void Query_SearchesUrlAsInertRawText()
    {
        LogEntryQueryResult result = Query(
            CreateEntries(),
            "https://example.invalid/path?q=one");

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(5, result.Entries[0].LineNumber);
        StringAssert.Contains(result.Entries[0].RawText, "https://");
    }

    [TestMethod]
    public void Query_SearchesCommandLookingTextWithoutChangingIt()
    {
        ParsedLogEntry[] entries = CreateEntries();
        string rawBefore = entries[6].RawText;

        LogEntryQueryResult result = Query(entries, "Remove-Item C:\\Temp\\sample.txt");

        Assert.HasCount(1, result.Entries);
        Assert.AreSame(entries[6], result.Entries[0]);
        Assert.AreEqual(rawBefore, result.Entries[0].RawText);
    }

    [TestMethod]
    public void Query_NoMatchesReturnsTruthfulEmptyResult()
    {
        ParsedLogEntry[] entries = CreateEntries();

        LogEntryQueryResult result = Query(entries, "not present anywhere");

        Assert.AreEqual(entries.Length, result.TotalEntries);
        Assert.AreEqual(0, result.VisibleEntries);
        Assert.HasCount(0, result.Entries);
    }

    [TestMethod]
    public void Query_OneMatchReturnsOnlyThatOriginalEntry()
    {
        ParsedLogEntry[] entries = CreateEntries();

        LogEntryQueryResult result = Query(entries, "fatal worker stopped");

        Assert.HasCount(1, result.Entries);
        Assert.AreSame(entries[5], result.Entries[0]);
    }

    [TestMethod]
    public void Query_MultipleMatchesRetainOriginalOrder()
    {
        LogEntryQueryResult result = Query(CreateEntries(), "service");

        CollectionAssert.AreEqual(
            new[] { 1, 2, 8 },
            result.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    [DataRow(LogSeverityFilter.Trace, LogSeverity.Trace, 5)]
    [DataRow(LogSeverityFilter.Debug, LogSeverity.Debug, 4)]
    [DataRow(LogSeverityFilter.Information, LogSeverity.Information, 1)]
    [DataRow(LogSeverityFilter.Warning, LogSeverity.Warning, 2)]
    [DataRow(LogSeverityFilter.Error, LogSeverity.Error, 3)]
    [DataRow(LogSeverityFilter.Critical, LogSeverity.Critical, 6)]
    [DataRow(LogSeverityFilter.Unknown, LogSeverity.Unknown, 7)]
    public void Query_EachSeveritySelectionMatchesOnlyThatSeverity(
        LogSeverityFilter filter,
        LogSeverity expectedSeverity,
        int expectedFirstLine)
    {
        LogEntryQueryResult result = Query(CreateEntries(), severities: filter);

        Assert.IsTrue(result.Entries.All(entry => entry.Severity == expectedSeverity));
        Assert.AreEqual(expectedFirstLine, result.Entries[0].LineNumber);
    }

    [TestMethod]
    public void Query_SeverityOnlyFilterReturnsEveryEntryInThatSeverity()
    {
        LogEntryQueryResult result = Query(
            CreateEntries(),
            severities: LogSeverityFilter.Information);

        CollectionAssert.AreEqual(
            new[] { 1, 8 },
            result.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Query_UnknownSelectionReturnsUnclassifiedEntries()
    {
        LogEntryQueryResult result = Query(
            CreateEntries(),
            severities: LogSeverityFilter.Unknown);

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(LogSeverity.Unknown, result.Entries[0].Severity);
    }

    [TestMethod]
    public void Query_MultipleSeveritySelectionsAreCombinedAsUnion()
    {
        LogSeverityFilter filters = LogSeverityFilter.Warning | LogSeverityFilter.Error;

        LogEntryQueryResult result = Query(CreateEntries(), severities: filters);

        CollectionAssert.AreEqual(
            new[] { 2, 3 },
            result.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Query_NoSeveritySelectionReturnsNoEntries()
    {
        LogEntryQueryResult result = Query(
            CreateEntries(),
            severities: LogSeverityFilter.None);

        Assert.AreEqual(0, result.VisibleEntries);
    }

    [TestMethod]
    public void Query_HasTimestampReturnsOnlyTimestampedEntries()
    {
        LogEntryQueryResult result = Query(
            CreateEntries(),
            timestampPresence: TimestampPresenceFilter.HasTimestamp);

        Assert.IsTrue(result.Entries.All(entry => entry.Timestamp is not null));
        CollectionAssert.AreEqual(
            new[] { 1, 3, 6, 8 },
            result.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Query_NoTimestampReturnsOnlyEntriesWithoutTimestamp()
    {
        LogEntryQueryResult result = Query(
            CreateEntries(),
            timestampPresence: TimestampPresenceFilter.NoTimestamp);

        Assert.IsTrue(result.Entries.All(entry => entry.Timestamp is null));
        CollectionAssert.AreEqual(
            new[] { 2, 4, 5, 7 },
            result.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Query_CombinesSearchAndSeverity()
    {
        LogEntryQueryResult result = Query(
            CreateEntries(),
            "request",
            LogSeverityFilter.Error);

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(3, result.Entries[0].LineNumber);
    }

    [TestMethod]
    public void Query_CombinesSearchAndTimestampPresence()
    {
        LogEntryQueryResult result = Query(
            CreateEntries(),
            "service",
            timestampPresence: TimestampPresenceFilter.HasTimestamp);

        CollectionAssert.AreEqual(
            new[] { 1, 8 },
            result.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Query_CombinesSeverityAndTimestampPresence()
    {
        LogSeverityFilter severities = LogSeverityFilter.Debug | LogSeverityFilter.Warning;

        LogEntryQueryResult result = Query(
            CreateEntries(),
            severities: severities,
            timestampPresence: TimestampPresenceFilter.NoTimestamp);

        CollectionAssert.AreEqual(
            new[] { 2, 4 },
            result.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Query_CombinesSearchSeverityAndTimestampPresence()
    {
        LogEntryQueryResult result = Query(
            CreateEntries(),
            "failed",
            LogSeverityFilter.Error,
            TimestampPresenceFilter.HasTimestamp);

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(3, result.Entries[0].LineNumber);
    }

    [TestMethod]
    public void Query_ResultCountsAlwaysReflectInputAndMatches()
    {
        ParsedLogEntry[] entries = CreateEntries();

        LogEntryQueryResult result = Query(
            entries,
            severities: LogSeverityFilter.Information);

        Assert.AreEqual(8, result.TotalEntries);
        Assert.AreEqual(2, result.VisibleEntries);
    }

    [TestMethod]
    public void Query_DoesNotMutateOrReorderOriginalEntries()
    {
        ParsedLogEntry[] entries = CreateEntries();
        ParsedLogEntry[] before = entries.ToArray();

        _ = Query(
            entries,
            "service",
            LogSeverityFilter.Information,
            TimestampPresenceFilter.HasTimestamp);

        CollectionAssert.AreEqual(before, entries);
        for (int index = 0; index < entries.Length; index++)
        {
            Assert.AreSame(before[index], entries[index]);
        }
    }

    [TestMethod]
    public void Query_PreservesCompleteRawTextInReturnedEntry()
    {
        ParsedLogEntry[] entries = CreateEntries();

        LogEntryQueryResult result = Query(entries, "powershell.exe");

        Assert.AreSame(entries[6], result.Entries[0]);
        Assert.AreEqual(entries[6].RawText, result.Entries[0].RawText);
    }

    [TestMethod]
    public void Query_SearchesBeyondUiPreviewLimitInLongLine()
    {
        string rawText = "INFO "
                         + new string('x', LogEntryTextProjection.PreviewCharacterLimit * 3)
                         + " needle-after-preview";
        ParsedLogEntry[] entries =
        [
            new ParsedLogEntry(1, rawText, LogSeverity.Information, null, rawText[5..])
        ];

        LogEntryQueryResult result = Query(entries, "needle-after-preview");

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(rawText, result.Entries[0].RawText);
    }

    [TestMethod]
    public void Query_ObservesCancellationBeforeScanning()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            _service.Query(CreateEntries(), LogEntryQuery.ShowAll, cancellation.Token));
    }

    [TestMethod]
    public void Query_ObservesCancellationDuringScanning()
    {
        using var cancellation = new CancellationTokenSource();
        var entries = new CancellingEntryList(CreateEntries(), cancellation, cancelBeforeIndex: 3);
        var query = new LogEntryQuery(
            "service",
            LogSeverityFilter.All,
            TimestampPresenceFilter.All);

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            _service.Query(entries, query, cancellation.Token));
    }

    private LogEntryQueryResult Query(
        IReadOnlyList<ParsedLogEntry> entries,
        string? searchText = "",
        LogSeverityFilter severities = LogSeverityFilter.All,
        TimestampPresenceFilter timestampPresence = TimestampPresenceFilter.All) =>
        _service.Query(
            entries,
            new LogEntryQuery(searchText, severities, timestampPresence));

    private static ParsedLogEntry[] CreateEntries() =>
    [
        Entry(1, "2026-08-14 10:00:00 INFO Alpha service started", LogSeverity.Information, true),
        Entry(2, "WARN Cache service is slow", LogSeverity.Warning, false),
        Entry(3, "2026-08-14T10:00:02Z ERROR Request failed /api?code=E-42", LogSeverity.Error, true),
        Entry(4, "DEBUG café Δ diagnostic", LogSeverity.Debug, false),
        Entry(5, "TRACE https://example.invalid/path?q=one", LogSeverity.Trace, false),
        Entry(6, "10:00:03 FATAL worker stopped", LogSeverity.Critical, true),
        Entry(7, "powershell.exe -Command \"Remove-Item C:\\Temp\\sample.txt\"", LogSeverity.Unknown, false),
        Entry(8, "14/08/2026 10:00:04 INFORMATION Beta service ready", LogSeverity.Information, true)
    ];

    private static ParsedLogEntry Entry(
        int lineNumber,
        string rawText,
        LogSeverity severity,
        bool hasTimestamp)
    {
        ParsedLogTimestamp? timestamp = hasTimestamp
            ? new ParsedLogTimestamp(
                "2026-08-14 10:00:00",
                new DateOnly(2026, 8, 14),
                new TimeOnly(10, 0),
                null)
            : null;

        return new ParsedLogEntry(lineNumber, rawText, severity, timestamp, rawText);
    }

    private sealed class CancellingEntryList(
        IReadOnlyList<ParsedLogEntry> entries,
        CancellationTokenSource cancellation,
        int cancelBeforeIndex) : IReadOnlyList<ParsedLogEntry>
    {
        public int Count => entries.Count;

        public ParsedLogEntry this[int index] => entries[index];

        public IEnumerator<ParsedLogEntry> GetEnumerator()
        {
            for (int index = 0; index < entries.Count; index++)
            {
                if (index == cancelBeforeIndex)
                {
                    cancellation.Cancel();
                }

                yield return entries[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
