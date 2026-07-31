using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

public sealed class WindowActivator : IWindowActivator
{
    public void Activate(IntPtr handle)
    {
        if (NativeMethods.IsIconic(handle))
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);

        NativeMethods.SetForegroundWindow(handle);
    }

    public void ToggleActivate(IntPtr handle)
    {
        if (NativeMethods.GetForegroundWindow() == handle)
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_MINIMIZE);
        }
        else
        {
            Activate(handle);
        }
    }
}
