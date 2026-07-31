using System.Drawing;
using System.Runtime.InteropServices;
using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

/// <summary>
/// Registers a window as a Windows AppBar -- the same mechanism the taskbar itself uses to
/// reserve screen space, so maximized windows stop at the dock's edge instead of running
/// underneath it.
/// </summary>
public enum AppBarEdge
{
    Left,
    Top,
    Right,
    Bottom
}

public static class AppBarService
{
    public static void Register(IntPtr hwnd)
    {
        var data = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = hwnd
        };
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data);
    }

    public static void Reposition(IntPtr hwnd, Rectangle monitorBounds, AppBarEdge edge, int thicknessPx)
    {
        var nativeEdge = edge switch
        {
            AppBarEdge.Left => NativeMethods.ABE_LEFT,
            AppBarEdge.Top => NativeMethods.ABE_TOP,
            AppBarEdge.Right => NativeMethods.ABE_RIGHT,
            _ => NativeMethods.ABE_BOTTOM
        };

        var data = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = hwnd,
            uEdge = nativeEdge,
            rc = ToRect(ComputeEdgeRect(monitorBounds, nativeEdge, thicknessPx))
        };

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref data);
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);
    }

    public static void Unregister(IntPtr hwnd)
    {
        var data = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = hwnd
        };
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
    }

    private static Rectangle ComputeEdgeRect(Rectangle bounds, uint edge, int thickness) => edge switch
    {
        NativeMethods.ABE_BOTTOM => new Rectangle(bounds.Left, bounds.Bottom - thickness, bounds.Width, thickness),
        NativeMethods.ABE_TOP => new Rectangle(bounds.Left, bounds.Top, bounds.Width, thickness),
        NativeMethods.ABE_LEFT => new Rectangle(bounds.Left, bounds.Top, thickness, bounds.Height),
        NativeMethods.ABE_RIGHT => new Rectangle(bounds.Right - thickness, bounds.Top, thickness, bounds.Height),
        _ => bounds
    };

    private static NativeMethods.RECT ToRect(Rectangle r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
}
