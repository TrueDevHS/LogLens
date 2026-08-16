using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LogLens.Core.Analysis;
using LogLens.Core.Parsing;
using LogLens.Core.Patterns;
using LogLens.Core.Querying;

namespace LogLens.App.Views;

public partial class PatternsView : UserControl
{
    private readonly IPatternAnalysisService _patternAnalysisService;
    private LogAnalysisResult? _analysis;
    private CancellationTokenSource? _analysisCancellation;
    private int _analysisSequence;
    private bool _changingSelection;
    private bool _isRestoredSnapshot;

    public PatternsView()
    {
        InitializeComponent();
        _patternAnalysisService = new PatternAnalysisService();
    }

    public event EventHandler<PatternEntryRequestedEventArgs>? EntryRequested;

    public event EventHandler<PatternAnalysisCompletedEventArgs>? AnalysisCompleted;

    public void ShowAnalysis(LogAnalysisResult analysis, bool isRestored = false)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        CancelPendingAnalysis();
        _analysis = analysis;
        _isRestoredSnapshot = isRestored;

        NoFilePanel.Visibility = Visibility.Collapsed;
        AnalysisPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        AnalysisProgressBar.Visibility = Visibility.Visible;
        LoadingTitleText.Text = "Analysing patterns locally";
        LoadingMessageText.Text = "Using the parsed entries already held in memory.";

        int sequence = ++_analysisSequence;
        var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        _ = AnalyzeAsync(analysis, sequence, cancellation);
    }

    public void Reset()
    {
        CancelPendingAnalysis();
        _analysis = null;
        _isRestoredSnapshot = false;
        ClearResults();
        LoadingPanel.Visibility = Visibility.Collapsed;
        AnalysisPanel.Visibility = Visibility.Collapsed;
        NoFilePanel.Visibility = Visibility.Visible;
    }

    public void CancelPendingAnalysis()
    {
        _analysisSequence++;
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = null;
    }

    private async Task AnalyzeAsync(
        LogAnalysisResult analysis,
        int sequence,
        CancellationTokenSource cancellation)
    {
        try
        {
            PatternAnalysisResult result = await Task.Run(
                () => _patternAnalysisService.Analyze(
                    analysis.Parsing.Entries,
                    cancellation.Token),
                cancellation.Token);

            if (sequence == _analysisSequence && ReferenceEquals(analysis, _analysis))
            {
                ApplyResult(analysis, result);
                AnalysisCompleted?.Invoke(
                    this,
                    new PatternAnalysisCompletedEventArgs(analysis, result));
            }
        }
        catch (OperationCanceledException)
        {
            // A new source or application close superseded this analysis.
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (sequence == _analysisSequence)
            {
                ShowAnalysisError();
            }
        }
        finally
        {
            if (sequence == _analysisSequence)
            {
                _analysisCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ApplyResult(LogAnalysisResult analysis, PatternAnalysisResult result)
    {
        RepeatedMessageFinding[] repeatedFindings = CombineRepeatedFindings(result);

        EntriesAnalyzedText.Text = result.EntriesAnalyzed.ToString("N0", CultureInfo.CurrentCulture);
        RepeatCountText.Text = result.TotalRepeatedMessagePatterns.ToString("N0", CultureInfo.CurrentCulture);
        BurstCountText.Text = result.TotalSeverityBurstCount.ToString("N0", CultureInfo.CurrentCulture);
        TimedEntryCountText.Text = result.TimeAnalysisStatus.ComparableTimestampEntries
            .ToString("N0", CultureInfo.CurrentCulture);
        TimeAnalysisStatusText.Text = result.TimeAnalysisStatus.Explanation;
        SeverityDistributionText.Text = FormatSeverityDistribution(result.SeverityDistribution);
        SourceContextText.Text = _isRestoredSnapshot
            ? $"{analysis.Source.FileName}  •  rebuilt from a restored local snapshot"
            : analysis.Parsing.Summary.IsComplete
                ? $"{analysis.Source.FileName}  •  pattern analysis complete"
                : $"{analysis.Source.FileName}  •  patterns reflect a partial parsed result";
        BoundedResultsText.Text =
            $"Top {PatternAnalysisPolicy.MaximumRepeatedMessageFindings} repeats • "
            + $"top {PatternAnalysisPolicy.MaximumSeverityBurstFindings} bursts • "
            + $"{PatternAnalysisPolicy.MaximumEvidenceEntriesPerFinding} evidence rows max";

        RepeatedFindingsListBox.ItemsSource = repeatedFindings;
        BurstFindingsListBox.ItemsSource = result.SeverityBursts;
        ActivityFindingsListBox.ItemsSource = result.ActivityWindows;

        NoRepeatedPatternsText.Visibility = repeatedFindings.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoBurstsText.Text = result.TimeAnalysisStatus.IsAvailable
            ? "No Warning or Error/Critical bursts detected with the current fixed thresholds."
            : result.TimeAnalysisStatus.Explanation;
        NoBurstsText.Visibility = result.SeverityBursts.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoActivityWindowsText.Text = result.TimeAnalysisStatus.IsAvailable
            ? "No activity windows were available."
            : result.TimeAnalysisStatus.Explanation;
        NoActivityWindowsText.Visibility = result.ActivityWindows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        LoadingPanel.Visibility = Visibility.Collapsed;
        NoFilePanel.Visibility = Visibility.Collapsed;
        AnalysisPanel.Visibility = Visibility.Visible;

        object? initialFinding = repeatedFindings.FirstOrDefault()
            ?? (object?)result.SeverityBursts.FirstOrDefault()
            ?? result.ActivityWindows.FirstOrDefault();
        SelectFinding(initialFinding);
    }

    private void ShowAnalysisError()
    {
        ClearResults();
        AnalysisProgressBar.Visibility = Visibility.Collapsed;
        LoadingTitleText.Text = "Pattern analysis could not be completed";
        LoadingMessageText.Text =
            "The parsed source remains unchanged. You can open another supported log file and try again.";
        AnalysisPanel.Visibility = Visibility.Collapsed;
        NoFilePanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
    }

    private void FindingListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection || sender is not ListBox listBox || listBox.SelectedItem is null)
        {
            return;
        }

        SelectFinding(listBox.SelectedItem);
    }

    private void SelectFinding(object? finding)
    {
        _changingSelection = true;
        RepeatedFindingsListBox.SelectedItem = finding is RepeatedMessageFinding ? finding : null;
        BurstFindingsListBox.SelectedItem = finding is SeverityBurstFinding ? finding : null;
        ActivityFindingsListBox.SelectedItem = finding is ActivityWindowFinding ? finding : null;
        _changingSelection = false;

        switch (finding)
        {
            case RepeatedMessageFinding repeated:
                ShowFinding(
                    repeated.Title,
                    repeated.Explanation,
                    FormatSeverityGroup(repeated.SeverityGroup),
                    $"Lines {repeated.FirstOccurrence.LineNumber:N0}–{repeated.LastOccurrence.LineNumber:N0}",
                    repeated.Evidence);
                break;
            case SeverityBurstFinding burst:
                ShowFinding(
                    burst.Title,
                    burst.Explanation,
                    FormatSeverityGroup(burst.SeverityGroup),
                    FormatTimeRange(burst.TimeRange),
                    burst.Evidence);
                break;
            case ActivityWindowFinding activity:
                ShowFinding(
                    activity.Title,
                    activity.Explanation,
                    FormatActivityType(activity.Type),
                    FormatActivityWindow(activity.Window),
                    activity.Evidence);
                break;
            default:
                ShowNoFindingSelection();
                break;
        }
    }

    private void ShowFinding(
        string title,
        string explanation,
        string type,
        string time,
        PatternEvidence evidence)
    {
        NoFindingSelectionPanel.Visibility = Visibility.Collapsed;
        FindingDetailsPanel.Visibility = Visibility.Visible;
        FindingTitleText.Text = title;
        FindingExplanationText.Text = explanation;
        FindingTypeText.Text = type;
        FindingTimeText.Text = time;
        EvidenceCountText.Text = evidence.IsTruncated
            ? $"Showing {evidence.Entries.Count:N0} of {evidence.TotalEntryCount:N0} supporting entries"
            : $"{evidence.TotalEntryCount:N0} supporting entries";
        EvidenceListBox.ItemsSource = evidence.Entries;
        EvidenceListBox.SelectedItem = evidence.Entries.FirstOrDefault();
    }

    private void EvidenceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EvidenceListBox.SelectedItem is not ParsedLogEntry entry)
        {
            EvidenceRawTextBox.Text = string.Empty;
            EvidenceLimitNoticeText.Visibility = Visibility.Collapsed;
            OpenEvidenceButton.IsEnabled = false;
            return;
        }

        BoundedEntryText detail = LogEntryTextProjection.CreateDetail(entry.RawText);
        EvidenceRawTextBox.Text = detail.Text;
        EvidenceLimitNoticeText.Text = detail.IsTruncated
            ? $"Display limited to the first {LogEntryTextProjection.DetailCharacterLimit:N0} characters of {detail.OriginalCharacterCount:N0}. The complete raw text remains preserved in Core."
            : string.Empty;
        EvidenceLimitNoticeText.Visibility = detail.IsTruncated
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenEvidenceButton.IsEnabled = true;
    }

    private void OpenEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (EvidenceListBox.SelectedItem is ParsedLogEntry entry)
        {
            EntryRequested?.Invoke(this, new PatternEntryRequestedEventArgs(entry));
        }
    }

    private void ShowNoFindingSelection()
    {
        FindingDetailsPanel.Visibility = Visibility.Collapsed;
        NoFindingSelectionPanel.Visibility = Visibility.Visible;
        EvidenceListBox.ItemsSource = null;
        EvidenceRawTextBox.Text = string.Empty;
        EvidenceLimitNoticeText.Visibility = Visibility.Collapsed;
        OpenEvidenceButton.IsEnabled = false;
    }

    private void ClearResults()
    {
        RepeatedFindingsListBox.ItemsSource = null;
        BurstFindingsListBox.ItemsSource = null;
        ActivityFindingsListBox.ItemsSource = null;
        EntriesAnalyzedText.Text = string.Empty;
        RepeatCountText.Text = string.Empty;
        BurstCountText.Text = string.Empty;
        TimedEntryCountText.Text = string.Empty;
        TimeAnalysisStatusText.Text = string.Empty;
        SeverityDistributionText.Text = string.Empty;
        SourceContextText.Text = string.Empty;
        BoundedResultsText.Text = string.Empty;
        ShowNoFindingSelection();
    }

    private static RepeatedMessageFinding[] CombineRepeatedFindings(PatternAnalysisResult result)
    {
        var findings = new List<RepeatedMessageFinding>(result.TopRepeatedMessages);
        foreach (RepeatedMessageFinding leader in result.RepeatedSeverityLeaders)
        {
            if (!findings.Any(candidate => ReferenceEquals(candidate, leader)))
            {
                findings.Add(leader);
            }
        }

        return findings.ToArray();
    }

    private static string FormatSeverityDistribution(IReadOnlyList<SeverityFrequency> frequencies)
    {
        if (frequencies.Count == 0)
        {
            return "No severity data";
        }

        return "Severity: " + string.Join(
            " • ",
            frequencies.Select(frequency =>
                $"{FormatSeverity(frequency.Severity)} {frequency.Count:N0}"));
    }

    private static string FormatSeverity(LogSeverity severity) => severity switch
    {
        LogSeverity.Information => "Info",
        LogSeverity.Critical => "Critical/Fatal",
        LogSeverity.Unknown => "Unclassified",
        _ => severity.ToString()
    };

    private static string FormatSeverityGroup(PatternSeverityGroup group) => group switch
    {
        PatternSeverityGroup.Information => "Information",
        PatternSeverityGroup.Critical => "Critical/Fatal",
        PatternSeverityGroup.ErrorCritical => "Error/Critical",
        PatternSeverityGroup.Unknown => "Unclassified",
        PatternSeverityGroup.Mixed => "Mixed severity",
        _ => group.ToString()
    };

    private static string FormatActivityType(ActivityWindowType type) => type switch
    {
        ActivityWindowType.BusiestMinute => "Fixed minute",
        ActivityWindowType.BusiestHour => "Fixed hour",
        ActivityWindowType.MostErrorCriticalMinute => "Error/Critical minute",
        _ => type.ToString()
    };

    private static string FormatTimeRange(PatternTimeRange range) =>
        $"{FormatTimestamp(range.Start, range.Basis)} – {FormatTimestamp(range.End, range.Basis)}";

    private static string FormatActivityWindow(PatternActivityWindow window)
    {
        string duration = window.Duration == TimeSpan.FromHours(1) ? "1 hour" : "1 minute";
        return $"{FormatTimestamp(window.Start, window.Basis)} ({duration})";
    }

    private static string FormatTimestamp(DateTime timestamp, PatternTimeBasis basis)
    {
        string formatted = timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return basis == PatternTimeBasis.Utc ? $"{formatted} UTC" : formatted;
    }
}
