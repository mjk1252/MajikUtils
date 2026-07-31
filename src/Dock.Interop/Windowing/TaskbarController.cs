using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

/// <summary>
/// Hides/restores the native Windows taskbar (primary + one Shell_SecondaryTrayWnd per extra
/// monitor). Callers are expected to pair Hide() with a crash-safe restore path (see
/// Dock.Guard) since a stuck-hidden taskbar with no dock running would strand the user.
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

    private static void SetVisible(bool visible)
    {
        var cmd = visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE;

        var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
            NativeMethods.ShowWindow(primary, cmd);

        var secondary = IntPtr.Zero;
        while ((secondary = NativeMethods.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(secondary, cmd);
        }
    }
}
