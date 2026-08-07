using Dock.Core.Models;

namespace Dock.Core.Services;

/// <summary>
/// Which applications are using the camera.
///
/// Windows tracks this for its own privacy indicator and surfaces it as a tray glyph with no name
/// attached, so the useful half -- *which* application -- is the part worth an island activity.
/// </summary>
public interface IDeviceUsageSource
{
    /// <summary>
    /// The full set in use, raised whenever it changes. Empty means nothing is watching -- not "no
    /// reading", which this cannot distinguish and does not have to.
    /// </summary>
    event EventHandler<IReadOnlyList<DeviceUsage>>? Changed;

    void Start();
    void Stop();
}
