namespace LogLens.Core.Persistence;

public sealed class LocalAppDataPathProvider : ILocalAppDataPathProvider
{
    public string GetLogLensDataRoot()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "Windows did not provide a local application-data location.");
        }

        return Path.Combine(
            localApplicationData,
            LocalSessionPolicy.ApplicationDataFolderName);
    }
}
