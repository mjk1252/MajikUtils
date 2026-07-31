using System.Runtime.InteropServices;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

public sealed class TrayIconService : IDisposable
{
    private const uint WM_TRAYICON = NativeMethods.WM_APP + 1;
    private const uint IconId = 1;

    private NativeMethods.WndProcDelegate? _wndProcDelegate;
    private IntPtr _hwnd;
    private bool _iconAdded;

    public event Action? LeftClicked;
    public event Action? RightClicked;

    public void Show(IntPtr hIcon, string tooltip)
    {
        CreateMessageWindow();

        var data = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = NativeMethods.NIF_ICON | NativeMethods.NIF_MESSAGE | NativeMethods.NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = tooltip
        };

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        _iconAdded = true;
    }

    private void CreateMessageWindow()
    {
        _wndProcDelegate = WndProc;
        var wndClass = new NativeMethods.WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = NativeMethods.GetModuleHandle(null),
            lpszClassName = "DockTrayIconWindow_" + Guid.NewGuid().ToString("N")
        };

        NativeMethods.RegisterClass(ref wndClass);
        _hwnd = NativeMethods.CreateWindowEx(0, wndClass.lpszClassName, string.Empty, 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = (uint)lParam.ToInt64();
            if (mouseMsg == NativeMethods.WM_LBUTTONUP)
                LeftClicked?.Invoke();
            else if (mouseMsg == NativeMethods.WM_RBUTTONUP)
                RightClicked?.Invoke();

            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_iconAdded)
        {
            var data = new NativeMethods.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = IconId,
                szTip = string.Empty
            };
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        }

        if (_hwnd != IntPtr.Zero)
            NativeMethods.DestroyWindow(_hwnd);
    }
}
