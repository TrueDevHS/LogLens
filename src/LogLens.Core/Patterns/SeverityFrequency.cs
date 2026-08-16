using LogLens.Core.Parsing;

namespace LogLens.Core.Patterns;

public sealed record SeverityFrequency(LogSeverity Severity, int Count);
