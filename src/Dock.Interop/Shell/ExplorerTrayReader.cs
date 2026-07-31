using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Automation;
using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

/// <summary>
/// Reads Explorer's system tray icons and relays clicks back to their owning apps.
///
/// Tries the classic path first (Explorer's ToolbarWindow32 under SysPager, read via
/// ReadProcessMemory) since it's cheap and works on older Windows builds. Newer Windows 11
/// builds render the tray through a XAML/DirectComposition surface with no classic child
/// HWNDs at all, so this falls back to UI Automation (the same public API screen readers use)
/// to enumerate icon buttons and relay clicks via synthetic input at their screen position.
/// Both paths degrade to an empty list rather than throwing if Explorer's internals don't
/// match what's expected.
/// </summary>
public sealed class ExplorerTrayReader : ITraySource, IDisposable
{
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const int NIN_SELECT = 0x0400;

    private readonly Timer _timer;

    public event EventHandler<IReadOnlyList<TrayIcon>>? Updated;

    public ExplorerTrayReader()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(0, 1500);

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    private void Poll()
    {
        List<TrayIcon> icons;
        try
        {
            icons = ReadClassic();
            if (icons.Count == 0)
                icons = ReadViaAutomation();
        }
        catch
        {
            icons = [];
        }

        Updated?.Invoke(this, icons);
    }

    // ----- Classic path: Explorer's ToolbarWindow32 under SysPager (pre-XAML tray) -----

    private static List<TrayIcon> ReadClassic()
    {
        var toolbar = FindClassicToolbar();
        if (toolbar == IntPtr.Zero)
            return [];

        NativeMethods.GetWindowThreadProcessId(toolbar, out var explorerPid);
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_QUERY_INFORMATION,
            false, explorerPid);

        if (hProcess == IntPtr.Zero)
            return [];

        try
        {
            return ReadClassicButtons(toolbar, hProcess);
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    private static IntPtr FindClassicToolbar()
    {
        var trayWnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (trayWnd == IntPtr.Zero)
            return IntPtr.Zero;

        var trayNotify = NativeMethods.FindWindowEx(trayWnd, IntPtr.Zero, "TrayNotifyWnd", null);
        if (trayNotify == IntPtr.Zero)
            return IntPtr.Zero;

        var sysPager = NativeMethods.FindWindowEx(trayNotify, IntPtr.Zero, "SysPager", null);
        if (sysPager == IntPtr.Zero)
            return IntPtr.Zero;

        return NativeMethods.FindWindowEx(sysPager, IntPtr.Zero, "ToolbarWindow32", null);
    }

    private static unsafe List<TrayIcon> ReadClassicButtons(IntPtr toolbar, IntPtr hProcess)
    {
        var result = new List<TrayIcon>();

        var count = (int)NativeMethods.SendMessage(toolbar, NativeMethods.TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0)
            return result;

        var buttonSize = (nuint)sizeof(NativeMethods.TBBUTTON);
        var remoteButton = NativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero, buttonSize,
            NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);
        if (remoteButton == IntPtr.Zero)
            return result;

        try
        {
            var buttonBuffer = new byte[buttonSize];
            var trayDataBuffer = new byte[24];

            for (var i = 0; i < count; i++)
            {
                NativeMethods.SendMessage(toolbar, NativeMethods.TB_GETBUTTON, new IntPtr(i), remoteButton);
                if (!NativeMethods.ReadProcessMemory(hProcess, remoteButton, buttonBuffer, buttonSize, out _))
                    continue;

                var dwData = new IntPtr(BitConverter.ToInt64(buttonBuffer, 16));
                if (dwData == IntPtr.Zero)
                    continue;

                if (!NativeMethods.ReadProcessMemory(hProcess, dwData, trayDataBuffer, (nuint)trayDataBuffer.Length, out _))
                    continue;

                var ownerHandle = new IntPtr(BitConverter.ToInt64(trayDataBuffer, 0));
                var iconId = BitConverter.ToUInt32(trayDataBuffer, 8);
                var callbackMessage = BitConverter.ToUInt32(trayDataBuffer, 12);

                if (ownerHandle == IntPtr.Zero || !NativeMethods.IsWindow(ownerHandle))
                    continue;

                NativeMethods.GetWindowThreadProcessId(ownerHandle, out var ownerPid);

                string? path;
                try
                {
                    path = Process.GetProcessById((int)ownerPid).MainModule?.FileName;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(path))
                    continue;

                result.Add(new TrayIcon
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    IconPng = new ShellIconProvider().GetIconPng(path, 32),
                    OwnerHandle = ownerHandle,
                    IconId = iconId,
                    CallbackMessage = callbackMessage
                });
            }
        }
        finally
        {
            NativeMethods.VirtualFreeEx(hProcess, remoteButton, 0, NativeMethods.MEM_RELEASE);
        }

        return result;
    }

    // ----- Fallback path: UI Automation over the XAML-hosted tray (modern Windows 11) -----

    private static List<TrayIcon> ReadViaAutomation()
    {
        var trayWnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (trayWnd == IntPtr.Zero)
            return [];

        AutomationElement root;
        try
        {
            root = AutomationElement.FromHandle(trayWnd);
        }
        catch
        {
            return [];
        }

        if (root is null)
            return [];

        AutomationElementCollection buttons;
        try
        {
            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            buttons = root.FindAll(TreeScope.Descendants, condition);
        }
        catch
        {
            return [];
        }

        var result = new List<TrayIcon>();

        foreach (AutomationElement button in buttons)
        {
            System.Windows.Rect rect;
            string name;
            string className;
            try
            {
                rect = button.Current.BoundingRectangle;
                name = button.Current.Name ?? "";
                className = button.Current.ClassName ?? "";
            }
            catch
            {
                continue;
            }

            // Real notification-area icons render as SystemTray.* controls (NormalButton,
            // AccentButton, AccentText, OmniButton/OmniButtonRight for volume/network/clock).
            // This excludes taskbar app buttons, Start, Widgets, and the "show desktop" sliver.
            if (!className.StartsWith("SystemTray.", StringComparison.Ordinal) ||
                className is "SystemTray.ShowDesktopButton")
                continue;

            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                continue;

            var centerX = (int)(rect.X + rect.Width / 2);
            var centerY = (int)(rect.Y + rect.Height / 2);

            result.Add(new TrayIcon
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Tray icon" : name,
                IconPng = CaptureRegion(rect),
                ClickX = centerX,
                ClickY = centerY,
                IsChevron = className == "SystemTray.NormalButton" && name.Contains("hidden icons", StringComparison.OrdinalIgnoreCase),
                IsClock = className == "SystemTray.OmniButton" && name.StartsWith("Clock", StringComparison.OrdinalIgnoreCase)
            });
        }

        return result;
    }

    private static byte[]? CaptureRegion(System.Windows.Rect rect)
    {
        try
        {
            var width = (int)Math.Ceiling(rect.Width);
            var height = (int)Math.Ceiling(rect.Height);
            if (width <= 0 || height <= 0)
                return null;

            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen((int)rect.X, (int)rect.Y, 0, 0, new Size(width, height));

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    // ----- Click relay -----

    public void Invoke(TrayIcon icon, bool rightClick)
    {
        if (icon.OwnerHandle is { } ownerHandle)
        {
            InvokeClassic(icon, ownerHandle, rightClick);
        }
        else if (icon.ClickX is int x && icon.ClickY is int y)
        {
            if (!rightClick && TryInvokeViaAutomation(icon.Name, x, y))
                return;

            InvokeViaSyntheticClick(x, y, rightClick);
        }
    }

    /// <summary>
    /// Re-finds the live automation element for this icon (cached coordinates can go stale
    /// between polls) and calls its InvokePattern directly -- the same mechanism accessibility
    /// tools use, which composition-hosted XAML controls are guaranteed to support even when
    /// they don't respond to raw synthetic mouse input.
    /// </summary>
    private static bool TryInvokeViaAutomation(string name, int approxX, int approxY)
    {
        try
        {
            var trayWnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (trayWnd == IntPtr.Zero)
                return false;

            NativeMethods.SetForegroundWindow(trayWnd);

            var root = AutomationElement.FromHandle(trayWnd);
            if (root is null)
                return false;

            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            var buttons = root.FindAll(TreeScope.Descendants, condition);

            AutomationElement? best = null;
            var bestDistance = double.MaxValue;

            foreach (AutomationElement button in buttons)
            {
                string className;
                string elementName;
                System.Windows.Rect rect;
                try
                {
                    className = button.Current.ClassName ?? "";
                    elementName = button.Current.Name ?? "";
                    rect = button.Current.BoundingRectangle;
                }
                catch
                {
                    continue;
                }

                if (!className.StartsWith("SystemTray.", StringComparison.Ordinal) || elementName != name)
                    continue;

                var cx = rect.X + rect.Width / 2;
                var cy = rect.Y + rect.Height / 2;
                var distance = Math.Abs(cx - approxX) + Math.Abs(cy - approxY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = button;
                }
            }

            if (best is null || !best.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObj))
                return false;

            ((InvokePattern)patternObj).Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void InvokeClassic(TrayIcon icon, IntPtr ownerHandle, bool rightClick)
    {
        NativeMethods.GetWindowThreadProcessId(ownerHandle, out var ownerPid);
        NativeMethods.AllowSetForegroundWindow(ownerPid);

        var mouseMsg = rightClick ? WM_RBUTTONUP : WM_LBUTTONUP;

        // Legacy convention (pre NOTIFYICON_VERSION_4): wParam = icon ID, lParam = mouse message.
        NativeMethods.PostMessage(ownerHandle, icon.CallbackMessage!.Value, new IntPtr(icon.IconId!.Value), new IntPtr(mouseMsg));

        // NOTIFYICON_VERSION_4 convention: lParam low word = notification code, high word = icon ID.
        var v4Code = rightClick ? WM_CONTEXTMENU : NIN_SELECT;
        var v4LParam = (icon.IconId.Value << 16) | (uint)(v4Code & 0xFFFF);
        NativeMethods.PostMessage(ownerHandle, icon.CallbackMessage.Value, IntPtr.Zero, new IntPtr((int)v4LParam));
    }

    private static void InvokeViaSyntheticClick(int x, int y, bool rightClick)
    {
        NativeMethods.GetCursorPos(out var original);
        NativeMethods.SetCursorPos(x, y);
        Thread.Sleep(20);

        var down = rightClick ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_LEFTDOWN;
        var up = rightClick ? NativeMethods.MOUSEEVENTF_RIGHTUP : NativeMethods.MOUSEEVENTF_LEFTUP;

        var inputs = new NativeMethods.INPUT[]
        {
            new() { type = NativeMethods.INPUT_MOUSE, u = new NativeMethods.INPUTUNION { mi = new NativeMethods.MOUSEINPUT { dwFlags = down } } },
            new() { type = NativeMethods.INPUT_MOUSE, u = new NativeMethods.INPUTUNION { mi = new NativeMethods.MOUSEINPUT { dwFlags = up } } }
        };

        NativeMethods.SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());

        Thread.Sleep(150);
        NativeMethods.SetCursorPos(original.X, original.Y);
    }

    public void Dispose() => _timer.Dispose();
}
