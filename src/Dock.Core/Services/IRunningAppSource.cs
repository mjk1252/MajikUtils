using Dock.Core.Models;

namespace Dock.Core.Services;

public interface IRunningAppSource
{
    event EventHandler<IReadOnlyList<RunningAppGroup>>? Updated;
    void Start();
    void Stop();
}
