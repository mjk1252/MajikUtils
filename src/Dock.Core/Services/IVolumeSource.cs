namespace Dock.Core.Services;

/// <summary>
/// The system output volume, whenever it moves.
///
/// Windows has its own on-screen display for this, parked in a corner and impossible to restyle.
/// The island is already the place the eye goes, which is the whole argument for putting it there
/// instead.
/// </summary>
public interface IVolumeSource
{
    /// <summary>Raised on every change, from any source -- a hardware key, the taskbar, another app.</summary>
    event EventHandler<VolumeReading>? Changed;

    /// <summary>False when this machine will not give up its output endpoint at all.</summary>
    bool Start();

    void Stop();
}

/// <param name="Level">0 to 1.</param>
public readonly record struct VolumeReading(double Level, bool IsMuted);
