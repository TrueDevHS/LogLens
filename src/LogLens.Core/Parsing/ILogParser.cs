namespace LogLens.Core.Parsing;

public interface ILogParser
{
    Task<LogParsingResult> ParseAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
