using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

/// <summary>
/// Hides/restores the native Windows taskbar (primary + one Shell_SecondaryTrayWnd per extra
/// monitor). Callers are expected to pair Hide() with a crash-safe restore path (see
/// Dock.Guard) since a stuck-hidden taskbar with no dock running would strand the user.
///
/// Moves the taskbar windows off-screen rather than calling ShowWindow(SW_HIDE): a genuinely
/// hidden window's XAML-hosted content (the modern notification area) goes dormant and stops
/// responding to UI Automation, which is exactly the interface Dock.Interop.Shell.
/// ExplorerTrayReader depends on to read tray icons. An off-screen window is still
/// "visible" as far as Windows and its own hosted content are concerned, just not on the
/// visible desktop, so tray reading keeps working while the dock is up.
/// </summary>
public static class TaskbarController
{
    private const int OffscreenOffset = 20000;

    public static void Hide() => MoveOffscreen();

    public static void Show() => RestorePositions();

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

    private static void MoveOffscreen()
    {
        var saved = new List<TaskbarSafety.TaskbarPosition>();

        foreach (var hwnd in GetTaskbarWindows())
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
                continue;

            saved.Add(new TaskbarSafety.TaskbarPosition(hwnd.ToInt64(), rect.Left, rect.Top));

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top + OffscreenOffset, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        TaskbarSafety.SavePositions(saved);
    }

    private static void RestorePositions()
    {
        foreach (var pos in TaskbarSafety.LoadPositions())
        {
            var hwnd = new IntPtr(pos.Handle);
            if (!NativeMethods.IsWindow(hwnd))
                continue;

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, pos.Left, pos.Top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        TaskbarSafety.ClearPositions();

        // Belt-and-suspenders: also ensure visible via the legacy show call, in case a window
        // ever ended up hidden through some other path.
        foreach (var hwnd in GetTaskbarWindows())
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
    }
}
