namespace Dock.Core.Services;

/// <summary>
/// Watches a single Stack's source folder for top-level add/remove/rename changes. Raises
/// <see cref="Changed"/> on whatever thread the OS delivers the notification on -- callers on a
/// UI thread must marshal back themselves (see App.xaml.cs, which mirrors the Dispatcher.Invoke
/// pattern already used for RunningWindowSource/ExplorerTrayReader/SystemStatsSource).
/// </summary>
public sealed class StackFolderWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;

    public event Action? Changed;

    public StackFolderWatcher(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            _watcher.Created += (_, _) => Changed?.Invoke();
            _watcher.Deleted += (_, _) => Changed?.Invoke();
            _watcher.Renamed += (_, _) => Changed?.Invoke();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public void Dispose() => _watcher?.Dispose();
}
