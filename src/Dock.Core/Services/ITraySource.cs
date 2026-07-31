using Dock.Core.Models;

namespace Dock.Core.Services;

public interface ITraySource
{
    event EventHandler<IReadOnlyList<TrayIcon>>? Updated;
    void Start();
    void Stop();
    void Invoke(TrayIcon icon, bool rightClick);
}
