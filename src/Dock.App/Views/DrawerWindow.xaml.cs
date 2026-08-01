using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Dock.Core.ViewModels;

namespace Dock.App.Views;

public partial class DrawerWindow : PanelWindow
{
    /// <summary>Segoe MDL2 "Dock Bottom" -- a drawer of things kept to hand.</summary>
    private const string DrawerGlyph = "";

    private static readonly BitmapSource ButtonIcon = PanelIcons.RenderGlyph(DrawerGlyph);
    private static readonly string? PinnedIcon = PanelIcons.EnsureIcoOnDisk("drawer", ButtonIcon);

    private ToggleButton _activeTab;

    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public DrawerWindow(DockViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        Icon = ButtonIcon;

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
    /// Shows the latest stats sample in the drawer's own header. Deliberately not pushed onto the
    /// taskbar button: a per-second icon repaint made the button flicker in the tray of otherwise
    /// static app icons, and a window title that changes every second is noise in Alt+Tab too.
    /// </summary>
    public void UpdateStats(double cpuPercent, double gpuPercent) =>
        StatsText.Text = $"CPU {cpuPercent:0}%   GPU {gpuPercent:0}%";

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
        ClipboardView.Visibility = VisibilityFor(tab, ClipboardTab);

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

    private ToggleButton[] Tabs => [RecentTab, StacksTab, ClipboardTab];

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
