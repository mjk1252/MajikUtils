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
    /// True when the foreground window covers the whole of the named monitor -- a game, a
    /// full-screen video, a presentation. An empty name means the primary.
    ///
    /// A topmost overlay pinned to the top of that screen would otherwise draw straight over them,
    /// so the media island uses this to take itself off-screen. Measured by rectangle rather
    /// than by any window style, because full-screen is not one thing: exclusive full-screen,
    /// borderless windows and full-screen browser tabs all reach it differently and only agree on
    /// the result.
    /// </summary>
    public static bool IsFullScreenOn(string? deviceName)
    {
        var hwnd = NativeMethods.GetForegroundWindow();

        // The desktop always fills the screen, and is the foreground window whenever nothing else
        // is -- treating it as full-screen would hide the island on an empty desktop.
        if (hwnd == IntPtr.Zero || hwnd == NativeMethods.GetShellWindow())
            return false;

        var target = MonitorPlacement.Resolve(deviceName);
        if (NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST) != target)
            return false;

        if (!MonitorPlacement.TryGetBounds(target, out var screen) ||
            !NativeMethods.GetWindowRect(hwnd, out var window))
        {
            return false;
        }

        var coversMonitor =
            window.Left <= screen.Left && window.Top <= screen.Top &&
            window.Right >= screen.Right && window.Bottom >= screen.Bottom;

        if (!coversMonitor)
            return false;

        // Covering the monitor is not enough, and assuming it was is a bug that only shows up on a
        // machine with the taskbar auto-hidden. Ordinarily a maximised window stops at the taskbar
        // and so falls short of the monitor's bounds by its height; hide the taskbar and every
        // maximised window covers the screen exactly, and the island went away behind all of them.
        //
        // A title bar is what tells the two apart. Going full-screen means dropping the caption --
        // exclusive full-screen, borderless windows and full-screen video all do it -- while a
        // maximised window keeps its caption however much of the screen it covers.
        var style = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);

        return (style & NativeMethods.WS_CAPTION) != NativeMethods.WS_CAPTION;
    }
}
