using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Interop.Shell;
using Dock.Interop.Windowing;

namespace Dock.App.Views;

/// <summary>
/// Base for the two windows that own Dock's taskbar buttons.
///
/// The central constraint: a window loses its taskbar button the instant it stops being visible,
/// so a panel can never be hidden or closed while the app is running. It minimises instead --
/// which also buys the whole show/hide interaction for free, since clicking a taskbar button
/// already restores a minimised window and minimises a foreground one.
/// </summary>
public abstract class PanelWindow : Window
{
    private SettingsStore? _settingsStore;
    private bool _exiting;
    private DateTime _openedAt = DateTime.MinValue;

    protected PanelWindow()
    {
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Minimized;
        Background = System.Windows.Media.Brushes.Transparent;

        // The safe default, since a subclass's XAML overrides it: the shell only minimises a
        // window on a second click of its taskbar button if the window carries WS_MINIMIZEBOX,
        // which ResizeMode grants for everything except NoResize. A NoResize panel can be opened
        // from its button but never put away again, because that second click merely re-activates
        // a window that is already active. CanResize is fine; NoResize is the one to avoid.
        ResizeMode = ResizeMode.CanMinimize;
    }

    /// <summary>Distinct per window: this is what stops the shell grouping both buttons into one.</summary>
    protected abstract string AppId { get; }

    /// <summary>The --panel value a pinned shortcut relaunches Dock with.</summary>
    protected abstract string PanelArgument { get; }

    /// <summary>Label the shell shows for the pinned button, and the basis for the hover tooltip.</summary>
    protected abstract string DisplayName { get; }

    /// <summary>"path,index" for the pinned button's artwork, or null to fall back to the exe's own.</summary>
    protected virtual string? RelaunchIconResource => null;

    /// <summary>
    /// Extra entries for this button's right-click menu, beyond the Exit every panel gets. The
    /// commands run as fresh processes, so anything that has to reach the running instance goes
    /// through <c>--panel</c> and the single-instance pipe rather than being called directly.
    /// </summary>
    protected virtual IReadOnlyList<JumpListTask> ExtraJumpListTasks => [];

    protected static string ExePath => Environment.ProcessPath ?? "Dock.exe";

    /// <summary>
    /// Holds the panel open through a deactivation it would otherwise treat as dismissal. Needed
    /// while a drag hovers over a drop target: the dragging application owns the foreground for
    /// the whole gesture, so the panel being dropped onto looks, by every other test, abandoned.
    /// </summary>
    protected bool SuppressAutoMinimise { get; set; }

    /// <summary>
    /// Whether this panel should keep its own position across sessions. Panels that place
    /// themselves relative to their taskbar button on every open have no position to remember.
    /// </summary>
    protected virtual bool PersistsPlacement => true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        AppIdRegistrar.Stamp(
            hwnd,
            AppId,
            relaunchCommand: $"\"{ExePath}\" --panel {PanelArgument}",
            displayName: DisplayName,
            iconResource: RelaunchIconResource ?? $"{ExePath},0");

        // Right-clicking a taskbar button is where people reach for "close", and Windows' own
        // Close window entry only minimises these panels -- it has to, since a real close would
        // destroy the button. Exit Dock sits right next to it and does what that reaches for.
        JumpListBuilder.Apply(AppId, [.. ExtraJumpListTasks, new JumpListTask("Exit Dock", ExePath, "--exit")]);
    }

    /// <summary>
    /// Losing focus dismisses the panel, matching how the dock's flyouts behaved. Minimising
    /// rather than hiding is what keeps the taskbar button alive to restore from.
    ///
    /// Deferred to the next dispatcher pass because Deactivated also fires for things that are
    /// still *us* -- the stack fan's popup, a context menu, the folder picker, an in-flight
    /// drag-and-drop -- and the new foreground window isn't settled yet at the point the event is
    /// raised. Minimising immediately would rip the panel away the moment any of those opened.
    /// </summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (_exiting || WindowState != WindowState.Normal)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (_exiting || SuppressAutoMinimise || JustOpened ||
                WindowState != WindowState.Normal || ForegroundWindow.IsOwnedByThisProcess())
            {
                return;
            }

            SavePlacement();
            WindowState = WindowState.Minimized;
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Ignores the deactivation that can arrive in the instant a panel opens. Windows refuses
    /// foreground changes requested by a process the user isn't currently working in, so
    /// <c>Activate</c> can silently fail and hand us a Deactivated for a window that was never
    /// active -- without this the panel would minimise itself the moment it appeared.
    ///
    /// A grace period rather than a "has it been activated yet" flag, deliberately: when
    /// activation is refused that flag never clears, and the panel then ignores every later
    /// deactivation too, leaving it stuck open with no way to dismiss it.
    /// </summary>
    private bool JustOpened => DateTime.UtcNow - _openedAt < TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Alt+F4 and the title-bar close would destroy the taskbar button along with the window, so
    /// they are reinterpreted as "put it away". <see cref="CloseForExit"/> is the only real close.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            SavePlacement();
            WindowState = WindowState.Minimized;
            return;
        }

        base.OnClosing(e);
    }

    public void CloseForExit()
    {
        SavePlacement();
        _exiting = true;
        Close();
    }

    /// <summary>
    /// Restores this panel's last position and size, and starts persisting changes to them.
    /// Centres the window on first run, since a panel is summoned from the taskbar and has no
    /// meaningful default edge to sit against.
    /// </summary>
    public void AttachPlacementStore(SettingsStore settingsStore)
    {
        if (!PersistsPlacement)
            return;

        _settingsStore = settingsStore;

        if (settingsStore.Load().PanelPlacements.TryGetValue(AppId, out var placement) && placement.Width > 0)
        {
            Left = placement.Left;
            Top = placement.Top;
            Width = placement.Width;
            Height = placement.Height;
            return;
        }

        Left = (SystemParameters.WorkArea.Width - Width) / 2;
        Top = (SystemParameters.WorkArea.Height - Height) / 2;
    }

    /// <summary>
    /// Saved when the panel is put away rather than on every move: LocationChanged fires per pixel
    /// of a drag, and each save is a full read-modify-write of the settings file.
    /// </summary>
    private void SavePlacement()
    {
        if (_settingsStore is not { } store || WindowState != WindowState.Normal)
            return;

        var settings = store.Load();
        settings.PanelPlacements[AppId] = new PanelPlacement
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height
        };
        store.Save(settings);
    }

    /// <summary>
    /// Brings the panel up from a relaunch or a hotkey. Restore-then-Activate is deliberate:
    /// activating a minimised window does not restore it.
    ///
    /// Note this is *not* the path a taskbar-button click takes -- the shell restores the window
    /// itself, without telling us. <see cref="OnStateChanged"/> is what both paths share, so the
    /// per-open work belongs there and not here.
    /// </summary>
    public void ShowPanel()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        else
            PrepareForDisplay();

        Show();
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Normal)
            PrepareForDisplay();
        else if (WindowState == WindowState.Minimized)
            Topmost = false;
    }

    private void PrepareForDisplay()
    {
        // These are flyouts summoned from the taskbar, so they have to come out in front of
        // whatever is already on screen. Activate() alone is not enough: Windows refuses
        // foreground changes from a process the user isn't currently working in, which is exactly
        // the case when the request arrives from a relaunch or the global hotkey rather than from
        // a click. Dropped again on minimise so a put-away panel never sits above other windows.
        Topmost = true;
        _openedAt = DateTime.UtcNow;

        PositionOnShow();
        OnPanelShown();
    }

    /// <summary>Hook for panels that refresh their contents each time they are opened.</summary>
    protected virtual void OnPanelShown()
    {
    }

    /// <summary>Hook for panels that place themselves fresh on every open.</summary>
    protected virtual void PositionOnShow()
    {
    }

    /// <summary>
    /// Screen position of the cursor, in DIPs. Clicking a taskbar button leaves the cursor on that
    /// button, which is the only practical way to find out where the button is -- the shell
    /// exposes no API for a taskbar button's rect.
    /// </summary>
    protected Point CursorPositionDips()
    {
        var (x, y) = CursorInfo.GetPosition();
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(x / dpi.DpiScaleX, y / dpi.DpiScaleY);
    }
}
