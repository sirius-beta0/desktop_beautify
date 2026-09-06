namespace Launcher.Core.Indexing;

public interface IAppScanner
{
    Task<IReadOnlyList<AppEntry>> ScanAsync(CancellationToken ct = default);
}