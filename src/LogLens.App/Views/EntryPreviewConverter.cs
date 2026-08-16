using System.Globalization;
using System.Windows.Data;
using LogLens.Core.Parsing;
using LogLens.Core.Querying;

namespace LogLens.App.Views;

public sealed class EntryPreviewConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not ParsedLogEntry entry)
        {
            return string.Empty;
        }

        string previewSource = entry.Message.Length > 0
            ? entry.Message
            : entry.RawText;
        return LogEntryTextProjection.CreatePreview(previewSource);
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
