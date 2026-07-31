using System.Diagnostics;
using System.Text;
using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

public sealed class RunningWindowSource : IRunningAppSource, IDisposable
{
    private readonly Timer _timer;

    public event EventHandler<IReadOnlyList<RunningAppGroup>>? Updated;

    public RunningWindowSource()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(0, 1000);

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    private void Poll()
    {
        var groups = BuildGroups(EnumerateTopLevelWindows());
        Updated?.Invoke(this, groups);
    }

    private static List<(IntPtr Handle, string Title, uint ProcessId)> EnumerateTopLevelWindows()
    {
        var list = new List<(IntPtr, string, uint)>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
                return true;

            var exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return true;

            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            var length = NativeMethods.GetWindowTextLength(hWnd);
            if (length == 0)
                return true;

            var buffer = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hWnd, buffer, buffer.Capacity);
            var title = buffer.ToString();
            if (string.IsNullOrWhiteSpace(title))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
            list.Add((hWnd, title, processId));
            return true;
        }, IntPtr.Zero);

        return list;
    }

    private static List<RunningAppGroup> BuildGroups(List<(IntPtr Handle, string Title, uint ProcessId)> windows)
    {
        var groups = new Dictionary<string, RunningAppGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var (handle, title, processId) in windows)
        {
            string? path;
            try
            {
                path = Process.GetProcessById((int)processId).MainModule?.FileName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                continue;
            }

            if (string.IsNullOrEmpty(path))
                continue;

            if (!groups.TryGetValue(path, out var group))
            {
                group = new RunningAppGroup
                {
                    ProcessPath = path,
                    DisplayName = Path.GetFileNameWithoutExtension(path)
                };
                groups[path] = group;
            }

            group.Windows.Add(new RunningWindow { Handle = handle, Title = title });
        }

        return [.. groups.Values];
    }

    public void Dispose() => _timer.Dispose();
}
