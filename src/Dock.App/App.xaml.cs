using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    private System.Threading.Timer? _gameModeTimer;
    private bool _hiddenForGameMode;
    private SettingsStore? _settingsStore;
    private SettingsWindow? _settingsWindow;
    private DockViewModel? _viewModel;
    private IWingetService? _wingetService;
    private IIconProvider? _iconProvider;
    private IAppLauncher? _launcher;
    private readonly List<DockWindow> _dockWindows = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Safety: if a previous run crashed while the taskbar was hidden, restore it now
        // before doing anything else.
        if (TaskbarSafety.IsFlagged())
        {
            TaskbarController.Show();
            TaskbarSafety.ClearFlag();
        }

        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreTaskbarAndClearFlag();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreTaskbarAndClearFlag();

        _settingsStore = new SettingsStore();
        var settings = _settingsStore.Load();

        var configStore = new ConfigStore();
        _iconProvider = new ShellIconProvider();
        _launcher = new ProcessAppLauncher();
        _viewModel = new DockViewModel(configStore, _iconProvider, _launcher);

        _viewModel.AttachRunningApps(new WindowActivator());

        _wingetService = new WingetService();
        _viewModel.AttachWingetService(_wingetService);
        LoadInstalledAppsAsync();

        CreateDockWindows(settings.Position);

        _runningAppSource = new RunningWindowSource();
        _runningAppSource.Updated += (_, groups) => Dispatcher.Invoke(() => _viewModel.UpdateRunningApps(groups));
        _runningAppSource.Start();

        _explorerTrayReader = new ExplorerTrayReader();
        _viewModel.AttachTraySource(_explorerTrayReader);
        _explorerTrayReader.Updated += (_, icons) => Dispatcher.Invoke(() => _viewModel.UpdateTrayIcons(icons));
        _explorerTrayReader.Start();

        CreateTrayIcon();

        if (settings.HideTaskbar)
            HideTaskbarAndMarkFlag();

        StartupRegistration.SetEnabled(settings.StartWithWindows);
        LaunchGuard();

        _gameModeTimer = new System.Threading.Timer(_ => CheckGameMode(), null, 2000, 2000);
    }

    private void LoadInstalledAppsAsync()
    {
        var iconProvider = _iconProvider!;
        var launcher = _launcher!;

        System.Threading.Tasks.Task.Run(() =>
        {
            var provider = new InstalledAppsProvider();
            var apps = provider.GetInstalledApps();

            return apps
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => new Core.ViewModels.AppLauncherItemViewModel(a, launcher)
                {
                    IconPng = iconProvider.GetIconPng(a.ExecutablePath, 32)
                })
                .ToList();
        }).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                Dispatcher.Invoke(() => _viewModel!.SetLauncherItems(t.Result));
        });
    }

    private void CreateDockWindows(Core.Models.DockPosition position)
    {
        var isFirstWindow = true;
        foreach (var monitor in MonitorService.GetMonitors())
        {
            var window = new DockWindow(_viewModel!, monitor, position, enableGlobalHooks: isFirstWindow, wingetService: _wingetService);
            if (isFirstWindow)
            {
                window.PanicHotkeyPressed += RestoreTaskbarAndClearFlag;
                window.ExplorerRestarted += () =>
                {
                    if (_settingsStore!.Load().HideTaskbar)
                        HideTaskbarAndMarkFlag();
                };
            }

            isFirstWindow = false;
            window.Show();
            _dockWindows.Add(window);
        }
    }

    public void RebuildDockWindows(Core.Models.DockPosition position)
    {
        foreach (var window in _dockWindows)
            window.Close();

        _dockWindows.Clear();
        CreateDockWindows(position);
    }

    private void HideTaskbarAndMarkFlag()
    {
        TaskbarController.Hide();
        TaskbarSafety.MarkHidden();
    }

    private static void LaunchGuard()
    {
        try
        {
            var guardPath = Path.Combine(AppContext.BaseDirectory, "Dock.Guard.exe");
            if (!File.Exists(guardPath))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = guardPath,
                Arguments = Environment.ProcessId.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            // Non-fatal: the taskbar-hidden flag is still checked on the next launch, so a
            // missing/failed watchdog just means recovery waits until then instead of instantly.
        }
    }

    private void CheckGameMode()
    {
        var isFullscreen = TaskbarController.IsGameFullscreenActive();
        if (isFullscreen == _hiddenForGameMode)
            return;

        _hiddenForGameMode = isFullscreen;
        var visibility = isFullscreen ? Visibility.Hidden : Visibility.Visible;
        Dispatcher.Invoke(() =>
        {
            foreach (var window in _dockWindows)
                window.Visibility = visibility;
        });
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

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => ShowSettingsWindow();
        menu.Items.Add(settings);

        var exit = new MenuItem { Header = "Exit Dock" };
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(exit);

        menu.IsOpen = true;
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsStore!);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        RestoreTaskbarAndClearFlag();
    }

    private static void RestoreTaskbarAndClearFlag()
    {
        TaskbarController.Show();
        TaskbarSafety.ClearFlag();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _gameModeTimer?.Dispose();
        RestoreTaskbarAndClearFlag();
        _runningAppSource?.Dispose();
        _explorerTrayReader?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
