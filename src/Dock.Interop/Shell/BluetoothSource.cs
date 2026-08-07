using Dock.Core.Services;
using Windows.Devices.Enumeration;

namespace Dock.Interop.Shell;

/// <summary>
/// Announces Bluetooth devices connecting and disconnecting -- the headphones-just-connected card,
/// which is the moment the Dynamic Island is most known for.
///
/// Watches association endpoints filtered to the Bluetooth protocol and to being connected, so the
/// watcher's Added and Removed are exactly "connected" and "disconnected" rather than "paired" and
/// "unpaired". Nothing here needs a capability: enumerating devices the user has already paired is
/// not privileged.
/// </summary>
public sealed class BluetoothSource : ISystemEventSource, IDisposable
{
    /// <summary>
    /// Classic Bluetooth's protocol id. Devices reachable over Bluetooth LE carry a different one,
    /// and audio devices -- which are what anybody wants announced -- are on this.
    /// </summary>
    private const string BluetoothProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";

    private const string ConnectedSelector =
        "System.Devices.Aep.ProtocolId:=\"" + BluetoothProtocol + "\""
        + " AND System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#True";

    private DeviceWatcher? _watcher;

    /// <summary>
    /// A watcher reports everything already connected before it reports anything new, and those are
    /// not events -- announcing them would put a card on the island for every paired headset every
    /// time MajikUtils started. Announcements begin only once that first sweep is done.
    /// </summary>
    private bool _enumerated;

    public event EventHandler<SystemEvent>? Occurred;

    public void Start()
    {
        if (_watcher is not null)
            return;

        try
        {
            _watcher = DeviceInformation.CreateWatcher(
                ConnectedSelector,
                ["System.Devices.Aep.IsConnected"],
                DeviceInformationKind.AssociationEndpoint);

            _watcher.Added += OnAdded;
            _watcher.Removed += OnRemoved;
            _watcher.EnumerationCompleted += (_, _) => _enumerated = true;

            _watcher.Start();
        }
        catch (Exception)
        {
            // No Bluetooth radio, a service that will not start, a platform that refuses the
            // selector -- one fewer kind of announcement, not a broken island.
            _watcher = null;
        }
    }

    public void Stop()
    {
        if (_watcher is null)
            return;

        try
        {
            if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                _watcher.Stop();
        }
        catch (Exception)
        {
            // Already stopping or already gone; either way there is nothing left to do.
        }

        _watcher = null;
        _enumerated = false;
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation device)
    {
        if (_enumerated)
            Announce("Connected", device.Name);
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        // An update carries only the id -- the name is not on it -- so a disconnection is announced
        // without one rather than with a GUID nobody would recognise.
        if (_enumerated)
            Announce("Bluetooth disconnected", string.Empty);
    }

    private void Announce(string label, string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            Occurred?.Invoke(this, new SystemEvent(label, "\uE702"));
        else
            Occurred?.Invoke(this, new SystemEvent(label, "\uE702", detail));
    }

    public void Dispose() => Stop();
}
