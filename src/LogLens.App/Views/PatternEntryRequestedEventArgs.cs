using LogLens.Core.Parsing;

namespace LogLens.App.Views;

public sealed class PatternEntryRequestedEventArgs(ParsedLogEntry entry) : EventArgs
{
    public ParsedLogEntry Entry { get; } = entry ?? throw new ArgumentNullException(nameof(entry));
}
