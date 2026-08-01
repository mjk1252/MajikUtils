using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Dock.Core.ViewModels;

namespace Dock.App.Views;

public partial class DrawerWindow : PanelWindow
{
    /// <summary>Segoe MDL2 "Processor" -- stands in until the first stats tick paints the gauge.</summary>
    private const string ProcessorGlyph = "";

    private static readonly string? PinnedIcon =
        PanelIcons.EnsureIcoOnDisk("drawer", PanelIcons.RenderGlyph(ProcessorGlyph));

    private ToggleButton _activeTab;

    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public DrawerWindow(DockViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        // Selected here rather than via IsChecked="True" in the XAML: that fires Checked partway
        // through InitializeComponent, when the panels the handler touches are still null.
        _activeTab = RecentTab;
        RecentTab.IsChecked = true;

        UpdateStats(0, 0);
    }

    protected override string AppId => "Dock.Drawer";
    protected override string PanelArgument => "drawer";
    protected override string DisplayName => "Dock Drawer";
    protected override string? RelaunchIconResource => PinnedIcon;

    /// <summary>
    /// Repaints the taskbar button from the latest stats sample. The Title carries the numbers
    /// because the taskbar's hover tooltip *is* the window title -- that is the whole readout,
    /// no extra plumbing involved.
    /// </summary>
    public void UpdateStats(double cpuPercent, double gpuPercent)
    {
        Icon = PanelIcons.RenderStatsGauge(cpuPercent, gpuPercent);
        Title = $"Dock — CPU {cpuPercent:0}% · GPU {gpuPercent:0}%";
        StatsText.Text = $"CPU {cpuPercent:0}%   GPU {gpuPercent:0}%";
    }

    /// <summary>Brings the drawer up on its Clipboard tab, for the global clipboard hotkey.</summary>
    public void ShowClipboard()
    {
        ClipboardTab.IsChecked = true;
        ShowPanel();
    }

    protected override void OnPanelShown()
    {
        if (ReferenceEquals(_activeTab, RecentTab))
            RecentView.Refresh();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // A fan left open over a window that just went away would hang in mid-air: the Popup is
        // its own HWND and does not minimise with its owner.
        if (WindowState == WindowState.Minimized)
            StacksView.CloseFan();
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

        RecentView.Visibility = VisibilityFor(tab, RecentTab);
        StacksView.Visibility = VisibilityFor(tab, StacksTab);
        ShelfView.Visibility = VisibilityFor(tab, ShelfTab);
        ClipboardView.Visibility = VisibilityFor(tab, ClipboardTab);

        if (!ReferenceEquals(tab, StacksTab))
            StacksView.CloseFan();

        if (ReferenceEquals(tab, RecentTab))
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

    private ToggleButton[] Tabs => [RecentTab, StacksTab, ShelfTab, ClipboardTab];

    private static Visibility VisibilityFor(ToggleButton active, ToggleButton tab) =>
        ReferenceEquals(active, tab) ? Visibility.Visible : Visibility.Collapsed;

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnMinimiseClick(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

    private void OnSettingsClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Items.Add(settings);

        var exit = new MenuItem { Header = "Exit Dock" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);

        ((FrameworkElement)sender).ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
