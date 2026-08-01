using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using Dock.App.Views;
using Dock.Core.Models;
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
    private DrawerWindow? _drawerWindow;
    private ShelfWindow? _shelfWindow;
    private readonly Dictionary<StackItemViewModel, StackWindow> _stackWindows = [];
    private ClipboardMonitor? _clipboardMonitor;
    private SystemStatsSource? _systemStatsSource;
    private MediaSessionSource? _mediaSource;
    private MediaViewModel? _mediaViewModel;
    private NotesViewModel? _notesViewModel;
    private IslandWindow? _islandWindow;
    private DispatcherTimer? _mediaClearTimer;
    private IIconProvider? _iconProvider;
    private IAppLauncher? _launcher;
    private readonly Dictionary<StackItemViewModel, StackFolderWatcher> _stackWatchers = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // "--exit" arrives from a jump-list entry, which always starts a *new* process: the work is
        // to tell the running instance to quit, then quit ourselves without ever building a UI.
        var exitRequested = e.Args.Any(a => string.Equals(a, "--exit", StringComparison.OrdinalIgnoreCase));
        var requestedPanel = exitRequested ? "exit" : ParsePanelArgument(e.Args);

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance && SingleInstance.SendToRunningInstance(requestedPanel ?? "drawer"))
        {
            Shutdown();
            return;
        }

        // Nothing was running to forward to, so there is nothing to close either.
        if (exitRequested)
        {
            Shutdown();
            return;
        }

        _settingsStore = new SettingsStore();

        _iconProvider = new ShellIconProvider();
        _launcher = new ProcessAppLauncher();
        _viewModel = new DockViewModel(_iconProvider, _launcher);
        _viewModel.AttachClipboardWriter(new ClipboardWriter());

        var wingetService = new WingetService();
        _viewModel.AttachWingetService(wingetService);
        LoadInstalledAppsAsync();

        CreatePanelWindows(wingetService);
        StartClipboardMonitor();
        StartSystemStats();

        if (_settingsStore.Load().ShowMediaIsland)
            StartMediaIsland();

        _singleInstance.StartListening(panel => Dispatcher.Invoke(() => ShowPanel(panel)));

        StartupRegistration.SetEnabled(_settingsStore.Load().StartWithWindows);

        // A launch carrying an explicit panel came from a pinned taskbar button, so open that
        // panel. A plain launch (startup, first install) leaves every button sitting minimised.
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
    /// Every panel window is created up front and never destroyed: a taskbar button only exists
    /// while its window does, so creating one lazily would mean the button the user is trying to
    /// click isn't there yet.
    /// </summary>
    private void CreatePanelWindows(IWingetService wingetService)
    {
        _drawerWindow = new DrawerWindow(_viewModel!, wingetService);
        _drawerWindow.AttachSizeStore(_settingsStore!);
        _drawerWindow.SettingsRequested += ShowSettingsWindow;
        _drawerWindow.ExitRequested += Shutdown;
        _drawerWindow.Show();

        _shelfWindow = new ShelfWindow(_viewModel!);
        _shelfWindow.Show();

        foreach (var stack in _viewModel!.Stacks)
            AddStackWindow(stack);

        _viewModel.Stacks.CollectionChanged += OnStacksCollectionChanged;
    }

    /// <summary>
    /// Every stack gets its own taskbar button, so adding or removing one has to create or
    /// destroy a window -- a button exists only for as long as its window does.
    /// </summary>
    private void AddStackWindow(StackItemViewModel stack)
    {
        var window = new StackWindow(stack);
        window.Show();
        _stackWindows[stack] = window;

        WatchStack(stack);
    }

    private void RemoveStackWindow(StackItemViewModel stack)
    {
        if (_stackWindows.Remove(stack, out var window))
            window.CloseForExit();

        UnwatchStack(stack);
    }

    private void ShowPanel(string panel)
    {
        if (panel.StartsWith("stack:", StringComparison.OrdinalIgnoreCase))
        {
            var id = panel["stack:".Length..];
            var match = _stackWindows.FirstOrDefault(
                p => string.Equals(p.Key.Folder.Id, id, StringComparison.OrdinalIgnoreCase));

            match.Value?.ShowPanel();
            return;
        }

        switch (panel.ToLowerInvariant())
        {
            case "exit":
                Shutdown();
                break;

            // Still honoured: the launcher used to have a taskbar button of its own, so a shortcut
            // pinned back then relaunches with it.
            case "launch":
                _drawerWindow?.ShowLauncher();
                break;

            case "clipboard":
                _drawerWindow?.ShowClipboard();
                break;

            case "settings":
                ShowSettingsWindow();
                break;

            case "shelf":
                _shelfWindow?.ShowPanel();
                break;

            default:
                _drawerWindow?.ShowPanel();
                break;
        }
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

    /// <summary>
    /// Brings up the media island. Unlike the panels this is torn down and rebuilt when the user
    /// toggles it, since it owns no taskbar button that has to outlive it.
    /// </summary>
    private void StartMediaIsland()
    {
        if (_islandWindow is not null)
            return;

        _mediaSource = new MediaSessionSource();
        _mediaViewModel = new MediaViewModel(_mediaSource);
        _notesViewModel = new NotesViewModel(new NotesStore());

        // Media notifications arrive on the thread pool, like the system stats above.
        _mediaSource.Changed += (_, snapshot) => Dispatcher.Invoke(() => ApplyMediaSnapshot(snapshot));
        _mediaSource.Start();

        _islandWindow = new IslandWindow(_mediaViewModel, _notesViewModel);
        _islandWindow.Show();
    }

    /// <summary>
    /// Applies a snapshot, holding back the one that empties the island.
    ///
    /// Losing the session is routine and usually momentary -- closing one tab of several, a player
    /// restarting its session between albums -- and the island is a strip of screen the user may
    /// be pointing at when it happens. Anything arriving inside the grace period cancels the
    /// clear, so only a session that stays gone actually puts it away.
    /// </summary>
    private void ApplyMediaSnapshot(MediaSnapshot? snapshot)
    {
        _mediaClearTimer?.Stop();

        if (snapshot is not null)
        {
            _mediaViewModel?.Apply(snapshot);
            return;
        }

        // Nothing to hold back if it is already empty.
        if (_mediaViewModel is not { HasSession: true })
        {
            _mediaViewModel?.Apply(null);
            return;
        }

        _mediaClearTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _mediaClearTimer.Tick -= OnMediaClearElapsed;
        _mediaClearTimer.Tick += OnMediaClearElapsed;
        _mediaClearTimer.Start();
    }

    private void OnMediaClearElapsed(object? sender, EventArgs e)
    {
        _mediaClearTimer?.Stop();
        _mediaViewModel?.Apply(null);
    }

    private void StopMediaIsland()
    {
        _mediaClearTimer?.Stop();
        _mediaClearTimer = null;

        _islandWindow?.CloseForExit();
        _islandWindow = null;

        _mediaSource?.Dispose();
        _mediaSource = null;
        _mediaViewModel = null;
        _notesViewModel = null;
    }

    private void OnStacksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (StackItemViewModel stack in e.OldItems)
                RemoveStackWindow(stack);
        }

        if (e.NewItems is not null)
        {
            foreach (StackItemViewModel stack in e.NewItems)
                AddStackWindow(stack);
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
        _settingsWindow.MediaIslandToggled += show =>
        {
            if (show)
                StartMediaIsland();
            else
                StopMediaIsland();
        };
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
        StopMediaIsland();

        foreach (var window in _stackWindows.Values)
            window.CloseForExit();
        _stackWindows.Clear();

        _drawerWindow?.CloseForExit();
        _shelfWindow?.CloseForExit();

        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
