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

    public static bool IsGameFullscreenActive()
    {
        return NativeMethods.SHQueryUserNotificationState(out var state) == 0
            && state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN;
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
