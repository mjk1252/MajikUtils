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
}
