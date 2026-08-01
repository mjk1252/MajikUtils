using System.Collections.Specialized;
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
    private SystemStatsSource? _systemStatsSource;
    private System.Threading.Timer? _gameModeTimer;
    private SettingsStore? _settingsStore;
    private SettingsWindow? _settingsWindow;
    private DockViewModel? _viewModel;
    private IWingetService? _wingetService;
    private IIconProvider? _iconProvider;
    private IAppLauncher? _launcher;
    private readonly List<DockWindow> _dockWindows = [];
    private readonly Dictionary<Core.ViewModels.StackItemViewModel, StackFolderWatcher> _stackWatchers = [];

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
        _viewModel.AttachClipboardWriter(new ClipboardWriter());

        foreach (var stack in _viewModel.Stacks)
            WatchStack(stack);

        _viewModel.Stacks.CollectionChanged += OnStacksCollectionChanged;

        _wingetService = new WingetService();
        _viewModel.AttachWingetService(_wingetService);
        LoadInstalledAppsAsync();

        CreateDockWindows(settings.Position, settings.AccentColor, settings.TintOpacity);

        _runningAppSource = new RunningWindowSource();
        _runningAppSource.Updated += (_, groups) => Dispatcher.Invoke(() => _viewModel.UpdateRunningApps(groups));
        _runningAppSource.Start();

        _explorerTrayReader = new ExplorerTrayReader();
        _viewModel.AttachTraySource(_explorerTrayReader);
        _explorerTrayReader.Updated += (_, icons) => Dispatcher.Invoke(() => _viewModel.UpdateTrayIcons(icons));
        _explorerTrayReader.Start();

        _systemStatsSource = new SystemStatsSource();
        _systemStatsSource.Updated += (_, stats) =>
            Dispatcher.Invoke(() => _viewModel.UpdateSystemStats(stats.CpuPercent, stats.GpuPercent));
        _systemStatsSource.Start();

        CreateTrayIcon();

        if (settings.HideTaskbar)
            HideTaskbarAndMarkFlag();

        StartupRegistration.SetEnabled(settings.StartWithWindows);
        LaunchGuard();

        _gameModeTimer = new System.Threading.Timer(_ => CheckGameMode(), null, 2000, 2000);
    }

    private void OnStacksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (Core.ViewModels.StackItemViewModel stack in e.OldItems)
                UnwatchStack(stack);
        }

        if (e.NewItems is not null)
        {
            foreach (Core.ViewModels.StackItemViewModel stack in e.NewItems)
                WatchStack(stack);
        }
    }

    private void WatchStack(Core.ViewModels.StackItemViewModel stack)
    {
        var watcher = new StackFolderWatcher(stack.Path);
        watcher.Changed += () => Dispatcher.Invoke(() => stack.Refresh(_iconProvider!, _launcher!));
        _stackWatchers[stack] = watcher;
    }

    private void UnwatchStack(Core.ViewModels.StackItemViewModel stack)
    {
        if (_stackWatchers.Remove(stack, out var watcher))
            watcher.Dispose();
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

    private void CreateDockWindows(Core.Models.DockPosition position, string accentColor, int tintOpacityPercent)
    {
        var isFirstWindow = true;
        foreach (var monitor in MonitorService.GetMonitors())
        {
            var window = new DockWindow(_viewModel!, monitor, position, enableGlobalHooks: isFirstWindow,
                wingetService: _wingetService, accentColor: accentColor, tintOpacityPercent: tintOpacityPercent);

            var savedSettings = _settingsStore!.Load();
            window.IconSize = savedSettings.IconSizeByMonitor.TryGetValue(monitor.DeviceName, out var savedSize)
                ? savedSize
                : savedSettings.IconSize;

            window.DockPadding = savedSettings.DockPadding;
            window.IconSpacing = savedSettings.IconSpacing;
            window.DockMargin = savedSettings.DockMargin;
            window.AppClearance = savedSettings.AppClearance;

            window.IconSizeChanged += size =>
            {
                var current = _settingsStore.Load();
                current.IconSizeByMonitor[monitor.DeviceName] = size;
                _settingsStore.Save(current);
            };

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

    /// <summary>
    /// Pushes spacing onto the live dock windows instead of rebuilding them, so dragging the
    /// sliders in Settings updates the dock continuously. Each window's SizeChanged handler
    /// re-runs ApplyPillRegionAndPosition, so the rounded region and docked position follow.
    /// </summary>
    public void ApplyDockSpacing(double dockPadding, double iconSpacing, double dockMargin, double appClearance)
    {
        foreach (var window in _dockWindows)
        {
            window.DockPadding = dockPadding;
            window.IconSpacing = iconSpacing;
            window.DockMargin = dockMargin;
            window.AppClearance = appClearance;
        }
    }

    public void RebuildDockWindows(Core.Models.DockPosition position, string accentColor, int tintOpacityPercent)
    {
        foreach (var window in _dockWindows)
            window.Close();

        _dockWindows.Clear();
        CreateDockWindows(position, accentColor, tintOpacityPercent);
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
        // Only the monitor actually showing the fullscreen window should have its dock hidden --
        // otherwise fullscreening something on one monitor blanks every dock on every monitor.
        var fullscreenMonitor = TaskbarController.GetFullscreenMonitor();

        Dispatcher.Invoke(() =>
        {
            foreach (var window in _dockWindows)
            {
                window.Visibility = window.MonitorHandle == fullscreenMonitor
                    ? Visibility.Hidden
                    : Visibility.Visible;
            }
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
        foreach (var watcher in _stackWatchers.Values)
            watcher.Dispose();
        _stackWatchers.Clear();
        _runningAppSource?.Dispose();
        _explorerTrayReader?.Dispose();
        _systemStatsSource?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
