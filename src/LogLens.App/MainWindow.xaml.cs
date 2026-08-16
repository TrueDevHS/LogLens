using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LogLens.App.Views;
using LogLens.Core.Analysis;
using LogLens.Core.Files;
using LogLens.Core.Parsing;
using LogLens.Core.Patterns;
using LogLens.Core.Persistence;
using LogLens.Core.Reports;
using Microsoft.Win32;

namespace LogLens.App;

public partial class MainWindow : Window
{
    private static readonly TimeSpan PersistenceDebounceInterval =
        TimeSpan.FromMilliseconds(650);

    private readonly IReadOnlyDictionary<AppSection, UserControl> _pages;
    private readonly WelcomeView _welcomeView;
    private readonly DashboardView _dashboardView;
    private readonly EntryExplorerView _entryExplorerView;
    private readonly PatternsView _patternsView;
    private readonly AboutPrivacyView _aboutPrivacyView;
    private readonly ILogAnalysisService _analysisService;
    private readonly IReportGenerationService _reportGenerationService;
    private readonly IReportDestinationWriter _reportDestinationWriter;
    private readonly ILocalSessionStore _localSessionStore;
    private readonly ILocalAppDataEraser _localAppDataEraser;
    private readonly DispatcherTimer _persistenceDebounceTimer;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _exportCancellation;
    private CancellationTokenSource? _persistenceCancellation;
    private Task _persistenceTask = Task.CompletedTask;
    private LogAnalysisResult? _currentAnalysis;
    private PatternAnalysisResult? _currentPatterns;
    private DateTimeOffset? _sessionCreatedAtUtc;
    private AppSection _currentSection = AppSection.Home;
    private int _loadSequence;
    private bool _startupRestoreInProgress = true;
    private bool _eraseInProgress;
    private bool _closeInProgress;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        _analysisService = new LogAnalysisService();
        _reportGenerationService = new ReportGenerationService();
        _reportDestinationWriter = new ReportDestinationWriter();
        var pathProvider = new LocalAppDataPathProvider();
        _localSessionStore = new LocalSessionStore(pathProvider);
        _localAppDataEraser = new LocalAppDataEraser(pathProvider);
        _welcomeView = new WelcomeView();
        _dashboardView = new DashboardView();
        _entryExplorerView = new EntryExplorerView();
        _patternsView = new PatternsView();
        _aboutPrivacyView = new AboutPrivacyView();
        _persistenceDebounceTimer = new DispatcherTimer
        {
            Interval = PersistenceDebounceInterval
        };
        _persistenceDebounceTimer.Tick += PersistenceDebounceTimer_Tick;
        _welcomeView.OpenFileRequested += WelcomeView_OpenFileRequested;
        _welcomeView.CancelRequested += WelcomeView_CancelRequested;
        _entryExplorerView.StateChanged += EntryExplorerView_StateChanged;
        _patternsView.EntryRequested += PatternsView_EntryRequested;
        _patternsView.AnalysisCompleted += PatternsView_AnalysisCompleted;
        _aboutPrivacyView.EraseRequested += AboutPrivacyView_EraseRequested;

        _pages = new Dictionary<AppSection, UserControl>
        {
            [AppSection.Home] = _welcomeView,
            [AppSection.Dashboard] = _dashboardView,
            [AppSection.Explorer] = _entryExplorerView,
            [AppSection.Patterns] = _patternsView,
            [AppSection.About] = _aboutPrivacyView
        };

        NavigateTo(AppSection.Home, schedulePersistence: false);
        Loaded += MainWindow_Loaded;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_eraseInProgress)
        {
            e.Cancel = true;
            base.OnClosing(e);
            return;
        }

        if (_allowClose || _currentAnalysis is null)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnClosing(e);
        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;
        await CancelPendingPersistenceAsync();
        await PersistCurrentSessionAsync(
            _currentAnalysis,
            CancellationToken.None,
            announceSuccess: false);
        _allowClose = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _loadSequence++;
        _loadCancellation?.Cancel();
        _entryExplorerView.CancelPendingQuery();
        _patternsView.CancelPendingAnalysis();
        _exportCancellation?.Cancel();
        _persistenceDebounceTimer.Stop();
        _persistenceCancellation?.Cancel();
        base.OnClosed(e);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        _welcomeView.ShowRestoring();

        try
        {
            SessionLoadResult loadResult = await _localSessionStore.LoadAsync();
            if (loadResult.Succeeded && loadResult.Session is not null)
            {
                await RestoreSessionAsync(loadResult.Session);
            }
            else if (loadResult.Status == SessionLoadStatus.NoSession)
            {
                _welcomeView.Reset();
            }
            else
            {
                _welcomeView.ShowStorageWarning(loadResult.Message);
                ShowLocalSessionStatus(loadResult.Message);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            _welcomeView.ShowStorageWarning(
                "LogLens could not inspect its local session data and started cleanly.");
        }
        finally
        {
            _startupRestoreInProgress = false;
            await RefreshStorageStatusAsync();
        }
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string destination }
            && Enum.TryParse(destination, out AppSection section))
        {
            NavigateTo(section);
        }
    }

    private async void WelcomeView_OpenFileRequested(object? sender, EventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Log File",
            Filter = "Log files (*.log;*.txt)|*.log;*.txt",
            DefaultExt = ".log",
            AddExtension = false,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            ValidateNames = true,
            DereferenceLinks = false,
            AddToRecent = false
        };

        try
        {
            if (dialog.ShowDialog(this) == true)
            {
                await LoadSourceAsync(dialog.FileName);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            _welcomeView.ShowError("The Windows file picker could not be opened. Please try again.");
        }
    }

    private void WelcomeView_CancelRequested(object? sender, EventArgs e) =>
        _loadCancellation?.Cancel();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedPaths(e.Data, out string[] paths) && paths.Length == 1
            ? DragDropEffects.Link
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!TryGetDroppedPaths(e.Data, out string[] paths))
        {
            ShowHomeError("Drag a single local .log or .txt file onto LogLens.");
            return;
        }

        if (paths.Length != 1)
        {
            ShowHomeError("LogLens opens one file at a time. Drag a single .log or .txt file.");
            return;
        }

        await LoadSourceAsync(paths[0]);
    }

    private async Task LoadSourceAsync(string path)
    {
        await CancelPendingPersistenceAsync();
        int loadSequence = ++_loadSequence;
        _loadCancellation?.Cancel();
        ResetExportState();
        _sessionCreatedAtUtc = null;

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;

        NavigateTo(AppSection.Home);
        HomeNavigationButton.IsChecked = true;
        _dashboardView.Reset();
        _entryExplorerView.Reset();
        _patternsView.Reset();
        _welcomeView.ShowLoading(GetSafeFileName(path));

        try
        {
            LogAnalysisResult analysis = await _analysisService.AnalyzeAsync(
                path,
                cancellation.Token);

            if (loadSequence == _loadSequence)
            {
                _currentAnalysis = analysis;
                _sessionCreatedAtUtc = DateTimeOffset.UtcNow;
                _dashboardView.ShowAnalysis(analysis, isRestored: false);
                _entryExplorerView.ShowAnalysis(analysis, isRestored: false);
                _patternsView.ShowAnalysis(analysis, isRestored: false);
                _welcomeView.ShowReady(analysis);
                ShowLocalSessionStatus("Preparing the current session for local restore.");
                SchedulePersistence();
            }
        }
        catch (OperationCanceledException)
        {
            if (loadSequence == _loadSequence)
            {
                _welcomeView.ShowCancelled();
            }
        }
        catch (SourceFileException exception)
        {
            Debug.WriteLine(exception);
            if (loadSequence == _loadSequence)
            {
                _welcomeView.ShowError(exception.Message);
            }
        }
        catch (LogParsingException exception)
        {
            Debug.WriteLine(exception);
            if (loadSequence == _loadSequence)
            {
                _welcomeView.ShowError(exception.Message);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (loadSequence == _loadSequence)
            {
                _welcomeView.ShowError(
                    "LogLens could not read this file safely. Check that it still exists and is not locked.");
            }
        }
        finally
        {
            if (loadSequence == _loadSequence)
            {
                _loadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ShowHomeError(string message)
    {
        NavigateTo(AppSection.Home);
        HomeNavigationButton.IsChecked = true;
        _welcomeView.ShowError(message);
    }

    private void PatternsView_EntryRequested(
        object? sender,
        PatternEntryRequestedEventArgs e)
    {
        if (_entryExplorerView.SelectEntry(e.Entry))
        {
            EntriesNavigationButton.IsChecked = true;
            NavigateTo(AppSection.Explorer);
        }
    }

    private void EntryExplorerView_StateChanged(object? sender, EventArgs e) =>
        SchedulePersistence();

    private void PatternsView_AnalysisCompleted(
        object? sender,
        PatternAnalysisCompletedEventArgs e)
    {
        if (ReferenceEquals(e.Analysis, _currentAnalysis))
        {
            _currentPatterns = e.Patterns;
            ExportSummaryButton.IsEnabled = true;
            ShowExportStatus(
                "Ready to export a local summary.");
            SchedulePersistence();
        }
    }

    private async void ExportSummaryButton_Click(object sender, RoutedEventArgs e)
    {
        LogAnalysisResult? analysis = _currentAnalysis;
        PatternAnalysisResult? patterns = _currentPatterns;
        if (analysis is null || patterns is null)
        {
            ShowExportStatus("Open and analyse a log before exporting a summary.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export LogLens Summary",
            Filter = "Text summary (*.txt)|*.txt|JSON summary (*.json)|*.json",
            FilterIndex = 1,
            DefaultExt = ".txt",
            AddExtension = true,
            CheckPathExists = true,
            CreatePrompt = false,
            OverwritePrompt = true,
            ValidateNames = true,
            AddToRecent = false,
            FileName = CreateSuggestedReportName(analysis.Source.FileName)
        };

        try
        {
            if (dialog.ShowDialog(this) != true)
            {
                ShowExportStatus("Export cancelled. No report created.");
                return;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            ShowExportFailure(
                "The Windows Save dialog could not be opened. No report was created.");
            return;
        }

        _exportCancellation?.Cancel();
        _exportCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _exportCancellation = cancellation;
        ExportSummaryButton.IsEnabled = false;
        ShowExportStatus("Saving summary locally…");

        try
        {
            ReportFormat format = GetReportFormat(dialog.FileName, dialog.FilterIndex);
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                ?? "0.1.0";
            var request = new ReportGenerationRequest(
                analysis,
                patterns,
                version,
                DateTimeOffset.UtcNow);
            ReportDocument report = await Task.Run(
                () => _reportGenerationService.Generate(
                    request,
                    format,
                    cancellation.Token),
                cancellation.Token);
            await _reportDestinationWriter.WriteAsync(
                new ReportWriteRequest(
                    report,
                    analysis.Source.FullPath,
                    dialog.FileName,
                    OverwriteConfirmed: true),
                cancellation.Token);

            if (ReferenceEquals(analysis, _currentAnalysis)
                && ReferenceEquals(patterns, _currentPatterns))
            {
                ShowExportStatus(
                    "Summary exported successfully.");
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(analysis, _currentAnalysis))
            {
                ShowExportStatus("Export cancelled. No report created.");
            }
        }
        catch (ReportExportException exception)
        {
            Debug.WriteLine(exception);
            ShowExportFailure(exception.Message);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            ShowExportFailure(
                "LogLens could not create the summary at that destination. The source log was not modified.");
        }
        finally
        {
            if (ReferenceEquals(cancellation, _exportCancellation))
            {
                _exportCancellation = null;
            }

            cancellation.Dispose();
            ExportSummaryButton.IsEnabled = _currentAnalysis is not null
                && _currentPatterns is not null;
        }
    }

    private void ResetExportState()
    {
        _exportCancellation?.Cancel();
        _exportCancellation?.Dispose();
        _exportCancellation = null;
        _currentAnalysis = null;
        _currentPatterns = null;
        ExportSummaryButton.IsEnabled = false;
        ExportStatusText.Text = string.Empty;
        ExportStatusText.Visibility = Visibility.Collapsed;
    }

    private void ShowExportStatus(string message)
    {
        ExportStatusText.Text = message;
        ExportStatusText.Visibility = Visibility.Visible;
    }

    private void ShowExportFailure(string message)
    {
        ShowExportStatus("Export failed. The source log was not modified.");
        MessageBox.Show(
            this,
            message,
            "Export Summary",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string CreateSuggestedReportName(string sourceFileName)
    {
        string name = Path.GetFileNameWithoutExtension(sourceFileName);
        return string.IsNullOrWhiteSpace(name)
            ? "LogLens-summary"
            : $"{name}-LogLens-summary";
    }

    private static ReportFormat GetReportFormat(string destinationPath, int filterIndex)
    {
        string extension = Path.GetExtension(destinationPath);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return ReportFormat.Json;
        }

        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return ReportFormat.Text;
        }

        return filterIndex == 2 ? ReportFormat.Json : ReportFormat.Text;
    }

    private void NavigateTo(AppSection section, bool schedulePersistence = true)
    {
        _currentSection = section;
        PageContent.Content = _pages[section];
        if (section == AppSection.About)
        {
            _ = RefreshStorageStatusAsync();
        }

        if (schedulePersistence)
        {
            SchedulePersistence();
        }
    }

    private void SetSelectedNavigation(AppSection section)
    {
        HomeNavigationButton.IsChecked = section == AppSection.Home;
        DashboardNavigationButton.IsChecked = section == AppSection.Dashboard;
        EntriesNavigationButton.IsChecked = section == AppSection.Explorer;
        PatternsNavigationButton.IsChecked = section == AppSection.Patterns;
        AboutNavigationButton.IsChecked = section == AppSection.About;
    }

    private async Task RestoreSessionAsync(RestoredLocalSession session)
    {
        ResetExportState();
        _currentAnalysis = session.Analysis;
        _sessionCreatedAtUtc = session.CreatedAtUtc;

        _dashboardView.ShowAnalysis(session.Analysis, isRestored: true);
        _entryExplorerView.ShowAnalysis(session.Analysis, isRestored: true);
        _patternsView.ShowAnalysis(session.Analysis, isRestored: true);
        _welcomeView.ShowRestored(session.Analysis, session.UpdatedAtUtc);
        await _entryExplorerView.RestoreStateAsync(session.UiState);

        AppSection restoredSection = Enum.TryParse(
            session.UiState.SelectedSection,
            ignoreCase: false,
            out AppSection parsedSection)
            ? parsedSection
            : AppSection.Home;
        SetSelectedNavigation(restoredSection);
        NavigateTo(restoredSection, schedulePersistence: false);
        ShowLocalSessionStatus("Restored from local LogLens data.");
        ShowExportStatus(
            "Restored snapshot loaded. Rebuilding deterministic patterns locally…");
    }

    private void SchedulePersistence()
    {
        if (_startupRestoreInProgress
            || _eraseInProgress
            || _closeInProgress
            || _currentAnalysis is null
            || _sessionCreatedAtUtc is null)
        {
            return;
        }

        _persistenceDebounceTimer.Stop();
        _persistenceDebounceTimer.Start();
    }

    private async void PersistenceDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _persistenceDebounceTimer.Stop();
        if (!_persistenceTask.IsCompleted)
        {
            _persistenceDebounceTimer.Start();
            return;
        }

        LogAnalysisResult? analysis = _currentAnalysis;
        if (analysis is null)
        {
            return;
        }

        _persistenceCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _persistenceCancellation = cancellation;
        _persistenceTask = PersistCurrentSessionAsync(
            analysis,
            cancellation.Token,
            announceSuccess: true);

        try
        {
            await _persistenceTask;
        }
        catch (OperationCanceledException)
        {
            // A new source, erase request or application close superseded this save.
        }
        finally
        {
            if (ReferenceEquals(cancellation, _persistenceCancellation))
            {
                _persistenceCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task PersistCurrentSessionAsync(
        LogAnalysisResult analysis,
        CancellationToken cancellationToken,
        bool announceSuccess)
    {
        if (!ReferenceEquals(analysis, _currentAnalysis)
            || _sessionCreatedAtUtc is not DateTimeOffset createdAtUtc)
        {
            return;
        }

        SessionUiState uiState = _entryExplorerView.CaptureState(
            _currentSection.ToString());
        var request = new SessionCaptureRequest(
            analysis,
            uiState,
            GetApplicationVersion(),
            createdAtUtc,
            DateTimeOffset.UtcNow);
        SessionSaveResult result = await _localSessionStore.SaveAsync(
            request,
            cancellationToken);

        if (!ReferenceEquals(analysis, _currentAnalysis))
        {
            return;
        }

        if (result.Succeeded)
        {
            if (announceSuccess)
            {
                ShowLocalSessionStatus("Current session stored locally.");
            }
        }
        else
        {
            ShowLocalSessionStatus(result.Message);
        }

        await RefreshStorageStatusAsync();
    }

    private async Task CancelPendingPersistenceAsync()
    {
        _persistenceDebounceTimer.Stop();
        CancellationTokenSource? cancellation = _persistenceCancellation;
        Task persistenceTask = _persistenceTask;
        cancellation?.Cancel();
        try
        {
            await persistenceTask;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected hand-off between persistence operations.
        }
        finally
        {
            if (ReferenceEquals(cancellation, _persistenceCancellation))
            {
                _persistenceCancellation = null;
            }

            cancellation?.Dispose();
            _persistenceTask = Task.CompletedTask;
        }
    }

    private async Task RefreshStorageStatusAsync()
    {
        try
        {
            LocalStorageStatus status = await _localSessionStore.GetStatusAsync();
            _aboutPrivacyView.ShowStorageStatus(status);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            _aboutPrivacyView.ShowEraseFailure(
                "LogLens could not inspect its local application-data folder.");
        }
    }

    private async void AboutPrivacyView_EraseRequested(object? sender, EventArgs e)
    {
        if (_eraseInProgress)
        {
            return;
        }

        bool confirmed;
        try
        {
            var dialog = new EraseDataWindow(_localSessionStore.StorageRoot)
            {
                Owner = this
            };
            confirmed = dialog.ShowDialog() == true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            _aboutPrivacyView.ShowEraseFailure(
                "The erase confirmation window could not be opened. No data was deleted.");
            return;
        }

        if (!confirmed)
        {
            _aboutPrivacyView.ShowEraseFailure(
                "Erase cancelled. No LogLens local data was deleted.");
            return;
        }

        _eraseInProgress = true;
        _aboutPrivacyView.SetEraseBusy(true);
        await CancelPendingPersistenceAsync();

        try
        {
            LocalDataEraseResult result = await _localAppDataEraser.EraseAllAsync();
            if (!result.Succeeded)
            {
                _aboutPrivacyView.ShowEraseFailure(result.Message);
                MessageBox.Show(
                    this,
                    result.Message,
                    "Erase LogLens Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ClearCurrentSessionAfterErase();
            await RefreshStorageStatusAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            _aboutPrivacyView.ShowEraseFailure(
                "LogLens local data could not be erased. Source logs and exported reports were not touched.");
        }
        finally
        {
            _eraseInProgress = false;
        }
    }

    private void ClearCurrentSessionAfterErase()
    {
        _loadSequence++;
        _loadCancellation?.Cancel();
        _entryExplorerView.CancelPendingQuery();
        _patternsView.CancelPendingAnalysis();
        ResetExportState();
        _sessionCreatedAtUtc = null;
        _dashboardView.Reset();
        _entryExplorerView.Reset();
        _patternsView.Reset();
        _welcomeView.Reset();
        _welcomeView.ShowLocalDataErased();
        SetSelectedNavigation(AppSection.Home);
        NavigateTo(AppSection.Home, schedulePersistence: false);
        ShowLocalSessionStatus("LogLens local data was erased.");
    }

    private void ShowLocalSessionStatus(string message)
    {
        LocalSessionStatusText.Text = message;
        LocalSessionStatusText.Visibility = Visibility.Visible;
    }

    private static string GetApplicationVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.1.0";

    private static bool TryGetDroppedPaths(IDataObject data, out string[] paths)
    {
        if (data.GetDataPresent(DataFormats.FileDrop)
            && data.GetData(DataFormats.FileDrop) is string[] droppedPaths)
        {
            paths = droppedPaths;
            return true;
        }

        paths = [];
        return false;
    }

    private static string GetSafeFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException)
        {
            return string.Empty;
        }
    }
}
