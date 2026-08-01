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

    public static void ApplyRoundedRegion(IntPtr hwnd, int widthPx, int heightPx, int cornerRadiusPx)
    {
        // CreateRoundRectRgn's last two params are the corner ellipse's width/height (diameter),
        // not its radius -- pass the radius directly and every corner comes out half as round as
        // intended, leaving a squared-off notch outside WPF's (correctly radius-sized) rounded
        // Border where the opaque window background peeks through.
        var cornerDiameterPx = cornerRadiusPx * 2;
        var region = NativeMethods.CreateRoundRectRgn(0, 0, widthPx, heightPx, cornerDiameterPx, cornerDiameterPx);
        NativeMethods.SetWindowRgn(hwnd, region, true);
    }

    /// <summary>
    /// Turns on DWM's acrylic blur-behind for a window and tints it with <paramref name="rgb"/> at
    /// <paramref name="alpha"/>. Returns false if the (undocumented) call failed, in which case the
    /// caller must paint an opaque background itself -- a WPF window with AllowsTransparency="False"
    /// and Background="Transparent" has per-pixel alpha that nothing composites, so DWM renders it
    /// flat black. This call is what makes that alpha meaningful.
    ///
    /// GradientColor is 0xAABBGGRR (ABGR), NOT the ARGB byte order every other colour API here uses.
    /// Alpha is floored at 1: several Windows builds skip the blur pass entirely for a fully
    /// transparent tint, which lands right back on a black window.
    /// </summary>
    public static bool EnableAcrylic(IntPtr hwnd, int rgb, byte alpha)
    {
        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        var gradientColor = (Math.Max((byte)1, alpha) << 24) | (b << 16) | (g << 8) | r;

        var accent = new NativeMethods.ACCENT_POLICY
        {
            AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 2,
            GradientColor = gradientColor,
            AnimationId = 0
        };

        var size = Marshal.SizeOf<NativeMethods.ACCENT_POLICY>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, buffer, false);
            var data = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = NativeMethods.WCA_ACCENT_POLICY,
                Data = buffer,
                SizeOfData = size
            };
            return NativeMethods.SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static void SetWindowPosition(IntPtr hwnd, int x, int y)
    {
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    public const int WM_HOTKEY = NativeMethods.WM_HOTKEY;
    public const int WM_CLIPBOARDUPDATE = NativeMethods.WM_CLIPBOARDUPDATE;

    public static bool RegisterPanicHotkey(IntPtr hwnd, int id) => NativeMethods.RegisterHotKey(
        hwnd, id, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, NativeMethods.VK_T);

    public static bool RegisterClipboardHotkey(IntPtr hwnd, int id) => NativeMethods.RegisterHotKey(
        hwnd, id, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, NativeMethods.VK_V);

    public static void UnregisterHotkey(IntPtr hwnd, int id) => NativeMethods.UnregisterHotKey(hwnd, id);

    public static void AddClipboardListener(IntPtr hwnd) => NativeMethods.AddClipboardFormatListener(hwnd);

    public static void RemoveClipboardListener(IntPtr hwnd) => NativeMethods.RemoveClipboardFormatListener(hwnd);

    /// <summary>
    /// Clears WS_EX_NOACTIVATE on a window. WPF Popups (especially AllowsTransparency=True
    /// ones, like our flyouts) inherit non-activating behavior from a non-activating owner --
    /// meaning a TextBox inside one can never receive real keyboard focus while the dock's own
    /// window stays WS_EX_NOACTIVATE. Call this on the popup's own HWND once it's open to let
    /// it actually take focus (e.g. for the app launcher's search box).
    /// </summary>
    public static void MakeActivatable(IntPtr hwnd)
    {
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle & ~NativeMethods.WS_EX_NOACTIVATE);
    }

    public static void ForceForeground(IntPtr hwnd) => NativeMethods.SetForegroundWindow(hwnd);

    /// <summary>
    /// Re-asserts the dock's own always-on-top z-order. Windows gives the foreground window a
    /// z-order boost as part of activation, which can momentarily lift it above *other*
    /// processes' topmost windows (like ours) -- most visible when the user activates or peeks
    /// another app's window via the dock. Call this right after any such activation to pull the
    /// dock back above it.
    /// </summary>
    public static void ReassertTopmost(IntPtr hwnd) => NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
        0, 0, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE);

    public static uint RegisterTaskbarCreatedMessage() => NativeMethods.RegisterWindowMessage("TaskbarCreated");

    /// <summary>
    /// Registers a live DWM thumbnail of <paramref name="sourceHwnd"/>, composited by the OS
    /// directly onto <paramref name="destinationHwnd"/>'s surface at whatever rect
    /// <see cref="ShowThumbnail"/> is given -- no z-order or focus changes on the source window
    /// are involved, unlike bringing it to the front. Returns IntPtr.Zero on failure.
    /// </summary>
    public static IntPtr RegisterThumbnail(IntPtr destinationHwnd, IntPtr sourceHwnd) =>
        NativeMethods.DwmRegisterThumbnail(destinationHwnd, sourceHwnd, out var id) == 0 ? id : IntPtr.Zero;

    public static void UnregisterThumbnail(IntPtr thumbnailId)
    {
        if (thumbnailId != IntPtr.Zero)
            NativeMethods.DwmUnregisterThumbnail(thumbnailId);
    }

    /// <summary>Rect is in the destination window's client-area device pixels.</summary>
    public static void ShowThumbnail(IntPtr thumbnailId, int left, int top, int right, int bottom)
    {
        var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new NativeMethods.RECT { Left = left, Top = top, Right = right, Bottom = bottom },
            fVisible = true,
            fSourceClientAreaOnly = true
        };
        NativeMethods.DwmUpdateThumbnailProperties(thumbnailId, ref props);
    }
}
