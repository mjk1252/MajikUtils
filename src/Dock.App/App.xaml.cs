using System.Collections.Specialized;
using System.Windows;
using Dock.App.Views;
using Dock.Core.Services;
using Dock.Core.ViewModels;
using Dock.Interop.Shell;
using Dock.Interop.Windowing;

namespace Dock.App;

public partial class App : System.Windows.Application
{
    private SingleInstance? _singleInstance;
    private SettingsStore? _settingsStore;
    private SettingsWindow? _settingsWindow;
    private DockViewModel? _viewModel;
    private LaunchWindow? _launchWindow;
    private DrawerWindow? _drawerWindow;
    private ClipboardMonitor? _clipboardMonitor;
    private SystemStatsSource? _systemStatsSource;
    private IIconProvider? _iconProvider;
    private IAppLauncher? _launcher;
    private readonly Dictionary<StackItemViewModel, StackFolderWatcher> _stackWatchers = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var requestedPanel = ParsePanelArgument(e.Args);

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance && SingleInstance.SendToRunningInstance(requestedPanel ?? "drawer"))
        {
            Shutdown();
            return;
        }

        _settingsStore = new SettingsStore();

        _iconProvider = new ShellIconProvider();
        _launcher = new ProcessAppLauncher();
        _viewModel = new DockViewModel(_iconProvider, _launcher);
        _viewModel.AttachClipboardWriter(new ClipboardWriter());

        foreach (var stack in _viewModel.Stacks)
            WatchStack(stack);

        _viewModel.Stacks.CollectionChanged += OnStacksCollectionChanged;

        var wingetService = new WingetService();
        _viewModel.AttachWingetService(wingetService);
        LoadInstalledAppsAsync();

        CreatePanelWindows(wingetService);
        StartClipboardMonitor();
        StartSystemStats();

        _singleInstance.StartListening(panel => Dispatcher.Invoke(() => ShowPanel(panel)));

        StartupRegistration.SetEnabled(_settingsStore.Load().StartWithWindows);

        // A launch carrying an explicit panel came from a pinned taskbar button, so open that
        // panel. A plain launch (startup, first install) leaves both buttons sitting minimised.
        if (requestedPanel is not null)
            ShowPanel(requestedPanel);
    }

    private static string? ParsePanelArgument(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--panel", StringComparison.OrdinalIgnoreCase))
                return args[i + 1].Trim().ToLowerInvariant();
        }

        return null;
    }

    /// <summary>
    /// Both windows are created up front and never destroyed: a taskbar button only exists while
    /// its window does, so creating one lazily would mean the button the user is trying to click
    /// isn't there yet.
    /// </summary>
    private void CreatePanelWindows(IWingetService wingetService)
    {
        _launchWindow = new LaunchWindow(_viewModel!, wingetService);
        _launchWindow.AttachPlacementStore(_settingsStore!);
        _launchWindow.Show();

        _drawerWindow = new DrawerWindow(_viewModel!);
        _drawerWindow.AttachPlacementStore(_settingsStore!);
        _drawerWindow.SettingsRequested += ShowSettingsWindow;
        _drawerWindow.ExitRequested += Shutdown;
        _drawerWindow.Show();
    }

    private void ShowPanel(string panel)
    {
        if (string.Equals(panel, "launch", StringComparison.OrdinalIgnoreCase))
            _launchWindow?.ShowPanel();
        else
            _drawerWindow?.ShowPanel();
    }

    private void StartClipboardMonitor()
    {
        _clipboardMonitor = new ClipboardMonitor();
        _clipboardMonitor.ClipboardChanged += () => Dispatcher.Invoke(CaptureClipboardText);
        _clipboardMonitor.HotkeyPressed += () => Dispatcher.Invoke(() => _drawerWindow?.ShowClipboard());
        _clipboardMonitor.Start();
    }

    private void CaptureClipboardText()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                    _viewModel!.AddClipboardEntry(text);
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard is transiently locked by whichever app just wrote to it -- that write is
            // exactly what triggered this notification, so nothing to capture is actually lost.
        }
    }

    private void StartSystemStats()
    {
        _systemStatsSource = new SystemStatsSource();
        _systemStatsSource.Updated += (_, stats) => Dispatcher.Invoke(() =>
        {
            _viewModel!.UpdateSystemStats(stats.CpuPercent, stats.GpuPercent);
            _drawerWindow?.UpdateStats(stats.CpuPercent, stats.GpuPercent);
        });
        _systemStatsSource.Start();
    }

    private void OnStacksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (StackItemViewModel stack in e.OldItems)
                UnwatchStack(stack);
        }

        if (e.NewItems is not null)
        {
            foreach (StackItemViewModel stack in e.NewItems)
                WatchStack(stack);
        }
    }

    private void WatchStack(StackItemViewModel stack)
    {
        var watcher = new StackFolderWatcher(stack.Path);
        watcher.Changed += () => Dispatcher.Invoke(() => stack.Refresh(_iconProvider!, _launcher!));
        _stackWatchers[stack] = watcher;
    }

    private void UnwatchStack(StackItemViewModel stack)
    {
        if (_stackWatchers.Remove(stack, out var watcher))
            watcher.Dispose();
    }

    private void LoadInstalledAppsAsync()
    {
        var iconProvider = _iconProvider!;
        var launcher = _launcher!;

        Task.Run(() =>
        {
            var provider = new InstalledAppsProvider();
            var apps = provider.GetInstalledApps();

            return apps
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => new AppLauncherItemViewModel(a, launcher)
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

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var watcher in _stackWatchers.Values)
            watcher.Dispose();
        _stackWatchers.Clear();

        _clipboardMonitor?.Dispose();
        _systemStatsSource?.Dispose();

        _launchWindow?.CloseForExit();
        _drawerWindow?.CloseForExit();

        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
