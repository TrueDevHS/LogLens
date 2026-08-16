using System.Reflection;
using LogLens.Core.Parsing;
using LogLens.Core.Patterns;

namespace LogLens.Core.Tests.Patterns;

[TestClass]
public sealed class PatternAnalysisServiceTests
{
    private readonly PatternAnalysisService _service = new();

    [TestMethod]
    public void Analyze_NoRepeatedMessagesReturnsEmptyRepeatFindings()
    {
        ParsedLogEntry[] entries =
        [
            Entry(1, "INFO first", LogSeverity.Information),
            Entry(2, "INFO second", LogSeverity.Information)
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.AreEqual(0, result.TotalRepeatedMessagePatterns);
        Assert.HasCount(0, result.TopRepeatedMessages);
        Assert.HasCount(0, result.RepeatedSeverityLeaders);
    }

    [TestMethod]
    public void Analyze_ExactlyTwoExactRawLinesMeetRepeatThreshold()
    {
        const string rawText = "ERROR identical failure";
        ParsedLogEntry[] entries =
        [
            Entry(1, rawText, LogSeverity.Error),
            Entry(2, rawText, LogSeverity.Error)
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.AreEqual(1, result.TotalRepeatedMessagePatterns);
        RepeatedMessageFinding finding = result.TopRepeatedMessages[0];
        Assert.AreEqual(2, finding.OccurrenceCount);
        Assert.AreEqual(rawText, finding.RawText);
    }

    [TestMethod]
    public void Analyze_ManyRepeatsHaveAccurateCountAndBoundedEvidence()
    {
        ParsedLogEntry[] entries = Enumerable.Range(1, 250)
            .Select(line => Entry(line, "WARN repeated", LogSeverity.Warning))
            .ToArray();

        RepeatedMessageFinding finding = _service.Analyze(entries).TopRepeatedMessages[0];

        Assert.AreEqual(250, finding.OccurrenceCount);
        Assert.HasCount(PatternAnalysisPolicy.MaximumEvidenceEntriesPerFinding, finding.Evidence.Entries);
        Assert.AreEqual(250, finding.Evidence.TotalEntryCount);
        Assert.IsTrue(finding.Evidence.IsTruncated);
    }

    [TestMethod]
    [DataRow(LogSeverity.Information, PatternSeverityGroup.Information)]
    [DataRow(LogSeverity.Warning, PatternSeverityGroup.Warning)]
    [DataRow(LogSeverity.Error, PatternSeverityGroup.Error)]
    [DataRow(LogSeverity.Critical, PatternSeverityGroup.Critical)]
    [DataRow(LogSeverity.Unknown, PatternSeverityGroup.Unknown)]
    public void Analyze_RepeatedSeverityIsReportedTruthfully(
        LogSeverity severity,
        PatternSeverityGroup expectedGroup)
    {
        string rawText = $"{severity} exact repeat";
        ParsedLogEntry[] entries =
        [
            Entry(1, rawText, severity),
            Entry(2, rawText, severity)
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.AreEqual(expectedGroup, result.TopRepeatedMessages[0].SeverityGroup);
        Assert.AreSame(
            result.TopRepeatedMessages[0],
            result.RepeatedSeverityLeaders.Single());
    }

    [TestMethod]
    public void Analyze_ExactRepeatMatchingIsCaseSensitiveAndDoesNotTrim()
    {
        ParsedLogEntry[] entries =
        [
            Entry(1, "ERROR Same", LogSeverity.Error),
            Entry(2, "error Same", LogSeverity.Error),
            Entry(3, " ERROR Same", LogSeverity.Error),
            Entry(4, "ERROR Same ", LogSeverity.Error)
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.AreEqual(0, result.TotalRepeatedMessagePatterns);
    }

    [TestMethod]
    public void Analyze_WhitespaceOnlyLinesAreNotReportedAsMessagePatterns()
    {
        ParsedLogEntry[] entries =
        [
            Entry(1, string.Empty, LogSeverity.Unknown),
            Entry(2, string.Empty, LogSeverity.Unknown),
            Entry(3, "   ", LogSeverity.Unknown),
            Entry(4, "   ", LogSeverity.Unknown)
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.AreEqual(0, result.TotalRepeatedMessagePatterns);
    }

    [TestMethod]
    public void Analyze_RepeatedFindingsUseDeterministicCountThenLineOrdering()
    {
        ParsedLogEntry[] entries =
        [
            Entry(1, "INFO first tie", LogSeverity.Information),
            Entry(2, "ERROR larger group", LogSeverity.Error),
            Entry(3, "WARN second tie", LogSeverity.Warning),
            Entry(4, "ERROR larger group", LogSeverity.Error),
            Entry(5, "INFO first tie", LogSeverity.Information),
            Entry(6, "WARN second tie", LogSeverity.Warning),
            Entry(7, "ERROR larger group", LogSeverity.Error)
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        CollectionAssert.AreEqual(
            new[] { "ERROR larger group", "INFO first tie", "WARN second tie" },
            result.TopRepeatedMessages.Select(finding => finding.RawText).ToArray());
    }

    [TestMethod]
    public void Analyze_RepeatedFindingsRespectTopNLimit()
    {
        var entries = new List<ParsedLogEntry>();
        int line = 1;
        for (int group = 0; group < PatternAnalysisPolicy.MaximumRepeatedMessageFindings + 5; group++)
        {
            string rawText = $"INFO repeated group {group:D2}";
            entries.Add(Entry(line++, rawText, LogSeverity.Information));
            entries.Add(Entry(line++, rawText, LogSeverity.Information));
        }

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.AreEqual(PatternAnalysisPolicy.MaximumRepeatedMessageFindings + 5, result.TotalRepeatedMessagePatterns);
        Assert.HasCount(PatternAnalysisPolicy.MaximumRepeatedMessageFindings, result.TopRepeatedMessages);
        Assert.AreEqual("INFO repeated group 00", result.TopRepeatedMessages[0].RawText);
    }

    [TestMethod]
    public void Analyze_SeverityLeaderRemainsAvailableWhenOutsideOverallTopN()
    {
        var entries = new List<ParsedLogEntry>();
        int line = 1;
        for (int group = 0; group < PatternAnalysisPolicy.MaximumRepeatedMessageFindings + 1; group++)
        {
            string rawText = $"INFO frequent {group:D2}";
            for (int repeat = 0; repeat < 3; repeat++)
            {
                entries.Add(Entry(line++, rawText, LogSeverity.Information));
            }
        }

        entries.Add(Entry(line++, "ERROR category leader", LogSeverity.Error));
        entries.Add(Entry(line, "ERROR category leader", LogSeverity.Error));

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.IsFalse(result.TopRepeatedMessages.Any(finding => finding.SeverityGroup == PatternSeverityGroup.Error));
        Assert.IsTrue(result.RepeatedSeverityLeaders.Any(finding =>
            finding.SeverityGroup == PatternSeverityGroup.Error
            && finding.RawText == "ERROR category leader"));
    }

    [TestMethod]
    public void Analyze_NoTimestampsProducesNoTimeFindings()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            Entry(1, "ERROR one", LogSeverity.Error),
            Entry(2, "ERROR two", LogSeverity.Error),
            Entry(3, "ERROR three", LogSeverity.Error)
        ]);

        Assert.IsFalse(result.TimeAnalysisStatus.IsAvailable);
        Assert.HasCount(0, result.SeverityBursts);
        Assert.HasCount(0, result.ActivityWindows);
        StringAssert.Contains(result.TimeAnalysisStatus.Explanation, "enough recognised timestamps");
    }

    [TestMethod]
    public void Analyze_TooFewSevereEventsDoesNotCreateBurst()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "ERROR first", LogSeverity.Error, At(0)),
            TimedEntry(2, "CRITICAL second", LogSeverity.Critical, At(30)),
            TimedEntry(3, "INFO context", LogSeverity.Information, At(40))
        ]);

        Assert.IsTrue(result.TimeAnalysisStatus.IsAvailable);
        Assert.HasCount(0, result.SeverityBursts);
    }

    [TestMethod]
    public void Analyze_ErrorCriticalBurstIncludesEventsExactlyAtSixtySecondThreshold()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "ERROR first", LogSeverity.Error, At(0)),
            TimedEntry(2, "CRITICAL second", LogSeverity.Critical, At(30)),
            TimedEntry(3, "ERROR third", LogSeverity.Error, At(60))
        ]);

        Assert.HasCount(1, result.SeverityBursts);
        SeverityBurstFinding finding = result.SeverityBursts[0];
        Assert.AreEqual(3, finding.OccurrenceCount);
        Assert.AreEqual(TimeSpan.FromSeconds(60), finding.TimeRange.End - finding.TimeRange.Start);
    }

    [TestMethod]
    public void Analyze_EventsInsideThresholdCreateBurst()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "ERROR first", LogSeverity.Error, At(10)),
            TimedEntry(2, "ERROR second", LogSeverity.Error, At(20)),
            TimedEntry(3, "ERROR third", LogSeverity.Error, At(25))
        ]);

        Assert.HasCount(1, result.SeverityBursts);
        Assert.AreEqual(3, result.SeverityBursts[0].OccurrenceCount);
    }

    [TestMethod]
    public void Analyze_EventsOutsideThresholdDoNotCreateBurst()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "ERROR first", LogSeverity.Error, At(0)),
            TimedEntry(2, "ERROR second", LogSeverity.Error, At(30)),
            TimedEntry(3, "ERROR third", LogSeverity.Error, At(61))
        ]);

        Assert.HasCount(0, result.SeverityBursts);
    }

    [TestMethod]
    public void Analyze_ErrorAndCriticalCombineWhileInfoAndDebugAreIgnored()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "INFO context", LogSeverity.Information, At(0)),
            TimedEntry(2, "ERROR first", LogSeverity.Error, At(5)),
            TimedEntry(3, "DEBUG context", LogSeverity.Debug, At(6)),
            TimedEntry(4, "CRITICAL second", LogSeverity.Critical, At(10)),
            TimedEntry(5, "ERROR third", LogSeverity.Error, At(20))
        ]);

        SeverityBurstFinding finding = result.SeverityBursts.Single();
        Assert.AreEqual(PatternSeverityGroup.ErrorCritical, finding.SeverityGroup);
        Assert.AreEqual(3, finding.OccurrenceCount);
        CollectionAssert.AreEqual(
            new[] { 2, 4, 5 },
            finding.Evidence.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Analyze_WarningBurstRequiresFourWarnings()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "WARN one", LogSeverity.Warning, At(0)),
            TimedEntry(2, "WARN two", LogSeverity.Warning, At(10)),
            TimedEntry(3, "WARN three", LogSeverity.Warning, At(20)),
            TimedEntry(4, "WARN four", LogSeverity.Warning, At(60))
        ]);

        SeverityBurstFinding finding = result.SeverityBursts.Single();
        Assert.AreEqual(PatternSeverityGroup.Warning, finding.SeverityGroup);
        Assert.AreEqual(4, finding.OccurrenceCount);
    }

    [TestMethod]
    public void Analyze_MultipleSeparatedBurstsAreReportedSeparately()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "ERROR a", LogSeverity.Error, At(0)),
            TimedEntry(2, "ERROR b", LogSeverity.Error, At(10)),
            TimedEntry(3, "ERROR c", LogSeverity.Error, At(20)),
            TimedEntry(4, "ERROR d", LogSeverity.Error, At(180)),
            TimedEntry(5, "CRITICAL e", LogSeverity.Critical, At(190)),
            TimedEntry(6, "ERROR f", LogSeverity.Error, At(200))
        ]);

        Assert.AreEqual(2, result.TotalSeverityBurstCount);
        Assert.HasCount(2, result.SeverityBursts);
    }

    [TestMethod]
    public void Analyze_TimestampSortingDoesNotChangeInputOrEvidenceLineOrder()
    {
        ParsedLogEntry[] entries =
        [
            TimedEntry(1, "ERROR third in time", LogSeverity.Error, At(20)),
            TimedEntry(2, "ERROR first in time", LogSeverity.Error, At(0)),
            TimedEntry(3, "CRITICAL second in time", LogSeverity.Critical, At(10))
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, entries.Select(entry => entry.LineNumber).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3 },
            result.SeverityBursts[0].Evidence.Entries.Select(entry => entry.LineNumber).ToArray());
        Assert.AreEqual(At(0), result.SeverityBursts[0].TimeRange.Start);
        Assert.AreEqual(At(20), result.SeverityBursts[0].TimeRange.End);
    }

    [TestMethod]
    public void Analyze_DuplicateTimestampsCountAsDistinctEvidence()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "ERROR one", LogSeverity.Error, At(0)),
            TimedEntry(2, "ERROR two", LogSeverity.Error, At(0)),
            TimedEntry(3, "CRITICAL three", LogSeverity.Critical, At(0))
        ]);

        SeverityBurstFinding finding = result.SeverityBursts.Single();
        Assert.AreEqual(3, finding.OccurrenceCount);
        Assert.AreEqual(finding.TimeRange.Start, finding.TimeRange.End);
    }

    [TestMethod]
    public void Analyze_SparseSevereTimestampsDoNotCreateBurst()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "ERROR one", LogSeverity.Error, At(0)),
            TimedEntry(2, "ERROR two", LogSeverity.Error, At(120)),
            TimedEntry(3, "CRITICAL three", LogSeverity.Critical, At(240))
        ]);

        Assert.HasCount(0, result.SeverityBursts);
        Assert.IsTrue(result.TimeAnalysisStatus.IsAvailable);
    }

    [TestMethod]
    public void Analyze_BurstFindingsAreBoundedWhileTotalRemainsAccurate()
    {
        var entries = new List<ParsedLogEntry>();
        int line = 1;
        for (int burst = 0; burst < PatternAnalysisPolicy.MaximumSeverityBurstFindings + 3; burst++)
        {
            int startSeconds = burst * 120;
            entries.Add(TimedEntry(line++, $"ERROR {line}a", LogSeverity.Error, At(startSeconds)));
            entries.Add(TimedEntry(line++, $"ERROR {line}b", LogSeverity.Error, At(startSeconds + 1)));
            entries.Add(TimedEntry(line++, $"ERROR {line}c", LogSeverity.Error, At(startSeconds + 2)));
        }

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.AreEqual(PatternAnalysisPolicy.MaximumSeverityBurstFindings + 3, result.TotalSeverityBurstCount);
        Assert.HasCount(PatternAnalysisPolicy.MaximumSeverityBurstFindings, result.SeverityBursts);
    }

    [TestMethod]
    public void Analyze_EmptyLogHasNoActivityWindows()
    {
        PatternAnalysisResult result = _service.Analyze([]);

        Assert.AreEqual(0, result.EntriesAnalyzed);
        Assert.HasCount(0, result.ActivityWindows);
        Assert.IsFalse(result.TimeAnalysisStatus.IsAvailable);
    }

    [TestMethod]
    public void Analyze_OneTimestampIsInsufficientForTimeAnalysis()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "INFO one", LogSeverity.Information, At(0))
        ]);

        Assert.IsFalse(result.TimeAnalysisStatus.IsAvailable);
        Assert.HasCount(0, result.ActivityWindows);
    }

    [TestMethod]
    public void Analyze_SeveralTimestampsCreateBusiestMinuteAndHour()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "INFO one", LogSeverity.Information, At(0)),
            TimedEntry(2, "INFO two", LogSeverity.Information, At(10)),
            TimedEntry(3, "WARN three", LogSeverity.Warning, At(70))
        ]);

        Assert.IsTrue(result.TimeAnalysisStatus.IsAvailable);
        Assert.IsTrue(result.ActivityWindows.Any(window => window.Type == ActivityWindowType.BusiestMinute));
        Assert.IsTrue(result.ActivityWindows.Any(window => window.Type == ActivityWindowType.BusiestHour));
    }

    [TestMethod]
    public void Analyze_BusiestMinuteUsesAccurateCountAndEarliestTieBreak()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "INFO one", LogSeverity.Information, At(65)),
            TimedEntry(2, "INFO two", LogSeverity.Information, At(70)),
            TimedEntry(3, "INFO three", LogSeverity.Information, At(5)),
            TimedEntry(4, "INFO four", LogSeverity.Information, At(15))
        ]);

        ActivityWindowFinding minute = result.ActivityWindows.Single(window =>
            window.Type == ActivityWindowType.BusiestMinute);
        Assert.AreEqual(2, minute.OccurrenceCount);
        Assert.AreEqual(At(0), minute.Window.Start);
        CollectionAssert.AreEqual(
            new[] { 3, 4 },
            minute.Evidence.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Analyze_BusiestHourUsesAccurateCount()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "INFO one", LogSeverity.Information, At(0)),
            TimedEntry(2, "INFO two", LogSeverity.Information, At(1800)),
            TimedEntry(3, "INFO three", LogSeverity.Information, At(3700))
        ]);

        ActivityWindowFinding hour = result.ActivityWindows.Single(window =>
            window.Type == ActivityWindowType.BusiestHour);
        Assert.AreEqual(2, hour.OccurrenceCount);
        Assert.AreEqual(new DateTime(2026, 8, 14, 10, 0, 0), hour.Window.Start);
    }

    [TestMethod]
    public void Analyze_MostErrorCriticalMinuteUsesOnlyThoseSeverities()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimedEntry(1, "INFO context", LogSeverity.Information, At(0)),
            TimedEntry(2, "ERROR one", LogSeverity.Error, At(10)),
            TimedEntry(3, "CRITICAL two", LogSeverity.Critical, At(20)),
            TimedEntry(4, "WARN context", LogSeverity.Warning, At(25))
        ]);

        ActivityWindowFinding severeMinute = result.ActivityWindows.Single(window =>
            window.Type == ActivityWindowType.MostErrorCriticalMinute);
        Assert.AreEqual(2, severeMinute.OccurrenceCount);
        CollectionAssert.AreEqual(
            new[] { 2, 3 },
            severeMinute.Evidence.Entries.Select(entry => entry.LineNumber).ToArray());
    }

    [TestMethod]
    public void Analyze_PartialTimestampCoverageIsDisclosedAccurately()
    {
        ParsedLogEntry[] entries =
        [
            TimedEntry(1, "INFO full one", LogSeverity.Information, At(0)),
            TimedEntry(2, "INFO full two", LogSeverity.Information, At(10)),
            TimeOnlyEntry(3, "INFO time only", LogSeverity.Information, new TimeOnly(10, 0, 20)),
            Entry(4, "INFO no timestamp", LogSeverity.Information)
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.IsTrue(result.TimeAnalysisStatus.IsAvailable);
        Assert.AreEqual(3, result.TimeAnalysisStatus.RecognizedTimestampEntries);
        Assert.AreEqual(2, result.TimeAnalysisStatus.ComparableTimestampEntries);
        Assert.AreEqual(1, result.TimeAnalysisStatus.ExcludedTimestampEntries);
        StringAssert.Contains(result.TimeAnalysisStatus.Explanation, "excluded");
    }

    [TestMethod]
    public void Analyze_OnlyTimeOnlyTimestampsAreNotUsedForTimeWindows()
    {
        PatternAnalysisResult result = _service.Analyze(
        [
            TimeOnlyEntry(1, "ERROR one", LogSeverity.Error, new TimeOnly(10, 0, 0)),
            TimeOnlyEntry(2, "ERROR two", LogSeverity.Error, new TimeOnly(10, 0, 10)),
            TimeOnlyEntry(3, "ERROR three", LogSeverity.Error, new TimeOnly(10, 0, 20))
        ]);

        Assert.IsFalse(result.TimeAnalysisStatus.IsAvailable);
        Assert.HasCount(0, result.SeverityBursts);
        Assert.HasCount(0, result.ActivityWindows);
    }

    [TestMethod]
    public void Analyze_ExplicitOffsetsAreNormalisedToUtcForComparableAnalysis()
    {
        ParsedLogEntry[] entries =
        [
            OffsetEntry(1, "INFO offset", LogSeverity.Information, new DateTime(2026, 8, 14, 11, 0, 0), TimeSpan.FromHours(1)),
            OffsetEntry(2, "INFO utc", LogSeverity.Information, new DateTime(2026, 8, 14, 10, 0, 30), TimeSpan.Zero)
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.IsTrue(result.TimeAnalysisStatus.IsAvailable);
        Assert.AreEqual(PatternTimeBasis.Utc, result.TimeAnalysisStatus.Basis);
        ActivityWindowFinding minute = result.ActivityWindows.Single(window =>
            window.Type == ActivityWindowType.BusiestMinute);
        Assert.AreEqual(2, minute.OccurrenceCount);
        Assert.AreEqual(DateTimeKind.Utc, minute.Window.Start.Kind);
    }

    [TestMethod]
    public void Analyze_MixedTimeBasesUseLargestComparableCohortAndDiscloseExclusions()
    {
        ParsedLogEntry[] entries =
        [
            TimedEntry(1, "INFO wall one", LogSeverity.Information, At(0)),
            TimedEntry(2, "INFO wall two", LogSeverity.Information, At(10)),
            TimedEntry(3, "INFO wall three", LogSeverity.Information, At(20)),
            OffsetEntry(4, "INFO offset", LogSeverity.Information, At(30), TimeSpan.Zero)
        ];

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.AreEqual(PatternTimeBasis.WallClock, result.TimeAnalysisStatus.Basis);
        Assert.AreEqual(3, result.TimeAnalysisStatus.ComparableTimestampEntries);
        Assert.AreEqual(1, result.TimeAnalysisStatus.ExcludedTimestampEntries);
    }

    [TestMethod]
    public void Analyze_EvidenceUsesCorrectOriginalReferencesAndLineNumbers()
    {
        ParsedLogEntry[] entries =
        [
            Entry(10, "ERROR exact", LogSeverity.Error),
            Entry(20, "ERROR exact", LogSeverity.Error)
        ];

        RepeatedMessageFinding finding = _service.Analyze(entries).TopRepeatedMessages[0];

        CollectionAssert.AreEqual(
            new[] { 10, 20 },
            finding.Evidence.Entries.Select(entry => entry.LineNumber).ToArray());
        Assert.AreSame(entries[0], finding.Evidence.Entries[0]);
        Assert.AreSame(entries[1], finding.Evidence.Entries[1]);
    }

    [TestMethod]
    public void Analyze_CommandAndUrlEvidenceRemainUnchangedAndInert()
    {
        const string rawText = "ERROR powershell.exe -Command \"Write-Output https://example.invalid\"";
        ParsedLogEntry[] entries =
        [
            Entry(1, rawText, LogSeverity.Error),
            Entry(2, rawText, LogSeverity.Error)
        ];

        RepeatedMessageFinding finding = _service.Analyze(entries).TopRepeatedMessages[0];

        Assert.AreEqual(rawText, finding.RawText);
        Assert.AreEqual(rawText, finding.Evidence.Entries[0].RawText);
        Assert.AreSame(entries[0], finding.Evidence.Entries[0]);
    }

    [TestMethod]
    public void Analyze_DoesNotMutateParsedEntriesOrRawText()
    {
        ParsedLogEntry[] entries =
        [
            TimedEntry(1, "ERROR same", LogSeverity.Error, At(0)),
            TimedEntry(2, "ERROR same", LogSeverity.Error, At(10)),
            TimedEntry(3, "ERROR same", LogSeverity.Error, At(20))
        ];
        ParsedLogEntry[] before = entries.ToArray();

        _ = _service.Analyze(entries);

        CollectionAssert.AreEqual(before, entries);
        for (int index = 0; index < entries.Length; index++)
        {
            Assert.AreSame(before[index], entries[index]);
            Assert.AreEqual(before[index].RawText, entries[index].RawText);
            Assert.AreEqual(before[index].Timestamp, entries[index].Timestamp);
        }
    }

    [TestMethod]
    public void Analyze_LargeSyntheticSetCompletesWithBoundedFindings()
    {
        ParsedLogEntry[] entries = Enumerable.Range(1, 100_000)
            .Select(line => Entry(line, $"INFO unique synthetic {line}", LogSeverity.Information))
            .ToArray();

        PatternAnalysisResult result = _service.Analyze(entries);

        Assert.AreEqual(100_000, result.EntriesAnalyzed);
        Assert.AreEqual(0, result.TotalRepeatedMessagePatterns);
        Assert.IsLessThanOrEqualTo(
            PatternAnalysisPolicy.MaximumRepeatedMessageFindings,
            result.TopRepeatedMessages.Count);
        Assert.IsLessThanOrEqualTo(
            PatternAnalysisPolicy.MaximumSeverityBurstFindings,
            result.SeverityBursts.Count);
    }

    [TestMethod]
    public void Analyze_ObservesCancellationDuringInputScan()
    {
        using var cancellation = new CancellationTokenSource();
        var entries = new CancellingEntryList(
            Enumerable.Range(1, 20)
                .Select(line => Entry(line, $"INFO {line}", LogSeverity.Information))
                .ToArray(),
            cancellation,
            cancelBeforeIndex: 5);

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            _service.Analyze(entries, cancellation.Token));
    }

    [TestMethod]
    public void Analyze_IsDeterministicAcrossRepeatedRuns()
    {
        ParsedLogEntry[] entries =
        [
            TimedEntry(1, "ERROR same", LogSeverity.Error, At(0)),
            TimedEntry(2, "ERROR same", LogSeverity.Error, At(10)),
            TimedEntry(3, "ERROR same", LogSeverity.Error, At(20)),
            TimedEntry(4, "WARN other", LogSeverity.Warning, At(30))
        ];

        PatternAnalysisResult first = _service.Analyze(entries);
        PatternAnalysisResult second = _service.Analyze(entries);

        CollectionAssert.AreEqual(
            first.TopRepeatedMessages.Select(finding => (finding.RawText, finding.OccurrenceCount)).ToArray(),
            second.TopRepeatedMessages.Select(finding => (finding.RawText, finding.OccurrenceCount)).ToArray());
        CollectionAssert.AreEqual(
            first.SeverityBursts.Select(finding => (finding.TimeRange.Start, finding.OccurrenceCount)).ToArray(),
            second.SeverityBursts.Select(finding => (finding.TimeRange.Start, finding.OccurrenceCount)).ToArray());
        CollectionAssert.AreEqual(
            first.ActivityWindows.Select(finding => (finding.Type, finding.Window.Start, finding.OccurrenceCount)).ToArray(),
            second.ActivityWindows.Select(finding => (finding.Type, finding.Window.Start, finding.OccurrenceCount)).ToArray());
    }

    [TestMethod]
    public void PatternService_PublicContractHasNoPathStreamOrFileParameters()
    {
        MethodInfo analyzeMethod = typeof(IPatternAnalysisService).GetMethod(
            nameof(IPatternAnalysisService.Analyze))!;
        Type[] parameterTypes = analyzeMethod.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { typeof(IReadOnlyList<ParsedLogEntry>), typeof(CancellationToken) },
            parameterTypes);
        Assert.AreEqual(typeof(PatternAnalysisResult), analyzeMethod.ReturnType);
    }

    private static ParsedLogEntry Entry(
        int lineNumber,
        string rawText,
        LogSeverity severity) => new(
            lineNumber,
            rawText,
            severity,
            null,
            rawText);

    private static ParsedLogEntry TimedEntry(
        int lineNumber,
        string rawText,
        LogSeverity severity,
        DateTime timestamp) => new(
            lineNumber,
            rawText,
            severity,
            new ParsedLogTimestamp(
                timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                DateOnly.FromDateTime(timestamp),
                TimeOnly.FromDateTime(timestamp),
                null),
            rawText);

    private static ParsedLogEntry OffsetEntry(
        int lineNumber,
        string rawText,
        LogSeverity severity,
        DateTime timestamp,
        TimeSpan offset) => new(
            lineNumber,
            rawText,
            severity,
            new ParsedLogTimestamp(
                timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                DateOnly.FromDateTime(timestamp),
                TimeOnly.FromDateTime(timestamp),
                offset),
            rawText);

    private static ParsedLogEntry TimeOnlyEntry(
        int lineNumber,
        string rawText,
        LogSeverity severity,
        TimeOnly time) => new(
            lineNumber,
            rawText,
            severity,
            new ParsedLogTimestamp(time.ToString("HH:mm:ss"), null, time, null),
            rawText);

    private static DateTime At(int secondsAfterTen) =>
        new DateTime(2026, 8, 14, 10, 0, 0).AddSeconds(secondsAfterTen);

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
