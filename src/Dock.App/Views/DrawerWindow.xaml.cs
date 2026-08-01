using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Dock.Core.Services;
using Dock.Core.ViewModels;
using Dock.Interop.Shell;

namespace Dock.App.Views;

public partial class DrawerWindow : PanelWindow
{
    /// <summary>Segoe MDL2 "Dock Bottom" -- a drawer of things kept to hand.</summary>
    private const string DrawerGlyph = "";

    // A custom icon dropped in by the user wins; the drawn glyph is only the fallback.
    private static readonly BitmapSource ButtonIcon =
        PanelIcons.LoadCustom("drawer") ?? PanelIcons.RenderGlyph(DrawerGlyph, PanelIcons.DrawerAccent);
    private static readonly string? PinnedIcon = PanelIcons.EnsureIcoOnDisk("drawer", ButtonIcon);

    private ToggleButton _activeTab;

    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public DrawerWindow(DockViewModel viewModel, IWingetService wingetService)
    {
        InitializeComponent();

        DataContext = viewModel;
        Icon = ButtonIcon;

        LauncherView.WingetService = wingetService;
        LauncherView.InstallStarted += OnInstallStarted;

        // The taskbar button's progress bar. Indeterminate is the honest state: winget reports no
        // percentage back, only "finished".
        TaskbarItemInfo = new TaskbarItemInfo();

        // Selected here rather than via IsChecked="True" in the XAML: that fires Checked partway
        // through InitializeComponent, when the panels the handler touches are still null.
        _activeTab = LauncherTab;
        LauncherTab.IsChecked = true;

        UpdateStats(0, 0);
    }

    protected override string AppId => "MajikUtils.Drawer";
    protected override string PanelArgument => "drawer";
    protected override string DisplayName => "MajikUtils Drawer";
    protected override string? RelaunchIconResource => PinnedIcon;

    /// <summary>The one resizable panel, so the only one with a size worth remembering.</summary>
    protected override bool PersistsSize => true;

    protected override IReadOnlyList<JumpListTask> ExtraJumpListTasks =>
    [
        new("Search apps", ExePath, "--panel launch"),
        new("Clipboard history", ExePath, "--panel clipboard"),
        new("Settings...", ExePath, "--panel settings")
    ];

    /// <summary>
    /// Shows the latest stats sample in the drawer's own header. Deliberately not pushed onto the
    /// taskbar button: a per-second icon repaint made the button flicker in the tray of otherwise
    /// static app icons, and a window title that changes every second is noise in Alt+Tab too.
    /// </summary>
    public void UpdateStats(double cpuPercent, double gpuPercent) =>
        StatsText.Text = $"CPU {cpuPercent:0}%   GPU {gpuPercent:0}%";

    /// <summary>Brings the drawer up on its Clipboard tab, for the global clipboard hotkey.</summary>
    public void ShowClipboard() => ShowOnTab(ClipboardTab);

    /// <summary>Brings the drawer up on its Launch tab, for a relaunch from a pinned launcher shortcut.</summary>
    public void ShowLauncher() => ShowOnTab(LauncherTab);

    private void ShowOnTab(ToggleButton tab)
    {
        tab.IsChecked = true;
        ShowPanel();
    }

    /// <summary>
    /// Opening the drawer on the Launch tab puts the caret straight in the search box, so the
    /// button acts as "press it and start typing".
    /// </summary>
    protected override void OnPanelShown()
    {
        if (ReferenceEquals(_activeTab, LauncherTab))
            LauncherView.FocusSearch();
        else if (ReferenceEquals(_activeTab, RecentTab))
            RecentView.Refresh();
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        var tab = (ToggleButton)sender;
        _activeTab = tab;

        foreach (var other in Tabs)
        {
            if (!ReferenceEquals(other, tab))
                other.IsChecked = false;
        }

        LauncherView.Visibility = VisibilityFor(tab, LauncherTab);
        RecentView.Visibility = VisibilityFor(tab, RecentTab);
        StacksView.Visibility = VisibilityFor(tab, StacksTab);
        ClipboardView.Visibility = VisibilityFor(tab, ClipboardTab);

        if (ReferenceEquals(tab, LauncherTab))
            LauncherView.FocusSearch();
        else if (ReferenceEquals(tab, RecentTab))
            RecentView.Refresh();
    }

    /// <summary>
    /// Clicking the tab that is already selected would otherwise leave the rail with nothing lit
    /// and the content area showing the panel of a tab that no longer looks active.
    /// </summary>
    private void OnTabUnchecked(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _activeTab))
            ((ToggleButton)sender).IsChecked = true;
    }

    private ToggleButton[] Tabs => [LauncherTab, RecentTab, StacksTab, ClipboardTab];

    private static Visibility VisibilityFor(ToggleButton active, ToggleButton tab) =>
        ReferenceEquals(active, tab) ? Visibility.Visible : Visibility.Collapsed;

    private void OnInstallStarted()
    {
        TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;

        // WingetResultViewModel owns the install and doesn't surface completion, so the bar is
        // cleared on a fixed delay rather than left spinning forever on a button the user has
        // most likely already put away.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
        };
        timer.Start();
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnMinimiseClick(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

    private void OnSettingsClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Items.Add(settings);

        var exit = new MenuItem { Header = "Exit MajikUtils" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);

        ((FrameworkElement)sender).ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
