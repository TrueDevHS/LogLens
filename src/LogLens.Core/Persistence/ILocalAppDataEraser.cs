namespace LogLens.Core.Persistence;

public interface ILocalAppDataEraser
{
    Task<LocalDataEraseResult> EraseAllAsync(CancellationToken cancellationToken = default);
}
