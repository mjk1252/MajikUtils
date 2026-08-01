using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
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
    private bool _awaitingActivation;

    protected PanelWindow()
    {
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Minimized;
        Background = System.Windows.Media.Brushes.Transparent;
    }

    /// <summary>Distinct per window: this is what stops the shell grouping both buttons into one.</summary>
    protected abstract string AppId { get; }

    /// <summary>The --panel value a pinned shortcut relaunches Dock with.</summary>
    protected abstract string PanelArgument { get; }

    /// <summary>Label the shell shows for the pinned button, and the basis for the hover tooltip.</summary>
    protected abstract string DisplayName { get; }

    /// <summary>"path,index" for the pinned button's artwork, or null to fall back to the exe's own.</summary>
    protected virtual string? RelaunchIconResource => null;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var exePath = Environment.ProcessPath ?? "Dock.exe";

        AppIdRegistrar.Stamp(
            hwnd,
            AppId,
            relaunchCommand: $"\"{exePath}\" --panel {PanelArgument}",
            displayName: DisplayName,
            iconResource: RelaunchIconResource ?? $"{exePath},0");
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
            if (_exiting || _awaitingActivation || WindowState != WindowState.Normal ||
                ForegroundWindow.IsOwnedByThisProcess())
            {
                return;
            }

            SavePlacement();
            WindowState = WindowState.Minimized;
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// A panel opened by <see cref="ShowPanel"/> stays up even if it never reached the foreground.
    /// Windows refuses foreground changes requested by a process the user isn't currently working
    /// in, so <c>Activate</c> can silently fail and hand us a Deactivated for a window that was
    /// never active -- without this the panel would minimise itself the instant it appeared.
    /// </summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _awaitingActivation = false;
    }

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
    /// Brings the panel up in response to its taskbar button (or a relaunch from a pinned copy).
    /// Restore-then-Activate is deliberate: activating a minimised window does not restore it.
    /// </summary>
    public void ShowPanel()
    {
        _awaitingActivation = true;

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
        Activate();
        OnPanelShown();
    }

    /// <summary>Hook for panels that refresh their contents each time they are opened.</summary>
    protected virtual void OnPanelShown()
    {
    }
}
