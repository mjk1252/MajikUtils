using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Dock.Core.Services;
using Microsoft.Win32;

namespace Dock.Interop.Shell;

/// <summary>
/// Notices the momentary things: a download finishing, a screenshot landing, a removable drive
/// appearing, the network coming or going.
///
/// Four unrelated mechanisms behind one event, because the island does the same thing with all of
/// them. Everything here is best-effort -- a watcher that will not start costs one kind of
/// announcement, not the feature.
/// </summary>
public sealed class SystemEventSource : ISystemEventSource, IDisposable
{
    /// <summary>
    /// The half-written files browsers leave behind while a download is in flight. Watching for one
    /// of these *disappearing* is what makes this work across every browser at once without
    /// integrating with any of them: Chrome and Edge rename <c>.crdownload</c> to the real name on
    /// completion, Firefox does the same with <c>.part</c>.
    ///
    /// Every one of these is an unambiguous marker of a download in progress. <c>.tmp</c> was here
    /// and has been taken out: installers, Office and half of everything else write one, so a
    /// <c>.tmp</c> that lived a moment and then vanished was announcing "Download finished" for
    /// something that was never a download. No mainstream browser uses it as its in-flight
    /// extension, so nothing is lost.
    /// </summary>
    private static readonly string[] PartialExtensions =
        [".crdownload", ".part", ".partial", ".download"];

    /// <summary>
    /// Long enough for a rename to settle, short enough that the announcement still feels like a
    /// response. A partial file that vanishes because the download was *cancelled* also lands here;
    /// there is no way to tell the two apart from the file system alone, and a cancelled download
    /// announcing itself is a smaller wrong than a finished one staying silent.
    /// </summary>
    private static readonly TimeSpan DownloadSettle = TimeSpan.FromMilliseconds(700);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a freshly connected network is given to produce a name before it is announced
    /// without one. A wireless interface reports itself available a second or two before it has
    /// finished associating, and announcing twice -- once anonymously, once with the name -- is
    /// worse than waiting.
    /// </summary>
    private static readonly TimeSpan AssociateGrace = TimeSpan.FromSeconds(6);

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, DateTimeOffset> _pendingDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    private Timer? _pollTimer;
    private int _monitors;
    private NetworkState _network;
    private DateTimeOffset? _connectingSince;
    private readonly Lock _networkGate = new();
    private HashSet<string> _drives = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;

    public event EventHandler<SystemEvent>? Occurred;

    public void Start()
    {
        if (_started)
            return;

        _started = true;

        WatchDownloads();
        WatchScreenshots();
        WatchDrives();

        _network = ReadNetwork();

        // Subscribed for speed, not for correctness: when these do fire they bring the next check
        // forward, but nothing depends on them arriving. See PublishNetwork.
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;

        _monitors = MonitorCount();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Stop()
    {
        if (!_started)
            return;

        _started = false;

        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();

        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    // ---- Downloads --------------------------------------------------------------------------

    private void WatchDownloads()
    {
        // Asked of the shell rather than assembled from the profile directory: Downloads can be
        // relocated to another drive from its Properties, and a watcher pointed at the old path
        // never fires and never complains.
        //
        // Recursive, because "ask where to save each file" lets a download land in any subfolder
        // and plenty of people keep their downloads sorted into some. It costs little: only the
        // partial extensions above ever produce an announcement, so ordinary files appearing
        // anywhere under here -- an archive being extracted, say -- are seen and ignored.
        var watcher = TryWatch(KnownFolders.DownloadsPath(), includeSubdirectories: true);
        if (watcher is null)
            return;

        watcher.Created += (_, e) => TrackPartial(e.FullPath);
        watcher.Renamed += (_, e) =>
        {
            // The completion signal: the partial file becomes the real one.
            if (IsPartial(e.OldFullPath) && !IsPartial(e.FullPath))
                Announce(new SystemEvent("Download finished", "\uE896", Path.GetFileName(e.FullPath)));
            else
                TrackPartial(e.FullPath);
        };

        watcher.Deleted += (_, e) => ForgetPartial(e.FullPath);
    }

    private void TrackPartial(string path)
    {
        if (!IsPartial(path))
            return;

        lock (_gate)
            _pendingDownloads[path] = DateTimeOffset.UtcNow;
    }

    private void ForgetPartial(string path)
    {
        if (!IsPartial(path))
            return;

        bool wasTracked;
        lock (_gate)
            wasTracked = _pendingDownloads.Remove(path, out var started)
                && DateTimeOffset.UtcNow - started > DownloadSettle;

        // Some browsers delete the partial rather than renaming it, having already written the
        // finished file alongside. Only counted when the partial actually lived for a moment, so a
        // file that appeared and vanished in the same instant is not announced as a download.
        if (wasTracked)
            Announce(new SystemEvent("Download finished", "\uE896", NameWithoutPartialExtension(path)));
    }

    private static bool IsPartial(string path) =>
        PartialExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string NameWithoutPartialExtension(string path) =>
        Path.GetFileNameWithoutExtension(path);

    // ---- Screenshots ------------------------------------------------------------------------

    private void WatchScreenshots()
    {
        // Screenshots is a known folder of its own, not a fixed subfolder of Pictures: moving
        // Pictures does not move it, and moving it does not move Pictures.
        //
        // Flat, unlike Downloads: Windows always saves straight into this folder, so a recursive
        // watch here would only widen what can go wrong.
        var watcher = TryWatch(KnownFolders.ScreenshotsPath(), includeSubdirectories: false);
        if (watcher is null)
            return;

        watcher.Created += (_, e) =>
            Announce(new SystemEvent("Screenshot captured", "\uE722", Path.GetFileName(e.FullPath)));
    }

    private FileSystemWatcher? TryWatch(string? path, bool includeSubdirectories)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return null;

            var watcher = new FileSystemWatcher(path)
            {
                // Names only: content writes fire continuously while a download is in flight, and
                // none of them are the moment worth announcing. It also keeps the recursive watch
                // below cheap, since the buffer only ever carries renames and creations.
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = includeSubdirectories,
                EnableRaisingEvents = true
            };

            _watchers.Add(watcher);
            return watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---- Removable drives -------------------------------------------------------------------

    /// <summary>
    /// Polled, deliberately. Volume arrival is delivered as a <c>WM_DEVICECHANGE</c> broadcast to
    /// *top-level* windows, and a message-only window -- which is what a background watcher wants
    /// to be -- never receives broadcasts at all. Diffing the drive list every couple of seconds
    /// gets the same answer with none of that, and nobody notices a two-second delay on a USB stick.
    /// </summary>
    private void WatchDrives()
    {
        _drives = ReadDrives();

        // One timer for both polled watchers. Drives and the network are polled for the same
        // reason -- the push notification for each covers less than it appears to -- so they may
        // as well share the tick.
        _pollTimer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    private void Poll()
    {
        PollDrives();
        PublishNetwork();
    }

    private void PollDrives()
    {
        var current = ReadDrives();

        foreach (var added in current.Except(_drives))
            Announce(new SystemEvent("Drive connected", "\uE88E", added));

        foreach (var removed in _drives.Except(current))
            Announce(new SystemEvent("Drive removed", "\uE8A7", removed));

        _drives = current;
    }

    private static HashSet<string> ReadDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType is DriveType.Removable or DriveType.CDRom && d.IsReady)
                .Select(d => d.Name.TrimEnd('\\'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // ---- Displays ---------------------------------------------------------------------------

    /// <summary>
    /// Only the count is watched, not the mode. Resolution changes fire this constantly -- every
    /// full-screen game launching and quitting raises one -- and none of those are news. A monitor
    /// appearing or going away is, particularly on a laptop being docked.
    /// </summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var count = MonitorCount();
        if (count == _monitors || count == 0)
            return;

        var added = count > _monitors;
        _monitors = count;

        Announce(new SystemEvent(
            added ? "Display connected" : "Display disconnected",
            "",
            count == 1 ? "1 screen" : $"{count} screens"));
    }

    private static int MonitorCount() => GetSystemMetrics(SM_CMONITORS);

    /// <summary>Number of display monitors on the desktop.</summary>
    private const int SM_CMONITORS = 80;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    // ---- Network ----------------------------------------------------------------------------

    /// <summary>
    /// Announces the network coming, going, or becoming a different one.
    ///
    /// Driven by <c>NetworkAddressChanged</c> rather than <c>NetworkAvailabilityChanged</c>, which
    /// is the obvious choice and does not work: toggling the wireless off and back on raised no
    /// availability event at all on the machine this was written on, while the address event fired
    /// reliably both times. Availability is still subscribed as a second opinion, but nothing
    /// depends on it arriving.
    ///
    /// Both events fire in bursts -- an address arrives, then DHCP settles, then DNS -- so the
    /// reading is debounced rather than taken per event. That delay does double duty: a wireless
    /// interface is "up" a moment before it has associated, and asking for the network name too
    /// early gets nothing back.
    /// </summary>
    private void OnNetworkChanged(object? sender, EventArgs e) => PublishNetwork();

    private void PublishNetwork()
    {
        // Serialised: the poll and either event can land here at once, and two threads racing
        // through the comparison below would announce the same change twice.
        lock (_networkGate)
        {
            var current = ReadNetwork();
            if (current == _network)
                return;

            // Connected, but the wireless has not finished associating and has no name yet. Wait
            // for one rather than announcing anonymously and again a moment later -- but not
            // forever, since a wired connection never produces a name at all.
            if (current.Connected && current.Name is null && !_network.Connected)
            {
                _connectingSince ??= DateTimeOffset.UtcNow;

                if (DateTimeOffset.UtcNow - _connectingSince < AssociateGrace)
                    return;
            }

            _connectingSince = null;
            _network = current;

            Announce(current switch
            {
                { Connected: false } => new SystemEvent("Network lost", ""),
                { Name: null } => new SystemEvent("Network connected", ""),
                var connected => new SystemEvent("Connected", "", connected.Name!)
            });
        }
    }

    /// <summary>
    /// What "connected" means here: some interface has a default gateway.
    ///
    /// Two simpler tests were tried and both misreport. <c>GetIsNetworkAvailable</c> stayed true
    /// through a wireless disconnect on the machine this was written on. Looking for a non-wireless
    /// interface that is up catches a sub-second transient during the same disconnect, where
    /// something is briefly up without being usable, and announces a reconnection that never
    /// happened.
    ///
    /// A gateway is the practical definition of being on a network rather than merely having
    /// hardware switched on, it disappears the moment the association drops, and it needs no
    /// special-casing for wired, wireless or VPN.
    /// </summary>
    private static NetworkState ReadNetwork()
    {
        var name = WifiInfo.CurrentNetwork();

        try
        {
            var routed = NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && n.GetIPProperties().GatewayAddresses.Any(g => g.Address is { } address
                    && !address.Equals(System.Net.IPAddress.Any)));

            return new NetworkState(routed, name);
        }
        catch (NetworkInformationException)
        {
            return new NetworkState(name is not null, name);
        }
    }

    /// <param name="Name">The wireless network's name, or null when wired or not yet associated.</param>
    private readonly record struct NetworkState(bool Connected, string? Name);

    private void Announce(SystemEvent occurrence) => Occurred?.Invoke(this, occurrence);

    public void Dispose() => Stop();
}
