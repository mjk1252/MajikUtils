using System.Runtime.InteropServices;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

/// <summary>
/// Owns Dock's clipboard-format listener, which feeds clipboard history.
///
/// Lives on a message-only window of its own rather than on a panel, because the panels spend
/// nearly all their time minimised and a hook that only fired while a window was on screen would
/// miss every copy the user actually wants captured.
///
/// The global hotkey that used to live on this same window is <see cref="HotkeyListener"/> now --
/// split out once a second one (the command palette's) needed registering too, and a listener that
/// only knows about "the" hotkey had no way to hold two.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private NativeMethods.WndProcDelegate? _wndProcDelegate;
    private IntPtr _hwnd;

    /// <summary>Raised on the thread that called <see cref="Start"/>.</summary>
    public event Action? ClipboardChanged;

    public void Start()
    {
        _wndProcDelegate = WndProc;
        var wndClass = new NativeMethods.WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = NativeMethods.GetModuleHandle(null),
            lpszClassName = "MajikUtilsClipboardMonitor_" + Guid.NewGuid().ToString("N")
        };

        NativeMethods.RegisterClass(ref wndClass);
        _hwnd = NativeMethods.CreateWindowEx(0, wndClass.lpszClassName, string.Empty, 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

        NativeMethods.AddClipboardFormatListener(_hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            ClipboardChanged?.Invoke();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        NativeMethods.RemoveClipboardFormatListener(_hwnd);
        NativeMethods.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }
}
