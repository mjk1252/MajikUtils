using System.Drawing;
using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

public static class MonitorService
{
    public static IReadOnlyList<MonitorSnapshot> GetMonitors()
    {
        var results = new List<MonitorSnapshot>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT _, IntPtr __) =>
        {
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                var bounds = ToRectangle(info.rcMonitor);
                var workArea = ToRectangle(info.rcWork);
                var isPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;

                const int MDT_EFFECTIVE_DPI = 0;
                var dpiScale = 1.0;
                if (NativeMethods.GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
                    dpiScale = dpiX / 96.0;

                results.Add(new MonitorSnapshot(bounds, workArea, isPrimary, hMonitor, dpiScale));
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }

    private static Rectangle ToRectangle(NativeMethods.RECT rect)
        => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
