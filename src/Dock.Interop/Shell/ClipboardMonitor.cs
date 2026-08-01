using System.Runtime.InteropServices;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

/// <summary>
/// Owns Dock's two global, always-on hooks: the clipboard-format listener that feeds clipboard
/// history, and the Ctrl+Alt+Shift+V hotkey that summons it.
///
/// Both live on a message-only window of their own rather than on a panel, because the panels
/// spend nearly all their time minimised and a hook that only fired while a window was on screen
/// would miss every copy the user actually wants captured.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private const int ClipboardHotkeyId = 1;

    private NativeMethods.WndProcDelegate? _wndProcDelegate;
    private IntPtr _hwnd;

    /// <summary>Raised on the thread that called <see cref="Start"/>.</summary>
    public event Action? ClipboardChanged;

    public event Action? HotkeyPressed;

    public void Start()
    {
        _wndProcDelegate = WndProc;
        var wndClass = new NativeMethods.WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = NativeMethods.GetModuleHandle(null),
            lpszClassName = "DockClipboardMonitor_" + Guid.NewGuid().ToString("N")
        };

        NativeMethods.RegisterClass(ref wndClass);
        _hwnd = NativeMethods.CreateWindowEx(0, wndClass.lpszClassName, string.Empty, 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

        NativeMethods.AddClipboardFormatListener(_hwnd);
        NativeMethods.RegisterHotKey(_hwnd, ClipboardHotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, NativeMethods.VK_V);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            ClipboardChanged?.Invoke();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == ClipboardHotkeyId)
        {
            HotkeyPressed?.Invoke();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        NativeMethods.RemoveClipboardFormatListener(_hwnd);
        NativeMethods.UnregisterHotKey(_hwnd, ClipboardHotkeyId);
        NativeMethods.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }
}
