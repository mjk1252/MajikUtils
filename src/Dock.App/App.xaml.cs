using System.Windows;
using System.Windows.Controls;
using Dock.App.Views;
using Dock.Core.Services;
using Dock.Core.ViewModels;
using Dock.Interop.Shell;
using Dock.Interop.Windowing;

namespace Dock.App;

public partial class App : System.Windows.Application
{
    private TrayIconService? _trayIcon;
    private RunningWindowSource? _runningAppSource;
    private ExplorerTrayReader? _explorerTrayReader;
    private readonly List<DockWindow> _dockWindows = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configStore = new ConfigStore();
        IIconProvider iconProvider = new ShellIconProvider();
        IAppLauncher launcher = new ProcessAppLauncher();
        var viewModel = new DockViewModel(configStore, iconProvider, launcher);

        viewModel.AttachRunningApps(new WindowActivator());

        foreach (var monitor in MonitorService.GetMonitors())
        {
            var window = new DockWindow(viewModel, monitor.WorkArea);
            window.Show();
            _dockWindows.Add(window);
        }

        _runningAppSource = new RunningWindowSource();
        _runningAppSource.Updated += (_, groups) => Dispatcher.Invoke(() => viewModel.UpdateRunningApps(groups));
        _runningAppSource.Start();

        _explorerTrayReader = new ExplorerTrayReader();
        viewModel.AttachTraySource(_explorerTrayReader);
        _explorerTrayReader.Updated += (_, icons) => Dispatcher.Invoke(() => viewModel.UpdateTrayIcons(icons));
        _explorerTrayReader.Start();

        CreateTrayIcon();
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayIconService();
        _trayIcon.RightClicked += ShowTrayMenu;

        var hIcon = IconHandles.GetHIcon(Environment.ProcessPath ?? "Dock.exe", small: true);
        _trayIcon.Show(hIcon, "Dock");
    }

    private void ShowTrayMenu()
    {
        var (x, y) = CursorInfo.GetPosition();

        var menu = new ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint,
            HorizontalOffset = x,
            VerticalOffset = y,
            StaysOpen = false
        };

        var exit = new MenuItem { Header = "Exit Dock" };
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(exit);

        menu.IsOpen = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runningAppSource?.Dispose();
        _explorerTrayReader?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
