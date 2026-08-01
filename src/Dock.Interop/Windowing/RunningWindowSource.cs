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

            // No title/size filtering here anymore -- any window that clears the four structural
            // checks above (visible, no owner, not a tool window, not DWM-cloaked) counts as a
            // real app window, title or not. This intentionally lets some helper/IME windows
            // through as noise in exchange for never again silently dropping a legitimate window
            // (e.g. fullscreen/borderless games, which routinely have no title at all and were
            // getting filtered out by earlier, stricter heuristics here).
            var length = NativeMethods.GetWindowTextLength(hWnd);
            var title = "";

            if (length > 0)
            {
                var buffer = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hWnd, buffer, buffer.Capacity);
                title = buffer.ToString();
            }

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
            var path = GetProcessImagePath(processId);
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

            group.Windows.Add(new RunningWindow
            {
                Handle = handle,
                Title = string.IsNullOrWhiteSpace(title) ? group.DisplayName : title,
                ProcessId = (int)processId
            });
        }

        return [.. groups.Values];
    }

    /// <summary>
    /// Resolves a process's executable path via OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) +
    /// QueryFullProcessImageName -- the same least-privilege path Task Manager uses -- rather
    /// than <see cref="Process.MainModule"/>, which internally needs PROCESS_VM_READ. Anti-cheat
    /// systems (e.g. Riot's Vanguard, used by League of Legends) routinely block VM_READ access
    /// to the game process while still allowing this limited query, which is exactly why such
    /// games' windows were silently dropped here before: MainModule threw access-denied and the
    /// window got skipped.
    /// </summary>
    private static string? GetProcessImagePath(uint processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public void Dispose() => _timer.Dispose();
}
