using System.Runtime.InteropServices;
using Dock.Core.Services;
using Microsoft.Win32;

namespace Dock.Interop.Shell;

/// <summary>
/// Reads the power state out of <c>GetSystemPowerStatus</c>.
///
/// Pushed <em>and</em> polled, because neither alone is enough: <c>PowerModeChanged</c> fires the
/// instant the charger goes in or out, which is the moment worth announcing, but it says nothing
/// while a battery quietly drains from 40% to 15%. So the event carries the transitions and a slow
/// poll carries the drift.
/// </summary>
public sealed class BatterySource : IBatterySource, IDisposable
{
    /// <summary>
    /// Slow on purpose. A percentage that moves once every few minutes needs nothing faster, and
    /// this runs for the life of the application on a machine that is trying to save power.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>BatteryFlag bit meaning the machine has no battery at all.</summary>
    private const byte NoBattery = 128;

    /// <summary>What BatteryLifePercent reads when Windows cannot say.</summary>
    private const byte UnknownPercent = 255;

    private const byte AcOnline = 1;

    private readonly Timer _timer;
    private BatteryStatus _last;
    private bool _started;
    private bool _hasLast;

    public event EventHandler<BatteryStatus>? Changed;

    public BatterySource()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
        IsPresent = ReadPresence();
    }

    public bool IsPresent { get; }

    public void Start()
    {
        if (_started || !IsPresent)
            return;

        _started = true;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Poll();
        _timer.Change(PollInterval, PollInterval);
    }

    public void Stop()
    {
        if (!_started)
            return;

        _started = false;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Charger in or out. Resume is here too because a machine that slept on mains and woke on
    /// battery never raised a StatusChange for the difference.
    /// </summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.StatusChange or PowerModes.Resume)
            Poll();
    }

    private void Poll()
    {
        if (!TryRead(out var status))
            return;

        // Only differences: the poll runs on a timer, and republishing an unchanged reading every
        // thirty seconds would put a battery announcement on the island twice a minute forever.
        if (_hasLast && status == _last)
            return;

        _hasLast = true;
        _last = status;
        Changed?.Invoke(this, status);
    }

    private static bool ReadPresence() =>
        TryRead(out _) && GetStatus() is { } raw && (raw.BatteryFlag & NoBattery) == 0;

    private static bool TryRead(out BatteryStatus status)
    {
        status = default;

        if (GetStatus() is not { } raw || (raw.BatteryFlag & NoBattery) != 0)
            return false;

        status = new BatteryStatus(
            IsCharging: raw.ACLineStatus == AcOnline,
            PercentRemaining: raw.BatteryLifePercent == UnknownPercent ? null : raw.BatteryLifePercent,

            // Reported in seconds, and -1 whenever Windows has not worked it out yet -- which it
            // always is for the first minute or so after unplugging.
            Remaining: raw.BatteryLifeTime < 0 ? null : TimeSpan.FromSeconds(raw.BatteryLifeTime));

        return true;
    }

    private static SYSTEM_POWER_STATUS? GetStatus()
    {
        try
        {
            return GetSystemPowerStatus(out var raw) ? raw : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
