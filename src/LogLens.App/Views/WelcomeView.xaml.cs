using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LogLens.Core.Analysis;
using LogLens.Core.Files;

namespace LogLens.App.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
    }

    public event EventHandler? OpenFileRequested;

    public event EventHandler? CancelRequested;

    public void Reset()
    {
        FinishLoading();
        LoadStatusPanel.Visibility = Visibility.Collapsed;
    }

    public void ShowRestoring()
    {
        OpenLogFileButton.IsEnabled = false;
        LoadStatusPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingMessageText.Text = "Checking for a stored local LogLens session…";
    }

    public void ShowRestored(LogAnalysisResult analysis, DateTimeOffset savedAtUtc)
    {
        FinishLoading();
        LoadStatusPanel.Visibility = Visibility.Visible;
        LoadStatusPanel.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        LoadStatusTitleText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        LoadStatusTitleText.Text = "Restored from local LogLens data";
        LoadStatusSummaryText.Text =
            $"{analysis.Source.FileName}  •  {FormatFileSize(analysis.Source.Length)}  •  stored snapshot";
        LoadStatusDetailText.Text =
            $"Saved {savedAtUtc.ToLocalTime():g}. The original source was not reopened or revalidated during this restore; its SHA-256 and integrity status are from the original analysis.";
    }

    public void ShowStorageWarning(string message)
    {
        FinishLoading();
        LoadStatusPanel.Visibility = Visibility.Visible;
        LoadStatusPanel.SetResourceReference(Border.BackgroundProperty, "WarningSoftBrush");
        LoadStatusTitleText.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        LoadStatusTitleText.Text = "Local session could not be restored";
        LoadStatusSummaryText.Text = message;
        LoadStatusDetailText.Text =
            "No source log or exported report was touched. You can erase LogLens-owned data from About & Privacy.";
    }

    public void ShowLocalDataErased()
    {
        FinishLoading();
        LoadStatusPanel.Visibility = Visibility.Visible;
        LoadStatusPanel.SetResourceReference(Border.BackgroundProperty, "SuccessSoftBrush");
        LoadStatusTitleText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
        LoadStatusTitleText.Text = "LogLens local data erased";
        LoadStatusSummaryText.Text = "The stored session and LogLens-owned local state were cleared.";
        LoadStatusDetailText.Text =
            "Original source logs and separately exported reports were not deleted or modified.";
    }

    public void ShowLoading(string fileName)
    {
        OpenLogFileButton.IsEnabled = false;
        LoadStatusPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingMessageText.Text = string.IsNullOrWhiteSpace(fileName)
            ? "Reading and parsing the selected file safely…"
            : $"Reading and parsing {fileName} safely…";
    }

    public void ShowReady(LogAnalysisResult analysis)
    {
        SourceFileInspection inspection = analysis.Source;
        var summary = analysis.Parsing.Summary;
        bool hasWarning = inspection.SourceChangedDuringRead || !summary.IsComplete;

        FinishLoading();
        LoadStatusPanel.Visibility = Visibility.Visible;
        LoadStatusPanel.SetResourceReference(
            Border.BackgroundProperty,
            hasWarning ? "WarningSoftBrush" : "SuccessSoftBrush");
        LoadStatusTitleText.SetResourceReference(
            TextBlock.ForegroundProperty,
            hasWarning ? "WarningBrush" : "SuccessBrush");

        LoadStatusTitleText.Text = hasWarning ? "Parsed with a warning" : "Parsing complete";
        LoadStatusSummaryText.Text =
            $"{inspection.FileName}  •  {FormatFileSize(inspection.Length)}  •  Opened read-only";

        if (inspection.SourceChangedDuringRead)
        {
            LoadStatusDetailText.Text =
                "The source file changed during analysis. Results may be incomplete. LogLens did not modify the file.";
        }
        else if (!summary.IsComplete)
        {
            LoadStatusDetailText.Text =
                $"{summary.TotalEntries:N0}+ entries were normalised before the safety limit was reached. View Dashboard or Entries for the truthful partial result.";
        }
        else
        {
            LoadStatusDetailText.Text =
                $"{summary.TotalEntries:N0} entries were normalised as inert text. View Dashboard or Entries to investigate them locally.";
        }
    }

    public void ShowError(string message)
    {
        FinishLoading();
        LoadStatusPanel.Visibility = Visibility.Visible;
        LoadStatusPanel.SetResourceReference(Border.BackgroundProperty, "ErrorSoftBrush");
        LoadStatusTitleText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        LoadStatusTitleText.Text = "File could not be opened";
        LoadStatusSummaryText.Text = message;
        LoadStatusDetailText.Text = "The original file was not modified.";
    }

    public void ShowCancelled()
    {
        FinishLoading();
        LoadStatusPanel.Visibility = Visibility.Visible;
        LoadStatusPanel.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        LoadStatusTitleText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        LoadStatusTitleText.Text = "Loading cancelled";
        LoadStatusSummaryText.Text = "LogLens stopped reading the selected file safely.";
        LoadStatusDetailText.Text = "The original file was not modified.";
    }

    private void FinishLoading()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        OpenLogFileButton.IsEnabled = true;
    }

    private void OpenLogFileButton_Click(object sender, RoutedEventArgs e) =>
        OpenFileRequested?.Invoke(this, EventArgs.Empty);

    private void CancelLoadingButton_Click(object sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private static string FormatFileSize(long bytes)
    {
        const double kibibyte = 1024;
        const double mebibyte = 1024 * 1024;

        if (bytes >= mebibyte)
        {
            return $"{bytes / mebibyte:0.##} MiB";
        }

        if (bytes >= kibibyte)
        {
            return $"{bytes / kibibyte:0.##} KiB";
        }

        return string.Format(CultureInfo.CurrentCulture, "{0:N0} bytes", bytes);
    }
}
