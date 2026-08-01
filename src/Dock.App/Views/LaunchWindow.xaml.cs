using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.App.Views;

public partial class LaunchWindow : PanelWindow
{
    /// <summary>Segoe MDL2 "Search" -- the same glyph the dock's launcher button used.</summary>
    private const string SearchGlyph = "";

    private static readonly BitmapSource ButtonIcon = PanelIcons.RenderGlyph(SearchGlyph);
    private static readonly string? PinnedIcon = PanelIcons.EnsureIcoOnDisk("launch", ButtonIcon);

    public LaunchWindow(DockViewModel viewModel, IWingetService wingetService)
    {
        InitializeComponent();

        DataContext = viewModel;
        Launcher.WingetService = wingetService;
        Launcher.InstallStarted += OnInstallStarted;

        Icon = ButtonIcon;

        // The taskbar button's progress bar. Indeterminate is the honest state: winget reports no
        // percentage back, only "finished".
        TaskbarItemInfo = new TaskbarItemInfo();
    }

    protected override string AppId => "Dock.Launch";
    protected override string PanelArgument => "launch";
    protected override string DisplayName => "Dock Launcher";
    protected override string? RelaunchIconResource => PinnedIcon;

    protected override void OnPanelShown() => Launcher.FocusSearch();

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
}
