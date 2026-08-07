namespace Dock.Core.Services;

/// <summary>
/// Where the sound is going, whenever that changes.
///
/// Windows moves the default output silently -- plug in a headset, wake a monitor over HDMI, start
/// a virtual mixer -- and the only way to find out is to go and open the volume flyout.
/// </summary>
public interface IAudioDeviceSource
{
    /// <summary>Carries the device's friendly name, not its id.</summary>
    event EventHandler<string>? DefaultOutputChanged;

    /// <summary>False when this machine will not hand over its endpoints at all.</summary>
    bool Start();

    void Stop();
}
