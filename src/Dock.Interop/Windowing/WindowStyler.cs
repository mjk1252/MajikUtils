using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

public static class WindowStyler
{
    public static void MakeNonActivatingToolWindow(IntPtr hwnd)
    {
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    public static void ApplyAcrylicBackdrop(IntPtr hwnd)
    {
        var backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        var cornerPreference = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    public static void ApplyPillRegion(IntPtr hwnd, int widthPx, int heightPx)
    {
        var region = NativeMethods.CreateRoundRectRgn(0, 0, widthPx, heightPx, heightPx, heightPx);
        NativeMethods.SetWindowRgn(hwnd, region, true);
    }

    public static void SetWindowPosition(IntPtr hwnd, int x, int y)
    {
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }
}
