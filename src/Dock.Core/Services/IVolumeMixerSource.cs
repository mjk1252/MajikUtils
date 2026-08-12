using Dock.Core.Models;

namespace Dock.Core.Services;

/// <summary>
/// Every application currently holding an audio session, the way the shell's own volume mixer
/// shows them -- and the write half, since a mixer that could not be dragged would just be a
/// second now-playing readout with extra steps.
/// </summary>
public interface IVolumeMixerSource
{
    event EventHandler<IReadOnlyList<AudioSessionInfo>>? Changed;

    bool Start();
    void Stop();

    void SetVolume(int processId, double level);
    void SetMuted(int processId, bool muted);
}
