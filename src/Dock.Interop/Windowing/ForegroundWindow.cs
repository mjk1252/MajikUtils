using System.Diagnostics;
using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

public static class ForegroundWindow
{
    /// <summary>
    /// True when whatever currently holds the foreground belongs to this process.
    ///
    /// Lets a window tell "the user clicked away to another app" apart from "focus moved to one of
    /// our own popups, context menus, modal dialogs or a drag-and-drop operation" -- all of which
    /// raise Deactivated on the owner window just the same, but none of which should be treated as
    /// the user dismissing it.
    /// </summary>
    public static bool IsOwnedByThisProcess()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// True when the foreground window covers the whole primary monitor -- a game, a full-screen
    /// video, a presentation.
    ///
    /// A topmost overlay pinned to the top of the primary screen would otherwise draw straight over
    /// them, so the media island uses this to take itself off-screen. Measured by rectangle rather
    /// than by any window style, because full-screen is not one thing: exclusive full-screen,
    /// borderless windows and full-screen browser tabs all reach it differently and only agree on
    /// the result.
    /// </summary>
    public static bool IsFullScreenOnPrimary()
    {
        var hwnd = NativeMethods.GetForegroundWindow();

        // The desktop always fills the screen, and is the foreground window whenever nothing else
        // is -- treating it as full-screen would hide the island on an empty desktop.
        if (hwnd == IntPtr.Zero || hwnd == NativeMethods.GetShellWindow())
            return false;

        var primary = MonitorPlacement.PrimaryMonitor;
        if (NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST) != primary)
            return false;

        if (!MonitorPlacement.TryGetBounds(primary, out var screen) ||
            !NativeMethods.GetWindowRect(hwnd, out var window))
        {
            return false;
        }

        return window.Left <= screen.Left && window.Top <= screen.Top &&
               window.Right >= screen.Right && window.Bottom >= screen.Bottom;
    }
}
