using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

/// <summary>A monitor's usable area in physical pixels, plus its own DPI scale.</summary>
public readonly record struct WorkArea(int Left, int Top, int Right, int Bottom, double Scale)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

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
