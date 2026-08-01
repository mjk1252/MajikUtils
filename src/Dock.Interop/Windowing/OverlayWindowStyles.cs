using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

/// <summary>
/// The extended styles that turn an ordinary WPF window into a passive overlay: present on screen,
/// but never in the user's way. WPF exposes none of these.
/// </summary>
public static class OverlayWindowStyles
{
    /// <summary>
    /// Marks a window as never-activated and Alt+Tab-invisible. Both matter for a HUD that sits
    /// where the pointer passes: without WS_EX_NOACTIVATE, brushing it would pull focus out of
    /// whatever the user is typing into.
    /// </summary>
    public static void MakePassiveOverlay(IntPtr hwnd) =>
        Update(hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW, add: true);

    /// <summary>
    /// Lets clicks pass through to whatever is underneath. Toggled rather than set once: the media
    /// island is click-through while it is only being looked at, and solid while its transport
    /// buttons are showing.
    /// </summary>
    public static void SetClickThrough(IntPtr hwnd, bool enabled) =>
        Update(hwnd, NativeMethods.WS_EX_TRANSPARENT, add: enabled);

    /// <summary>
    /// Lifts WS_EX_NOACTIVATE for as long as something in the overlay needs real keyboard focus --
    /// typing into a text box requires Win32 focus, which a never-activated window can never hold.
    /// Callers are expected to put the style back the moment that need ends.
    /// </summary>
    public static void SetActivatable(IntPtr hwnd, bool activatable) =>
        Update(hwnd, NativeMethods.WS_EX_NOACTIVATE, add: !activatable);

    private static void Update(IntPtr hwnd, int styles, bool add)
    {
        if (hwnd == IntPtr.Zero)
            return;

        // Through uint: widening a style constant straight to long sign-extends it, which for a
        // style with the top bit set would clear every high bit of the existing value instead.
        var mask = (long)(uint)styles;

        var current = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var updated = add ? current | mask : current & ~mask;

        if (updated != current)
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)updated);
    }
}
