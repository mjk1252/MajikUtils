using System.Diagnostics;
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

    public void EndTask(IReadOnlyList<IntPtr> handles, IReadOnlyList<int> processIds)
    {
        // Ask nicely first (equivalent to clicking the window's close button), then fall back
        // to a hard kill for whatever's still alive after giving apps a moment to shut down.
        foreach (var handle in handles)
            NativeMethods.PostMessage(handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

        Task.Delay(1500).ContinueWith(_ =>
        {
            foreach (var processId in processIds)
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    if (!process.HasExited)
                        process.Kill();
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                }
            }
        });
    }
}
