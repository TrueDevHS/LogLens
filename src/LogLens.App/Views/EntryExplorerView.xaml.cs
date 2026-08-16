using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LogLens.Core.Analysis;
using LogLens.Core.Parsing;
using LogLens.Core.Persistence;
using LogLens.Core.Querying;

namespace LogLens.App.Views;

public partial class EntryExplorerView : UserControl
{
    private static readonly TimeSpan SearchDebounceInterval = TimeSpan.FromMilliseconds(180);

    private readonly ILogEntryQueryService _queryService;
    private readonly DispatcherTimer _searchDebounceTimer;
    private LogAnalysisResult? _analysis;
    private CancellationTokenSource? _queryCancellation;
    private int _querySequence;
    private bool _updatingControls;
    private bool _suppressStateNotifications;

    public EntryExplorerView()
    {
        InitializeComponent();

        _queryService = new LogEntryQueryService();
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = SearchDebounceInterval
        };
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
    }

    public event EventHandler? StateChanged;

    public void ShowAnalysis(LogAnalysisResult analysis, bool isRestored = false)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        CancelPendingQuery();
        _analysis = analysis;

        _suppressStateNotifications = true;
        ResetControls();
        NoFilePanel.Visibility = Visibility.Collapsed;
        ExplorerPanel.Visibility = Visibility.Visible;
        SourceContextText.Text = isRestored
            ? $"{analysis.Source.FileName}  •  restored local snapshot"
            : analysis.Parsing.Summary.IsComplete
                ? $"{analysis.Source.FileName}  •  parsed locally"
                : $"{analysis.Source.FileName}  •  partial parsed result";

        ApplyResult(new LogEntryQueryResult(
            analysis.Parsing.Entries,
            analysis.Parsing.Entries.Count));
        _suppressStateNotifications = false;
    }

    public SessionUiState CaptureState(string selectedSection) => new(
        selectedSection,
        SearchTextBox.Text,
        GetSelectedSeverities(),
        TimestampFilterComboBox.SelectedIndex switch
        {
            1 => TimestampPresenceFilter.HasTimestamp,
            2 => TimestampPresenceFilter.NoTimestamp,
            _ => TimestampPresenceFilter.All
        },
        EntriesListBox.SelectedItem is ParsedLogEntry entry
            ? entry.LineNumber
            : null);

    public async Task RestoreStateAsync(SessionUiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_analysis is null)
        {
            return;
        }

        _suppressStateNotifications = true;
        _updatingControls = true;
        SearchTextBox.Text = state.SearchText;
        bool showAll = state.SeverityFilter == LogSeverityFilter.All;
        AllSeveritiesCheckBox.IsChecked = showAll;
        TraceCheckBox.IsChecked = !showAll
            && state.SeverityFilter.HasFlag(LogSeverityFilter.Trace);
        DebugCheckBox.IsChecked = !showAll
            && state.SeverityFilter.HasFlag(LogSeverityFilter.Debug);
        InformationCheckBox.IsChecked = !showAll
            && state.SeverityFilter.HasFlag(LogSeverityFilter.Information);
        WarningCheckBox.IsChecked = !showAll
            && state.SeverityFilter.HasFlag(LogSeverityFilter.Warning);
        ErrorCheckBox.IsChecked = !showAll
            && state.SeverityFilter.HasFlag(LogSeverityFilter.Error);
        CriticalCheckBox.IsChecked = !showAll
            && state.SeverityFilter.HasFlag(LogSeverityFilter.Critical);
        UnknownCheckBox.IsChecked = !showAll
            && state.SeverityFilter.HasFlag(LogSeverityFilter.Unknown);
        TimestampFilterComboBox.SelectedIndex = state.TimestampFilter switch
        {
            TimestampPresenceFilter.HasTimestamp => 1,
            TimestampPresenceFilter.NoTimestamp => 2,
            _ => 0
        };
        _updatingControls = false;

        await RefreshResultsAsync(state.SelectedEntryLineNumber);
        _suppressStateNotifications = false;
    }

    public void Reset()
    {
        CancelPendingQuery();
        _analysis = null;
        ResetControls();
        EntriesListBox.ItemsSource = null;
        ResultCountText.Text = "Showing 0 of 0 parsed entries";
        SourceContextText.Text = string.Empty;
        ExplorerPanel.Visibility = Visibility.Collapsed;
        NoFilePanel.Visibility = Visibility.Visible;
        ShowNoSelection();
    }

    public bool SelectEntry(ParsedLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LogAnalysisResult? analysis = _analysis;
        if (analysis is null)
        {
            return false;
        }

        ParsedLogEntry? loadedEntry = analysis.Parsing.Entries
            .FirstOrDefault(candidate => ReferenceEquals(candidate, entry));
        if (loadedEntry is null)
        {
            return false;
        }

        CancelPendingQuery();
        ResetControls();
        ApplyResult(new LogEntryQueryResult(
            analysis.Parsing.Entries,
            analysis.Parsing.Entries.Count));
        EntriesListBox.SelectedItem = loadedEntry;
        EntriesListBox.ScrollIntoView(loadedEntry);
        EntriesListBox.Focus();
        return true;
    }

    public void CancelPendingQuery()
    {
        _searchDebounceTimer.Stop();
        _querySequence++;
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        _queryCancellation = null;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingControls || _analysis is null)
        {
            return;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private async void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        await RefreshResultsAsync();
    }

    private async void AllSeveritiesCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        _updatingControls = true;
        bool showAll = AllSeveritiesCheckBox.IsChecked == true;
        if (showAll)
        {
            SetIndividualSeverityChecks(false);
        }
        else if (!HasIndividualSeveritySelection())
        {
            AllSeveritiesCheckBox.IsChecked = true;
        }

        _updatingControls = false;
        await RefreshImmediatelyAsync();
    }

    private async void SeverityCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        _updatingControls = true;
        AllSeveritiesCheckBox.IsChecked = !HasIndividualSeveritySelection();
        _updatingControls = false;
        await RefreshImmediatelyAsync();
    }

    private async void TimestampFilterComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingControls || _analysis is null)
        {
            return;
        }

        await RefreshImmediatelyAsync();
    }

    private async void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        ResetControls();
        await RefreshImmediatelyAsync();
        SearchTextBox.Focus();
    }

    private void EntriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntriesListBox.SelectedItem is ParsedLogEntry entry)
        {
            ShowSelectedEntry(entry);
        }
        else
        {
            ShowNoSelection();
        }

        NotifyStateChanged();
    }

    private async Task RefreshImmediatelyAsync()
    {
        _searchDebounceTimer.Stop();
        await RefreshResultsAsync();
    }

    private async Task RefreshResultsAsync(int? preferredLineNumber = null)
    {
        LogAnalysisResult? analysis = _analysis;
        if (analysis is null)
        {
            return;
        }

        int sequence = ++_querySequence;
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _queryCancellation = cancellation;
        LogEntryQuery query = BuildQuery();

        try
        {
            LogEntryQueryResult result = await Task.Run(
                () => _queryService.Query(
                    analysis.Parsing.Entries,
                    query,
                    cancellation.Token),
                cancellation.Token);

            if (sequence == _querySequence && ReferenceEquals(analysis, _analysis))
            {
                ApplyResult(result, preferredLineNumber);
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer filter request or a new source replaced this query.
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (sequence == _querySequence)
            {
                ResultCountText.Text = "The current filters could not be applied.";
            }
        }
        finally
        {
            if (sequence == _querySequence)
            {
                _queryCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private LogEntryQuery BuildQuery() => new(
        SearchTextBox.Text,
        GetSelectedSeverities(),
        TimestampFilterComboBox.SelectedIndex switch
        {
            1 => TimestampPresenceFilter.HasTimestamp,
            2 => TimestampPresenceFilter.NoTimestamp,
            _ => TimestampPresenceFilter.All
        });

    private LogSeverityFilter GetSelectedSeverities()
    {
        if (AllSeveritiesCheckBox.IsChecked == true)
        {
            return LogSeverityFilter.All;
        }

        LogSeverityFilter selected = LogSeverityFilter.None;
        selected |= TraceCheckBox.IsChecked == true ? LogSeverityFilter.Trace : LogSeverityFilter.None;
        selected |= DebugCheckBox.IsChecked == true ? LogSeverityFilter.Debug : LogSeverityFilter.None;
        selected |= InformationCheckBox.IsChecked == true ? LogSeverityFilter.Information : LogSeverityFilter.None;
        selected |= WarningCheckBox.IsChecked == true ? LogSeverityFilter.Warning : LogSeverityFilter.None;
        selected |= ErrorCheckBox.IsChecked == true ? LogSeverityFilter.Error : LogSeverityFilter.None;
        selected |= CriticalCheckBox.IsChecked == true ? LogSeverityFilter.Critical : LogSeverityFilter.None;
        selected |= UnknownCheckBox.IsChecked == true ? LogSeverityFilter.Unknown : LogSeverityFilter.None;
        return selected;
    }

    private void ApplyResult(
        LogEntryQueryResult result,
        int? preferredLineNumber = null)
    {
        int previousLine = EntriesListBox.SelectedItem is ParsedLogEntry selected
            ? selected.LineNumber
            : -1;
        int lineToRestore = preferredLineNumber ?? previousLine;

        EntriesListBox.ItemsSource = result.Entries;
        ResultCountText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "Showing {0:N0} of {1:N0} parsed entries",
            result.VisibleEntries,
            result.TotalEntries);

        bool hasMatches = result.VisibleEntries > 0;
        EntriesListBox.Visibility = hasMatches ? Visibility.Visible : Visibility.Collapsed;
        NoMatchesPanel.Visibility = hasMatches ? Visibility.Collapsed : Visibility.Visible;

        ParsedLogEntry? entryToSelect = result.Entries.FirstOrDefault(entry =>
            entry.LineNumber == lineToRestore);
        entryToSelect ??= result.Entries.FirstOrDefault();
        EntriesListBox.SelectedItem = entryToSelect;

        if (entryToSelect is null)
        {
            ShowNoSelection();
        }
    }

    private void ShowSelectedEntry(ParsedLogEntry entry)
    {
        NoSelectionPanel.Visibility = Visibility.Collapsed;
        EntryDetailsPanel.Visibility = Visibility.Visible;
        DetailLineNumberText.Text = entry.LineNumber.ToString("N0", CultureInfo.CurrentCulture);
        DetailSeverityText.Text = entry.Severity == LogSeverity.Unknown
            ? "Unclassified"
            : entry.Severity.ToString();
        DetailTimestampText.Text = entry.Timestamp?.RawText ?? "Unavailable";

        string[] diagnostics = _analysis?.Parsing.Diagnostics
            .Where(diagnostic => diagnostic.LineNumber == entry.LineNumber)
            .Select(diagnostic => $"• {diagnostic.Message}")
            .ToArray() ?? [];
        DetailDiagnosticsText.Text = diagnostics.Length == 0
            ? "No line-specific diagnostics."
            : string.Join(Environment.NewLine, diagnostics);

        BoundedEntryText detail = LogEntryTextProjection.CreateDetail(entry.RawText);
        DetailRawTextBox.Text = detail.Text;
        DetailLimitNoticeText.Visibility = detail.IsTruncated
            ? Visibility.Visible
            : Visibility.Collapsed;
        DetailLimitNoticeText.Text = detail.IsTruncated
            ? $"Display limited to the first {LogEntryTextProjection.DetailCharacterLimit:N0} characters of {detail.OriginalCharacterCount:N0}. The complete raw text remains preserved and searchable in memory."
            : string.Empty;
    }

    private void ShowNoSelection()
    {
        EntryDetailsPanel.Visibility = Visibility.Collapsed;
        NoSelectionPanel.Visibility = Visibility.Visible;
        DetailRawTextBox.Text = string.Empty;
    }

    private void ResetControls()
    {
        _updatingControls = true;
        SearchTextBox.Text = string.Empty;
        AllSeveritiesCheckBox.IsChecked = true;
        SetIndividualSeverityChecks(false);
        TimestampFilterComboBox.SelectedIndex = 0;
        _updatingControls = false;
    }

    private bool HasIndividualSeveritySelection() =>
        TraceCheckBox.IsChecked == true
        || DebugCheckBox.IsChecked == true
        || InformationCheckBox.IsChecked == true
        || WarningCheckBox.IsChecked == true
        || ErrorCheckBox.IsChecked == true
        || CriticalCheckBox.IsChecked == true
        || UnknownCheckBox.IsChecked == true;

    private void SetIndividualSeverityChecks(bool isChecked)
    {
        TraceCheckBox.IsChecked = isChecked;
        DebugCheckBox.IsChecked = isChecked;
        InformationCheckBox.IsChecked = isChecked;
        WarningCheckBox.IsChecked = isChecked;
        ErrorCheckBox.IsChecked = isChecked;
        CriticalCheckBox.IsChecked = isChecked;
        UnknownCheckBox.IsChecked = isChecked;
    }

    private void NotifyStateChanged()
    {
        if (!_suppressStateNotifications && _analysis is not null)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
