using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Dock.Core.Models;
using Dock.Core.Services;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Dock.Interop.Shell;

/// <summary>
/// Reads which applications are using the camera, out of the capability manager's own bookkeeping.
///
/// Windows records every grant under <c>ConsentStore</c>, one subkey per application per device,
/// each carrying a <c>LastUsedTimeStart</c> and a <c>LastUsedTimeStop</c>. The whole of the
/// detection is that **a stop time of zero means the device is open right now**.
///
/// The microphone lives one key over and is deliberately left alone. Reading it on a real machine
/// returns an audio routing service that took it at boot and a chat client that holds it for as
/// long as it is running -- true readings of a question nobody was asking. The camera is the one
/// that actually turns off again, which is what makes it worth putting on the island.
///
/// Two limits worth stating, because neither is a bug to be fixed later:
///
/// - This reports what the capability manager sees. An application reaching the camera through a
///   legacy or virtual driver path may never appear here at all.
/// - The stop time is written when the device is released, which is not the moment the
///   application's own UI suggests it was. The indicator is truthful about the device, not about
///   anybody's intent.
/// </summary>
public sealed class DeviceUsageMonitor : IDeviceUsageSource, IDisposable
{
    /// <summary>
    /// Still called "webcam" in the registry, long after the shell stopped calling it that
    /// anywhere a person can see.
    /// </summary>
    private const string CameraKey =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    /// <summary>
    /// Win32 apps live under here, keyed by executable path with '#' standing in for the path
    /// separator. Everything else at the top level is a package family name.
    /// </summary>
    private const string NonPackaged = "NonPackaged";

    private const int RegNotifyChangeName = 0x1;
    private const int RegNotifyChangeLastSet = 0x4;

    /// <summary>
    /// Without this the registration belongs to the thread that made it and dies with it. The
    /// watcher thread outlives each individual wait, but saying so explicitly is what keeps
    /// re-arming safe.
    /// </summary>
    private const int RegNotifyThreadAgnostic = 0x10000000;

    /// <summary>
    /// Only used where the notification could not be set up. The key is a few dozen values, so
    /// polling it is cheap -- it is the responsiveness that suffers, not the machine.
    /// </summary>
    private static readonly TimeSpan FallbackPollInterval = TimeSpan.FromSeconds(2);

    private readonly ManualResetEvent _stop = new(false);

    /// <summary>
    /// Auto-reset, so a signal consumed by the wait leaves the handle ready for the next
    /// registration rather than firing the loop straight through a second time.
    /// </summary>
    private readonly AutoResetEvent _changed = new(false);

    private RegistryKey? _key;
    private Thread? _thread;
    private Timer? _fallbackTimer;
    private IReadOnlyList<DeviceUsage> _last = [];
    private bool _disposed;

    public event EventHandler<IReadOnlyList<DeviceUsage>>? Changed;

    public void Start()
    {
        if (_disposed || _thread is not null || _fallbackTimer is not null)
            return;

        _key = TryOpen();

        // Publish what is true now rather than waiting for the first change: an application that
        // was already on camera when MajikUtils started would otherwise go unnoticed until it
        // stopped.
        Publish();

        if (_key is not null && Arm())
        {
            _thread = new Thread(WatchLoop)
            {
                IsBackground = true,
                Name = "MajikUtils camera usage"
            };

            _thread.Start();
            return;
        }

        // Whatever went wrong, a feature that updates slowly beats one that never updates.
        _fallbackTimer = new Timer(_ => Publish(), null, FallbackPollInterval, FallbackPollInterval);
    }

    public void Stop()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;

        _fallbackTimer?.Dispose();
        _fallbackTimer = null;

        _key?.Dispose();
        _key = null;
    }

    private static RegistryKey? TryOpen()
    {
        try
        {
            // HKCU only. Packaged apps and Win32 apps alike record here; the machine hive carries
            // services, which have no application to name on the island anyway.
            return Registry.CurrentUser.OpenSubKey(CameraKey);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks for one notification. These are one-shot, so every signal has to be followed by another
    /// call before the next change would be seen.
    /// </summary>
    private bool Arm() =>
        _key is not null && RegNotifyChangeKeyValue(
            _key.Handle,
            watchSubtree: true,
            RegNotifyChangeName | RegNotifyChangeLastSet | RegNotifyThreadAgnostic,
            _changed.SafeWaitHandle,
            asynchronous: true) == 0;

    /// <summary>
    /// Blocked until something moves. Waiting on the stop event alongside the change is what lets
    /// Stop() get the thread back without an interrupt.
    /// </summary>
    private void WatchLoop()
    {
        WaitHandle[] handles = [_changed, _stop];

        while (WaitHandle.WaitAny(handles) == 0)
        {
            // Re-armed before reading, so a change landing during the read is not missed.
            if (!Arm())
                return;

            Publish();
        }
    }

    private void Publish()
    {
        var current = Collect();

        if (Same(_last, current))
            return;

        _last = current;
        Changed?.Invoke(this, current);
    }

    private List<DeviceUsage> Collect()
    {
        List<DeviceUsage> found = [];

        if (_key is null)
            return found;

        foreach (var name in SafeSubKeyNames(_key))
        {
            if (string.Equals(name, NonPackaged, StringComparison.OrdinalIgnoreCase))
            {
                using var nonPackaged = TryOpenSub(_key, name);
                if (nonPackaged is null)
                    continue;

                foreach (var appKey in SafeSubKeyNames(nonPackaged))
                {
                    if (!InUse(nonPackaged, appKey))
                        continue;

                    var path = appKey.Replace('#', '\\');
                    found.Add(new DeviceUsage(path, DescribeExecutable(path)));
                }

                continue;
            }

            if (InUse(_key, name))
                found.Add(new DeviceUsage(string.Empty, DescribePackage(name)));
        }

        return found;
    }

    private static string[] SafeSubKeyNames(RegistryKey key)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static RegistryKey? TryOpenSub(RegistryKey parent, string name)
    {
        try
        {
            return parent.OpenSubKey(name);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The detection, entire. A zero stop time against a real start time is an application that
    /// took the camera and has not given it back.
    /// </summary>
    private static bool InUse(RegistryKey parent, string name)
    {
        using var key = TryOpenSub(parent, name);
        if (key is null)
            return false;

        try
        {
            return key.GetValue("LastUsedTimeStop") is long and 0
                && key.GetValue("LastUsedTimeStart") is long start && start > 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// What to call a Win32 application. Its file description is the name a person would recognise
    /// -- "Microsoft Teams" rather than "ms-teams" -- and the filename is the fallback for anything
    /// that shipped without version information.
    /// </summary>
    private static string DescribeExecutable(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                return description.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            // The executable has been moved or deleted since it last used the camera.
        }

        return Path.GetFileNameWithoutExtension(path) is { Length: > 0 } name ? name : path;
    }

    /// <summary>
    /// What to call a packaged application. A package family name is all there is to work with here
    /// -- "Microsoft.WindowsCamera_8wekyb3d8bbwe" -- so the publisher hash and the vendor prefix
    /// come off and the rest is shown as-is. Rough, and the minority case.
    /// </summary>
    private static string DescribePackage(string familyName)
    {
        var name = familyName.Split('_')[0];
        var lastDot = name.LastIndexOf('.');

        return lastDot >= 0 && lastDot < name.Length - 1 ? name[(lastDot + 1)..] : name;
    }

    private static bool Same(IReadOnlyList<DeviceUsage> left, IReadOnlyList<DeviceUsage> right)
    {
        if (left.Count != right.Count)
            return false;

        // Registry enumeration order is stable between reads, so position is a fair comparison and
        // saves sorting a list that is almost always empty.
        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();

        _stop.Dispose();
        _changed.Dispose();
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        int notifyFilter,
        SafeWaitHandle notifyEvent,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
