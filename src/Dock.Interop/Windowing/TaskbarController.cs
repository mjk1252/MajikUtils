using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

/// <summary>
/// Hides/restores the native Windows taskbar (primary + one Shell_SecondaryTrayWnd per extra
/// monitor). Callers are expected to pair Hide() with a crash-safe restore path (see
/// Dock.Guard) since a stuck-hidden taskbar with no dock running would strand the user.
///
/// Makes the taskbar windows fully transparent (WS_EX_LAYERED + zero alpha) rather than
/// ShowWindow(SW_HIDE) or repositioning them off-screen. Both of those were tried and ruled
/// out live: SW_HIDE causes the modern notification area's XAML-hosted content to go dormant,
/// breaking UI Automation (which Dock.Interop.Shell.ExplorerTrayReader depends on for tray
/// icons); and Windows silently ignores SetWindowPos calls from other processes that try to
/// move Shell_TrayWnd (it reports success but the window never actually moves). Zero-alpha
/// leaves the window's real position, visibility state, and hosted content completely intact
/// -- just invisible -- so tray reading keeps working AND any flyout it opens appears in the
/// correct, real screen position instead of wherever we'd have tried to move it to.
/// </summary>
public static class TaskbarController
{
    public static void Hide() => SetVisible(false);

    public static void Show() => SetVisible(true);

    /// <summary>
    /// Returns the handle of the monitor currently showing a genuine fullscreen foreground
    /// window (covering that monitor's entire bounds), or null if none -- used to auto-hide the
    /// dock during games/fullscreen video on JUST that monitor, leaving other monitors' docks
    /// alone. Deliberately does NOT use SHQueryUserNotificationState's
    /// QUNS_RUNNING_D3D_FULL_SCREEN flag: that API is a well-known false-positive source for any
    /// GPU-accelerated app (Electron apps like Discord included), since it infers "fullscreen"
    /// heuristically (and system-wide, with no per-monitor answer) rather than checking actual
    /// window geometry. Directly comparing the foreground window's rect against its own
    /// monitor's bounds is what more reliable fullscreen-detectors (e.g. TranslucentTB) do
    /// instead, and naturally answers "which monitor" along the way.
    /// </summary>
    public static IntPtr? GetFullscreenMonitor()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == NativeMethods.GetShellWindow())
            return null;

        if (NativeMethods.IsIconic(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
            return null;

        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect))
            return null;

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return null;

        var coversMonitor = windowRect.Left <= info.rcMonitor.Left && windowRect.Top <= info.rcMonitor.Top &&
                             windowRect.Right >= info.rcMonitor.Right && windowRect.Bottom >= info.rcMonitor.Bottom;

        if (!coversMonitor)
            return null;

        var buffer = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        var className = buffer.ToString();

        return className is "Progman" or "WorkerW" or "Shell_TrayWnd" ? null : monitor;
    }

    private static IEnumerable<IntPtr> GetTaskbarWindows()
    {
        var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
            yield return primary;

        var secondary = IntPtr.Zero;
        while ((secondary = NativeMethods.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return secondary;
    }

    private static void SetVisible(bool visible)
    {
        foreach (var hwnd in GetTaskbarWindows())
        {
            var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

            if (visible)
            {
                NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle & ~NativeMethods.WS_EX_LAYERED);
            }
            else
            {
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_LAYERED);
                NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA);
            }
        }
    }
}
