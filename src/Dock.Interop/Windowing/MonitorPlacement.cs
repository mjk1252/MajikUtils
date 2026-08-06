using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

/// <summary>A monitor's usable area in physical pixels, plus its own DPI scale.</summary>
public readonly record struct WorkArea(int Left, int Top, int Right, int Bottom, double Scale)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// One attached monitor: the adapter device name that identifies it across sessions, a label to
/// show a user who has never heard of <c>\\.\DISPLAY2</c>, and whether it is the primary.
/// </summary>
public readonly record struct MonitorInfo(string DeviceName, string Label, bool IsPrimary);

/// <summary>
/// Per-monitor placement, in physical pixels throughout.
///
/// WPF's <c>SystemParameters.WorkArea</c> only ever describes the primary monitor, so anything
/// positioned with it lands on the primary no matter which screen the user is working on. And
/// because Dock is PerMonitorV2 DPI aware, two monitors can be at different scales, which makes
/// WPF's DIP-based Left/Top ambiguous the moment a window crosses between them.
///
/// Working in physical pixels and calling SetWindowPos directly sidesteps both problems: physical
/// coordinates are the one space every monitor agrees on.
/// </summary>
public static class MonitorPlacement
{
    /// <summary>
    /// The work area of the monitor under the cursor. Clicking a taskbar button leaves the cursor
    /// on it, so this is also the monitor whose taskbar the click came from.
    /// </summary>
    public static WorkArea FromCursor()
    {
        var (x, y) = CursorInfo.GetPosition();
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = x, Y = y },
            NativeMethods.MONITOR_DEFAULTTONEAREST);

        return Describe(monitor);
    }

    public static WorkArea FromWindow(IntPtr hwnd) =>
        Describe(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST));

    /// <summary>
    /// The primary monitor's work area, for the one thing that does not follow the cursor: the
    /// media island hangs from the top of a screen the user picks, defaulting to this one.
    /// </summary>
    public static WorkArea FromPrimary() => Describe(PrimaryMonitor);

    /// <summary>
    /// The work area of the monitor with this device name, falling back to the primary when the
    /// name is empty or names a screen that is no longer attached -- unplugging the monitor the
    /// island was pinned to should move it back into view, not leave it drawing into nowhere.
    /// </summary>
    public static WorkArea FromDeviceName(string? deviceName) => Describe(Resolve(deviceName));

    /// <summary>
    /// Every attached monitor, in the order Windows reports them, for the settings picker.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr monitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            if (TryDescribeDevice(monitor, out var info))
            {
                monitors.Add(new MonitorInfo(
                    info.szDevice,
                    $"Monitor {monitors.Count + 1} ({info.rcMonitor.Right - info.rcMonitor.Left}" +
                    $"×{info.rcMonitor.Bottom - info.rcMonitor.Top})",
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            }

            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    internal static IntPtr PrimaryMonitor =>
        NativeMethods.MonitorFromPoint(default, NativeMethods.MONITOR_DEFAULTTOPRIMARY);

    /// <summary>The monitor with this device name, or the primary if there is no such screen.</summary>
    internal static IntPtr Resolve(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
            return PrimaryMonitor;

        var match = IntPtr.Zero;

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr monitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            if (TryDescribeDevice(monitor, out var info) &&
                string.Equals(info.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                match = monitor;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return match != IntPtr.Zero ? match : PrimaryMonitor;
    }

    private static bool TryDescribeDevice(IntPtr monitor, out NativeMethods.MONITORINFOEX info)
    {
        info = new NativeMethods.MONITORINFOEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
        };

        return NativeMethods.GetMonitorInfo(monitor, ref info);
    }

    /// <summary>A monitor's full bounds, taskbar included -- unlike <see cref="WorkArea"/>.</summary>
    internal static bool TryGetBounds(IntPtr monitor, out NativeMethods.RECT bounds)
    {
        var info = new MONITORINFOInitialised();
        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info.Value))
        {
            bounds = info.Value.rcMonitor;
            return true;
        }

        bounds = default;
        return false;
    }

    private static WorkArea Describe(IntPtr monitor)
    {
        var info = new MONITORINFOInitialised();
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info.Value))
            return new WorkArea(0, 0, 1920, 1080, 1.0);

        var scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
            scale = dpiX / 96.0;

        var work = info.Value.rcWork;
        return new WorkArea(work.Left, work.Top, work.Right, work.Bottom, scale);
    }

    /// <summary>Moves and sizes a window in physical pixels, without activating or reordering it.</summary>
    public static void SetPhysicalBounds(IntPtr hwnd, int x, int y, int width, int height) =>
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

    /// <summary>GetMonitorInfo rejects a struct whose cbSize it did not set itself.</summary>
    private struct MONITORINFOInitialised()
    {
        public NativeMethods.MONITORINFO Value =
            new() { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
    }
}
