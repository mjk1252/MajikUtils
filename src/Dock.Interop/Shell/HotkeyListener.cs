using System.Runtime.InteropServices;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

/// <summary>
/// One message-only window backing every global hotkey Dock registers -- originally just the
/// clipboard-history one, now also the command palette's, and built to take a third without
/// anyone touching this file again.
///
/// A single window rather than one per hotkey: <c>RegisterHotKey</c> only cares that the handle
/// belongs to a thread with a message loop, and multiplexing every id through one <c>WndProc</c>
/// costs nothing that registering a second window would not also cost.
/// </summary>
public sealed class HotkeyListener : IDisposable
{
    private NativeMethods.WndProcDelegate? _wndProcDelegate;
    private IntPtr _hwnd;
    private readonly HashSet<int> _registered = [];

    /// <summary>Raised with the id passed to <see cref="Register"/>, on the thread that called <see cref="Start"/>.</summary>
    public event Action<int>? HotkeyPressed;

    public void Start()
    {
        _wndProcDelegate = WndProc;
        var wndClass = new NativeMethods.WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = NativeMethods.GetModuleHandle(null),
            lpszClassName = "MajikUtilsHotkeyListener_" + Guid.NewGuid().ToString("N")
        };

        NativeMethods.RegisterClass(ref wndClass);
        _hwnd = NativeMethods.CreateWindowEx(0, wndClass.lpszClassName, string.Empty, 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
    }

    /// <summary>
    /// (Re)registers a hotkey under this id, replacing whatever it was bound to before -- the
    /// caller does not have to know the previous binding to change it.
    /// </summary>
    public bool Register(int id, uint modifiers, uint key)
    {
        if (_hwnd == IntPtr.Zero)
            return false;

        Unregister(id);

        if (!NativeMethods.RegisterHotKey(_hwnd, id, modifiers, key))
            return false;

        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_hwnd == IntPtr.Zero || !_registered.Remove(id))
            return;

        NativeMethods.UnregisterHotKey(_hwnd, id);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        foreach (var id in _registered)
            NativeMethods.UnregisterHotKey(_hwnd, id);

        _registered.Clear();

        NativeMethods.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }
}
