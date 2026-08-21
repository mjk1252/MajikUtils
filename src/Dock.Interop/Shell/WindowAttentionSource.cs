using System.Diagnostics;
using System.Runtime.InteropServices;
using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

/// <summary>
/// Listens to the shell's running commentary on top-level windows, and reports the ones flashing
/// for attention.
///
/// Pushed rather than polled, which makes it the only one of the three notification sources that
/// is not a guess taken every couple of seconds. <c>RegisterShellHookWindow</c> asks the shell to
/// tell us when any window flashes, and a flash is <c>FlashWindowEx</c> underneath -- exactly what
/// a chat application does when a message arrives while it is in the background. No string is
/// parsed and nothing drawn is read, so an auto-hidden taskbar makes no difference at all.
///
/// The message id is not a fixed <c>WM_</c> constant. It comes from
/// <c>RegisterWindowMessage("SHELLHOOK")</c> at runtime, and everything else here hangs off that.
///
/// A flash has a beginning but no end: Windows says a window started flashing and never says it
/// stopped. What actually stops it is the user going to look, so the window being activated is
/// what clears it -- along with the window being destroyed, which is the other way a flash stops
/// mattering.
/// </summary>
public sealed class WindowAttentionSource : IWindowAttentionSource, IDisposable
{
    private readonly Lock _gate = new();

    /// <summary>
    /// Every window currently flashing, and what to call the app behind it. Keyed by window handle
    /// rather than by app, because it is windows that flash and are activated -- two windows of the
    /// same application flashing is one entry in the reading and two here.
    /// </summary>
    private readonly Dictionary<IntPtr, AttentionRequest> _flashing = [];

    /// <summary>
    /// Executable path per process id, because resolving one costs a process handle and the same
    /// handful of applications flash over and over. Never invalidated: a process id is not reused
    /// while its process lives, and a stale entry costs a wrong icon rather than anything worse.
    /// </summary>
    private readonly Dictionary<uint, AttentionRequest> _appCache = [];

    private NativeMethods.WndProcDelegate? _wndProcDelegate;
    private IntPtr _hwnd;
    private uint _shellHookMessage;

    public event EventHandler<IReadOnlyList<AttentionRequest>>? Changed;

    public void Start()
    {
        lock (_gate)
        {
            if (_hwnd != IntPtr.Zero)
                return;

            _wndProcDelegate = WndProc;

            var wndClass = new NativeMethods.WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = NativeMethods.GetModuleHandle(null),
                lpszClassName = "MajikUtilsShellHook_" + Guid.NewGuid().ToString("N")
            };

            NativeMethods.RegisterClass(ref wndClass);

            // Not a message-only window. A message-only window has no place in the window manager's
            // world, and the shell hook is the window manager talking about that world -- it will
            // not send to something outside it.
            _hwnd = NativeMethods.CreateWindowEx(0, wndClass.lpszClassName, string.Empty, 0,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
                return;

            _shellHookMessage = NativeMethods.RegisterWindowMessage("SHELLHOOK");
            NativeMethods.RegisterShellHookWindow(_hwnd);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_hwnd == IntPtr.Zero)
                return;

            NativeMethods.DeregisterShellHookWindow(_hwnd);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        var wasFlashing = _flashing.Count > 0;
        _flashing.Clear();

        if (wasFlashing)
            Publish();
    }

    public void Dispose() => Stop();

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != _shellHookMessage || _shellHookMessage == 0)
            return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);

        var changed = (int)wParam switch
        {
            NativeMethods.HSHELL_FLASH => BeginFlashing(lParam),

            // Going to look is what stops a flash mattering, and the shell never says a flash
            // ended -- so activation is the only signal there is that it is over.
            NativeMethods.HSHELL_WINDOWACTIVATED or
            NativeMethods.HSHELL_RUDEAPPACTIVATED or
            NativeMethods.HSHELL_WINDOWDESTROYED => _flashing.Remove(lParam),

            _ => false
        };

        if (changed)
            Publish();

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private bool BeginFlashing(IntPtr window)
    {
        if (window == IntPtr.Zero || _flashing.ContainsKey(window))
            return false;

        if (DescribeApp(window) is not { } app)
            return false;

        // Never us. The island flashing at the user about itself would be a loop with no end.
        if (app.AppUserModelId.EndsWith("MajikUtils.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        _flashing[window] = app;
        return true;
    }

    /// <summary>
    /// One entry per application rather than per window: two windows of the same app flashing is
    /// still one thing wanting you.
    /// </summary>
    private void Publish()
    {
        var apps = _flashing.Values
            .GroupBy(a => a.AppUserModelId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        Changed?.Invoke(this, apps);
    }

    /// <summary>
    /// The executable behind a window, and something to call it.
    ///
    /// The path is what identifies the app here, rather than an AppUserModelID: a flashing window
    /// is a process, and a process has a path. It happens to be what the icon lookup prefers
    /// anyway, since that tries a real file before asking the Applications folder.
    /// </summary>
    private AttentionRequest? DescribeApp(IntPtr window)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(window, out var processId);

            if (processId == 0)
                return null;

            if (_appCache.TryGetValue(processId, out var cached))
                return cached;

            using var process = Process.GetProcessById((int)processId);
            var path = process.MainModule?.FileName;

            if (string.IsNullOrEmpty(path))
                return null;

            // The product name where the executable carries one -- "Discord" rather than
            // "Discord.exe" -- and the file name where it does not.
            var description = process.MainModule?.FileVersionInfo?.FileDescription;

            var name = !string.IsNullOrWhiteSpace(description)
                ? description
                : Path.GetFileNameWithoutExtension(path);

            var app = new AttentionRequest(path, name);
            _appCache[processId] = app;

            return app;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                      or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // A process that exited between flashing and being asked about, or one this app has no
            // right to open. Either way there is nothing to show and nothing to be done.
            return null;
        }
    }
}
