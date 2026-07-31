using System.Runtime.InteropServices;
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

    /// <summary>
    /// Applies a real blur-behind with an explicitly low tint alpha. The Windows 11 public API
    /// (DWMWA_SYSTEMBACKDROP_TYPE) looks solid/opaque because it doesn't expose an opacity knob
    /// -- Windows picks it. SetWindowCompositionAttribute is older and undocumented but still
    /// fully functional, and lets us control exactly how see-through the glass is.
    /// </summary>
    public static void ApplyAcrylicBackdrop(IntPtr hwnd)
    {
        var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        const int alpha = 0x28;   // ~16% -- low enough to read as genuinely translucent
        const int rgb = 0x1E1E1E; // neutral dark-gray tint, close to Windows' own dark-mode surfaces
        var gradientColor = (alpha << 24) | rgb;

        var accent = new NativeMethods.ACCENT_POLICY
        {
            AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = gradientColor
        };

        var accentSize = Marshal.SizeOf<NativeMethods.ACCENT_POLICY>();
        var accentPtr = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = NativeMethods.WCA_ACCENT_POLICY,
                Data = accentPtr,
                SizeOfData = accentSize
            };

            NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    public static void ApplyRoundedRegion(IntPtr hwnd, int widthPx, int heightPx, int cornerRadiusPx)
    {
        var region = NativeMethods.CreateRoundRectRgn(0, 0, widthPx, heightPx, cornerRadiusPx, cornerRadiusPx);
        NativeMethods.SetWindowRgn(hwnd, region, true);
    }

    public static void SetWindowPosition(IntPtr hwnd, int x, int y)
    {
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    public const int WM_HOTKEY = NativeMethods.WM_HOTKEY;

    public static bool RegisterPanicHotkey(IntPtr hwnd, int id) => NativeMethods.RegisterHotKey(
        hwnd, id, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, NativeMethods.VK_T);

    public static void UnregisterHotkey(IntPtr hwnd, int id) => NativeMethods.UnregisterHotKey(hwnd, id);

    public static uint RegisterTaskbarCreatedMessage() => NativeMethods.RegisterWindowMessage("TaskbarCreated");
}
