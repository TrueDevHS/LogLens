using LogLens.Core.Parsing;

namespace LogLens.Core.Patterns;

public sealed class PatternAnalysisService : IPatternAnalysisService
{
    private static readonly LogSeverity[] SeverityDisplayOrder =
    [
        LogSeverity.Trace,
        LogSeverity.Debug,
        LogSeverity.Information,
        LogSeverity.Warning,
        LogSeverity.Error,
        LogSeverity.Critical,
        LogSeverity.Unknown
    ];

    public PatternAnalysisResult Analyze(
        IReadOnlyList<ParsedLogEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        RepeatedAnalysis repeated = AnalyzeRepeatedMessages(entries, cancellationToken);
        IReadOnlyList<SeverityFrequency> severityDistribution = BuildSeverityDistribution(
            entries,
            cancellationToken);
        TimeSelection timeSelection = SelectComparableTimestamps(entries, cancellationToken);

        BurstAnalysis bursts = timeSelection.Status.IsAvailable
            ? AnalyzeBursts(timeSelection.Entries, cancellationToken)
            : BurstAnalysis.Empty;
        IReadOnlyList<ActivityWindowFinding> activityWindows = timeSelection.Status.IsAvailable
            ? AnalyzeActivityWindows(timeSelection.Entries, cancellationToken)
            : [];

        return new PatternAnalysisResult(
            entries.Count,
            repeated.TopFindings,
            repeated.SeverityLeaders,
            repeated.TotalPatternCount,
            bursts.Findings,
            bursts.TotalFindingCount,
            activityWindows,
            severityDistribution,
            timeSelection.Status);
    }

    private static RepeatedAnalysis AnalyzeRepeatedMessages(
        IReadOnlyList<ParsedLogEntry> entries,
        CancellationToken cancellationToken)
    {
        var accumulators = new Dictionary<string, RepeatAccumulator>(StringComparer.Ordinal);

        foreach (ParsedLogEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.RawText))
            {
                continue;
            }

            if (accumulators.TryGetValue(entry.RawText, out RepeatAccumulator? accumulator))
            {
                accumulator.Add(entry);
            }
            else
            {
                accumulators.Add(entry.RawText, new RepeatAccumulator(entry));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        RepeatAccumulator[] candidates = accumulators.Values
            .Where(accumulator =>
                accumulator.Count >= PatternAnalysisPolicy.MinimumRepeatedMessageOccurrences)
            .OrderByDescending(accumulator => accumulator.Count)
            .ThenBy(accumulator => accumulator.FirstOccurrence.LineNumber)
            .ThenBy(accumulator => accumulator.RawText, StringComparer.Ordinal)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        RepeatAccumulator[] topCandidates = candidates
            .Take(PatternAnalysisPolicy.MaximumRepeatedMessageFindings)
            .ToArray();

        PatternSeverityGroup[] leaderGroups =
        [
            PatternSeverityGroup.Trace,
            PatternSeverityGroup.Debug,
            PatternSeverityGroup.Information,
            PatternSeverityGroup.Warning,
            PatternSeverityGroup.Error,
            PatternSeverityGroup.Critical,
            PatternSeverityGroup.Unknown
        ];
        RepeatAccumulator[] leaderCandidates = leaderGroups
            .Select(group => candidates.FirstOrDefault(candidate => candidate.SeverityGroup == group))
            .Where(candidate => candidate is not null)
            .Cast<RepeatAccumulator>()
            .ToArray();

        var findingCache = new Dictionary<RepeatAccumulator, RepeatedMessageFinding>();
        RepeatedMessageFinding GetFinding(RepeatAccumulator candidate)
        {
            if (!findingCache.TryGetValue(candidate, out RepeatedMessageFinding? finding))
            {
                finding = CreateRepeatedFinding(candidate);
                findingCache.Add(candidate, finding);
            }

            return finding;
        }

        return new RepeatedAnalysis(
            topCandidates.Select(GetFinding).ToArray(),
            leaderCandidates.Select(GetFinding).ToArray(),
            candidates.Length);
    }

    private static RepeatedMessageFinding CreateRepeatedFinding(RepeatAccumulator candidate)
    {
        string severityLabel = GetSeverityGroupLabel(candidate.SeverityGroup);
        return new RepeatedMessageFinding(
            $"Repeated {severityLabel} message",
            $"The exact raw line occurs {candidate.Count:N0} times. Matching is case-sensitive and does not remove timestamps, identifiers, numbers, paths or surrounding whitespace.",
            candidate.RawText,
            candidate.SeverityGroup,
            candidate.Count,
            candidate.FirstOccurrence,
            candidate.LastOccurrence,
            CreateEvidence(candidate.Occurrences, candidate.Count));
    }

    private static IReadOnlyList<SeverityFrequency> BuildSeverityDistribution(
        IReadOnlyList<ParsedLogEntry> entries,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<LogSeverity, int>();
        foreach (ParsedLogEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts[entry.Severity] = counts.GetValueOrDefault(entry.Severity) + 1;
        }

        return SeverityDisplayOrder
            .Where(counts.ContainsKey)
            .Select(severity => new SeverityFrequency(severity, counts[severity]))
            .ToArray();
    }

    private static TimeSelection SelectComparableTimestamps(
        IReadOnlyList<ParsedLogEntry> entries,
        CancellationToken cancellationToken)
    {
        var wallClockEntries = new List<TimedEntry>();
        var utcEntries = new List<TimedEntry>();
        int recognizedTimestamps = 0;

        foreach (ParsedLogEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParsedLogTimestamp? timestamp = entry.Timestamp;
            if (timestamp is null)
            {
                continue;
            }

            recognizedTimestamps++;
            if (timestamp.Date is not DateOnly date)
            {
                continue;
            }

            DateTime wallClock = date.ToDateTime(timestamp.Time, DateTimeKind.Unspecified);
            if (timestamp.UtcOffset is TimeSpan offset)
            {
                try
                {
                    DateTime utc = new DateTimeOffset(wallClock, offset).UtcDateTime;
                    utcEntries.Add(new TimedEntry(entry, utc));
                }
                catch (ArgumentException)
                {
                    // A manually constructed invalid offset is excluded rather than guessed.
                }
            }
            else
            {
                wallClockEntries.Add(new TimedEntry(entry, wallClock));
            }
        }

        List<TimedEntry> selected;
        PatternTimeBasis? basis;
        if (utcEntries.Count > wallClockEntries.Count)
        {
            selected = utcEntries;
            basis = PatternTimeBasis.Utc;
        }
        else if (wallClockEntries.Count > 0)
        {
            selected = wallClockEntries;
            basis = PatternTimeBasis.WallClock;
        }
        else
        {
            selected = utcEntries;
            basis = utcEntries.Count > 0 ? PatternTimeBasis.Utc : null;
        }

        selected.Sort(static (left, right) =>
        {
            int timestampComparison = left.Timestamp.CompareTo(right.Timestamp);
            return timestampComparison != 0
                ? timestampComparison
                : left.Entry.LineNumber.CompareTo(right.Entry.LineNumber);
        });

        int excluded = recognizedTimestamps - selected.Count;
        bool isAvailable = selected.Count >= 2;
        string explanation;
        if (!isAvailable && recognizedTimestamps < 2)
        {
            explanation = "Time-based analysis is unavailable because this log does not contain enough recognised timestamps.";
        }
        else if (!isAvailable)
        {
            explanation = "Time-based analysis is unavailable because fewer than two recognised timestamps share a comparable dated time basis.";
        }
        else
        {
            string basisText = basis == PatternTimeBasis.Utc
                ? "explicit-offset timestamps normalised to UTC"
                : "dated timestamps without an explicit offset";
            explanation = excluded == 0
                ? $"Time analysis used {selected.Count:N0} {basisText}."
                : $"Time analysis used {selected.Count:N0} {basisText}; {excluded:N0} recognised timestamps with a different or time-only basis were excluded.";
        }

        var status = new PatternTimeAnalysisStatus(
            isAvailable,
            recognizedTimestamps,
            selected.Count,
            excluded,
            basis,
            explanation);
        return new TimeSelection(selected.ToArray(), status);
    }

    private static BurstAnalysis AnalyzeBursts(
        IReadOnlyList<TimedEntry> timedEntries,
        CancellationToken cancellationToken)
    {
        BurstAnalysis errorCritical = DetectBursts(
            timedEntries.Where(entry =>
                entry.Entry.Severity is LogSeverity.Error or LogSeverity.Critical).ToArray(),
            PatternSeverityGroup.ErrorCritical,
            PatternAnalysisPolicy.ErrorCriticalBurstMinimumEntries,
            cancellationToken);
        BurstAnalysis warnings = DetectBursts(
            timedEntries.Where(entry => entry.Entry.Severity == LogSeverity.Warning).ToArray(),
            PatternSeverityGroup.Warning,
            PatternAnalysisPolicy.WarningBurstMinimumEntries,
            cancellationToken);

        SeverityBurstFinding[] shownFindings = errorCritical.Findings
            .Concat(warnings.Findings)
            .OrderBy(finding => finding.TimeRange.Start)
            .ThenBy(finding => finding.SeverityGroup)
            .Take(PatternAnalysisPolicy.MaximumSeverityBurstFindings)
            .ToArray();

        return new BurstAnalysis(
            shownFindings,
            errorCritical.TotalFindingCount + warnings.TotalFindingCount);
    }

    private static BurstAnalysis DetectBursts(
        IReadOnlyList<TimedEntry> entries,
        PatternSeverityGroup severityGroup,
        int minimumEntries,
        CancellationToken cancellationToken)
    {
        var findings = new List<SeverityBurstFinding>();
        int totalFindings = 0;
        int start = 0;

        while (start < entries.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int endExclusive = start + 1;
            while (endExclusive < entries.Count
                   && entries[endExclusive].Timestamp - entries[start].Timestamp
                   <= PatternAnalysisPolicy.SeverityBurstWindow)
            {
                endExclusive++;
            }

            int count = endExclusive - start;
            if (count < minimumEntries)
            {
                start++;
                continue;
            }

            totalFindings++;
            if (findings.Count < PatternAnalysisPolicy.MaximumSeverityBurstFindings)
            {
                TimedEntry[] burstEntries = entries
                    .Skip(start)
                    .Take(count)
                    .ToArray();
                ParsedLogEntry[] evidenceEntries = burstEntries
                    .Select(entry => entry.Entry)
                    .OrderBy(entry => entry.LineNumber)
                    .ToArray();
                string label = severityGroup == PatternSeverityGroup.Warning
                    ? "Warning activity burst"
                    : "Error/Critical activity burst";
                findings.Add(new SeverityBurstFinding(
                    label,
                    $"{count:N0} {GetSeverityGroupLabel(severityGroup)} entries occurred within an inclusive 60-second window.",
                    severityGroup,
                    count,
                    new PatternTimeRange(
                        burstEntries[0].Timestamp,
                        burstEntries[^1].Timestamp,
                        burstEntries[0].Timestamp.Kind == DateTimeKind.Utc
                            ? PatternTimeBasis.Utc
                            : PatternTimeBasis.WallClock),
                    CreateEvidence(evidenceEntries, count)));
            }

            start = endExclusive;
        }

        return new BurstAnalysis(findings.ToArray(), totalFindings);
    }

    private static IReadOnlyList<ActivityWindowFinding> AnalyzeActivityWindows(
        IReadOnlyList<TimedEntry> timedEntries,
        CancellationToken cancellationToken)
    {
        var findings = new List<ActivityWindowFinding>(3);
        BucketAccumulator busiestMinute = FindBusiestBucket(
            timedEntries,
            TimeSpan.FromMinutes(1),
            cancellationToken);
        findings.Add(CreateActivityFinding(
            busiestMinute,
            ActivityWindowType.BusiestMinute,
            "Busiest minute",
            "The recognised minute containing the most comparable timestamped entries."));

        BucketAccumulator busiestHour = FindBusiestBucket(
            timedEntries,
            TimeSpan.FromHours(1),
            cancellationToken);
        findings.Add(CreateActivityFinding(
            busiestHour,
            ActivityWindowType.BusiestHour,
            "Busiest hour",
            "The recognised hour containing the most comparable timestamped entries."));

        TimedEntry[] errorCriticalEntries = timedEntries
            .Where(entry => entry.Entry.Severity is LogSeverity.Error or LogSeverity.Critical)
            .ToArray();
        if (errorCriticalEntries.Length > 0)
        {
            BucketAccumulator busiestSevereMinute = FindBusiestBucket(
                errorCriticalEntries,
                TimeSpan.FromMinutes(1),
                cancellationToken);
            findings.Add(CreateActivityFinding(
                busiestSevereMinute,
                ActivityWindowType.MostErrorCriticalMinute,
                "Most Error/Critical-heavy minute",
                "The recognised minute containing the most Error or Critical entries."));
        }

        return findings.ToArray();
    }

    private static BucketAccumulator FindBusiestBucket(
        IReadOnlyList<TimedEntry> timedEntries,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var buckets = new Dictionary<DateTime, List<ParsedLogEntry>>();

        foreach (TimedEntry timedEntry in timedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime timestamp = timedEntry.Timestamp;
            DateTime start = duration == TimeSpan.FromHours(1)
                ? new DateTime(
                    timestamp.Year,
                    timestamp.Month,
                    timestamp.Day,
                    timestamp.Hour,
                    0,
                    0,
                    timestamp.Kind)
                : new DateTime(
                    timestamp.Year,
                    timestamp.Month,
                    timestamp.Day,
                    timestamp.Hour,
                    timestamp.Minute,
                    0,
                    timestamp.Kind);

            if (!buckets.TryGetValue(start, out List<ParsedLogEntry>? bucketEntries))
            {
                bucketEntries = [];
                buckets.Add(start, bucketEntries);
            }

            bucketEntries.Add(timedEntry.Entry);
        }

        cancellationToken.ThrowIfCancellationRequested();
        KeyValuePair<DateTime, List<ParsedLogEntry>> busiest = buckets
            .OrderByDescending(bucket => bucket.Value.Count)
            .ThenBy(bucket => bucket.Key)
            .First();
        return new BucketAccumulator(busiest.Key, duration, busiest.Value.ToArray());
    }

    private static ActivityWindowFinding CreateActivityFinding(
        BucketAccumulator bucket,
        ActivityWindowType type,
        string title,
        string explanation) => new(
            type,
            title,
            $"{explanation} It contains {bucket.Entries.Count:N0} entries.",
            bucket.Entries.Count,
            new PatternActivityWindow(
                bucket.Start,
                bucket.Duration,
                bucket.Start.Kind == DateTimeKind.Utc
                    ? PatternTimeBasis.Utc
                    : PatternTimeBasis.WallClock),
            CreateEvidence(bucket.Entries, bucket.Entries.Count));

    private static PatternEvidence CreateEvidence(
        IReadOnlyList<ParsedLogEntry> entries,
        int totalEntryCount) => new(
            entries.Take(PatternAnalysisPolicy.MaximumEvidenceEntriesPerFinding).ToArray(),
            totalEntryCount);

    private static PatternSeverityGroup GetSeverityGroup(LogSeverity severity) => severity switch
    {
        LogSeverity.Trace => PatternSeverityGroup.Trace,
        LogSeverity.Debug => PatternSeverityGroup.Debug,
        LogSeverity.Information => PatternSeverityGroup.Information,
        LogSeverity.Warning => PatternSeverityGroup.Warning,
        LogSeverity.Error => PatternSeverityGroup.Error,
        LogSeverity.Critical => PatternSeverityGroup.Critical,
        _ => PatternSeverityGroup.Unknown
    };

    private static string GetSeverityGroupLabel(PatternSeverityGroup severityGroup) => severityGroup switch
    {
        PatternSeverityGroup.Trace => "Trace",
        PatternSeverityGroup.Debug => "Debug",
        PatternSeverityGroup.Information => "Information",
        PatternSeverityGroup.Warning => "Warning",
        PatternSeverityGroup.Error => "Error",
        PatternSeverityGroup.Critical => "Critical/Fatal",
        PatternSeverityGroup.ErrorCritical => "Error/Critical",
        PatternSeverityGroup.Mixed => "mixed-severity",
        _ => "unclassified"
    };

    private sealed class RepeatAccumulator
    {
        private List<ParsedLogEntry>? _occurrences;

        public RepeatAccumulator(ParsedLogEntry firstOccurrence)
        {
            FirstOccurrence = firstOccurrence;
            LastOccurrence = firstOccurrence;
            RawText = firstOccurrence.RawText;
            SeverityGroup = GetSeverityGroup(firstOccurrence.Severity);
            Count = 1;
        }

        public string RawText { get; }

        public int Count { get; private set; }

        public PatternSeverityGroup SeverityGroup { get; private set; }

        public ParsedLogEntry FirstOccurrence { get; }

        public ParsedLogEntry LastOccurrence { get; private set; }

        public IReadOnlyList<ParsedLogEntry> Occurrences => _occurrences ?? [FirstOccurrence];

        public void Add(ParsedLogEntry entry)
        {
            _occurrences ??= [FirstOccurrence];
            _occurrences.Add(entry);
            Count++;
            LastOccurrence = entry;

            if (SeverityGroup != GetSeverityGroup(entry.Severity))
            {
                SeverityGroup = PatternSeverityGroup.Mixed;
            }
        }
    }

    private sealed record RepeatedAnalysis(
        IReadOnlyList<RepeatedMessageFinding> TopFindings,
        IReadOnlyList<RepeatedMessageFinding> SeverityLeaders,
        int TotalPatternCount);

    private sealed record TimedEntry(ParsedLogEntry Entry, DateTime Timestamp);

    private sealed record TimeSelection(
        IReadOnlyList<TimedEntry> Entries,
        PatternTimeAnalysisStatus Status);

    private sealed record BurstAnalysis(
        IReadOnlyList<SeverityBurstFinding> Findings,
        int TotalFindingCount)
    {
        public static BurstAnalysis Empty { get; } = new([], 0);
    }

    private sealed record BucketAccumulator(
        DateTime Start,
        TimeSpan Duration,
        IReadOnlyList<ParsedLogEntry> Entries);
}
