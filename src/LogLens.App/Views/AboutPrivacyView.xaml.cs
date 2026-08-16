using System.Globalization;
using System.Windows.Controls;
using LogLens.Core.Persistence;

namespace LogLens.App.Views;

public partial class AboutPrivacyView : UserControl
{
    public AboutPrivacyView()
    {
        InitializeComponent();
    }

    public event EventHandler? EraseRequested;

    public void ShowStorageStatus(LocalStorageStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        StoragePathText.Text = status.StorageRoot;
        StoredSessionText.Text = status.SessionExists ? "Yes" : "No";
        StorageSizeText.Text = FormatSize(status.ApproximateSizeBytes);
        RawTextStoredText.Text = status.ContainsRawParsedLogText ? "Yes" : "No";
        RecentExportMetadataText.Text = status.ContainsRecentExportMetadata ? "Yes" : "No";
        StorageStatusText.Text = status.Message;
        EraseAllDataButton.IsEnabled = status.IsAccessible;
    }

    public void SetEraseBusy(bool isBusy)
    {
        EraseAllDataButton.IsEnabled = !isBusy;
        if (isBusy)
        {
            StorageStatusText.Text =
                "Erasing only LogLens-owned local application data…";
        }
    }

    public void ShowEraseFailure(string message)
    {
        EraseAllDataButton.IsEnabled = true;
        StorageStatusText.Text = message;
    }

    private void EraseAllDataButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        EraseRequested?.Invoke(this, EventArgs.Empty);

    private static string FormatSize(long bytes)
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
