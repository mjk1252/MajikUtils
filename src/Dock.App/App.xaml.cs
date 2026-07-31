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
    private DockWindow? _dockWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configStore = new ConfigStore();
        IIconProvider iconProvider = new ShellIconProvider();
        IAppLauncher launcher = new ProcessAppLauncher();
        var viewModel = new DockViewModel(configStore, iconProvider, launcher);

        _dockWindow = new DockWindow(viewModel);
        _dockWindow.Show();

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
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
