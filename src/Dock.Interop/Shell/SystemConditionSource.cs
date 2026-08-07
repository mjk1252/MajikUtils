using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Dock.Core.Services;
using Microsoft.Win32;

namespace Dock.Interop.Shell;

/// <summary>
/// Reads the two standing conditions worth a dot on the island.
///
/// <para><b>Do not disturb.</b> <c>SHQueryUserNotificationState</c> is the shell's own answer to
/// "may I interrupt", which is the question actually being asked. It is a plain Win32 call needing
/// no capability and no package identity -- unlike <c>FocusSessionManager</c>, which covers the
/// same ground and is a Limited Access Feature requiring a token from Microsoft.</para>
///
/// <para><b>Restart pending.</b> Windows Update creates a <c>RebootRequired</c> key when it has
/// staged something. The key's *existence* is the signal; it holds nothing worth reading.</para>
/// </summary>
public sealed class SystemConditionSource : ISystemConditionSource, IDisposable
{
    /// <summary>
    /// Neither of these changes quickly, and the poll costs one shell call and one registry probe.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Where a drive stops being fine and starts being worth a dot. Ten percent is late enough not
    /// to nag and early enough to still be fixable.
    /// </summary>
    private const int LowDiskPercent = 10;

    private const string RebootRequiredKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";

    // The states in which the shell says an interruption is unwelcome. Quiet time is Focus assist
    // and its descendants; the other three are the older "I am presenting or gaming" answers, which
    // matter for the same reason and which Windows still returns.
    private const int QunsNotPresent = 1;
    private const int QunsBusy = 2;
    private const int QunsRunningD3dFullScreen = 3;
    private const int QunsPresentationMode = 4;
    private const int QunsQuietTime = 6;

    private readonly Timer _timer;
    private SystemConditions _last;
    private bool _started;

    public event EventHandler<SystemConditions>? Changed;

    public SystemConditionSource()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;

        // Published straight away rather than one interval later: a machine already in focus mode
        // when MajikUtils starts should show it immediately, not in five seconds.
        _last = Read();
        Changed?.Invoke(this, _last);

        _timer.Change(PollInterval, PollInterval);
    }

    public void Stop()
    {
        _started = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Poll()
    {
        var current = Read();
        if (current == _last)
            return;

        _last = current;
        Changed?.Invoke(this, current);
    }

    private static SystemConditions Read() =>
        new(ReadDoNotDisturb(), ReadRestartPending(), ReadFullestDrive());

    /// <summary>
    /// The fixed drive closest to full, once it drops under the threshold. Reported as a percentage
    /// rather than a byte count because a gigabyte free means something very different on a 256GB
    /// laptop and a 4TB array.
    /// </summary>
    private static DriveSpace? ReadFullestDrive()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0)
                .Select(d => new DriveSpace(
                    d.Name.TrimEnd('\\'),
                    (int)(d.AvailableFreeSpace * 100 / d.TotalSize),
                    d.AvailableFreeSpace))
                .Where(d => d.PercentFree <= LowDiskPercent)
                .OrderBy(d => d.PercentFree)
                .Cast<DriveSpace?>()
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ReadDoNotDisturb()
    {
        // A failure means "we could not tell", which for an indicator is the same as "no" -- far
        // better than claiming the machine is silent when it is not.
        if (SHQueryUserNotificationState(out var state) != 0)
            return false;

        return state is QunsNotPresent or QunsBusy or QunsRunningD3dFullScreen
            or QunsPresentationMode or QunsQuietTime;
    }

    private static bool ReadRestartPending()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RebootRequiredKey);
            return key is not null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
