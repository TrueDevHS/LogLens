namespace LogLens.Core.Querying;

[Flags]
public enum LogSeverityFilter
{
    None = 0,
    Trace = 1 << 0,
    Debug = 1 << 1,
    Information = 1 << 2,
    Warning = 1 << 3,
    Error = 1 << 4,
    Critical = 1 << 5,
    Unknown = 1 << 6,
    All = Trace | Debug | Information | Warning | Error | Critical | Unknown
}
