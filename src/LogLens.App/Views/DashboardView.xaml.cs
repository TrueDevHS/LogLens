using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LogLens.Core.Analysis;
using LogLens.Core.Parsing;

namespace LogLens.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    public void Reset()
    {
        DashboardSubtitleText.Text =
            "Summary statistics will appear here after a file is opened.";
        EmptyStatePanel.Visibility = Visibility.Visible;
        AnalysisPanel.Visibility = Visibility.Collapsed;
    }

    public void ShowAnalysis(LogAnalysisResult analysis, bool isRestored = false)
    {
        LogParsingSummary summary = analysis.Parsing.Summary;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        AnalysisPanel.Visibility = Visibility.Visible;

        DashboardSubtitleText.Text = isRestored
            ? "Counts restored from the local LogLens snapshot created during the original analysis."
            : "Truthful counts derived from the selected file's normalised text entries.";
        FileNameText.Text = analysis.Source.FileName;
        SourceDetailText.Text =
            $"{FormatFileSize(analysis.Source.Length)}  •  {analysis.Parsing.DetectedEncoding}";

        TotalEntriesText.Text = summary.IsComplete
            ? FormatCount(summary.TotalEntries)
            : $"{FormatCount(summary.TotalEntries)}+";
        InformationCountText.Text = FormatCount(summary.InformationCount);
        WarningCountText.Text = FormatCount(summary.WarningCount);
        ErrorCountText.Text = FormatCount(summary.ErrorCount);
        CriticalCountText.Text = FormatCount(summary.CriticalCount);
        DebugTraceCountText.Text = FormatCount(summary.DebugCount + summary.TraceCount);
        UnclassifiedCountText.Text = FormatCount(summary.UnclassifiedEntries);
        TimestampedCountText.Text = FormatCount(summary.TimestampedEntries);

        string completeness = summary.IsComplete
            ? "Parsing completed."
            : "The entry safety limit was reached, so this is an explicitly incomplete summary.";
        ParsingStatusText.Text =
            $"{completeness} {summary.ClassifiedEntries:N0} entries had a recognised severity, "
            + $"{summary.UnclassifiedEntries:N0} remained unclassified, and "
            + $"{analysis.Parsing.Diagnostics.Count:N0} diagnostics were recorded.";

        SourceIntegrityText.Text = analysis.Source.SourceChangedDuringRead
            ? "The source file changed during analysis. Results may be incomplete. LogLens did not modify the file."
            : "Source length and last-write metadata remained stable. The source was opened with read-only access.";
    }

    private static string FormatCount(int count) => count.ToString("N0", CultureInfo.CurrentCulture);

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
