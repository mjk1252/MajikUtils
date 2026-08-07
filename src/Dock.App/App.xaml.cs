using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using Dock.App.Views;
using Dock.Core.Services;
using Dock.Core.ViewModels;
using Dock.Interop.Audio;
using Dock.Interop.Shell;
using Dock.Interop.Windowing;

namespace Dock.App;

public partial class App : System.Windows.Application
{
    private SingleInstance? _singleInstance;
    private SettingsStore? _settingsStore;
    private SettingsWindow? _settingsWindow;
    private DockViewModel? _viewModel;
    private readonly Dictionary<StackItemViewModel, StackWindow> _stackWindows = [];
    private ClipboardMonitor? _clipboardMonitor;
    private SystemStatsSource? _systemStatsSource;
    private MediaSessionSource? _mediaSource;
    private MediaViewModel? _mediaViewModel;
    private IslandActivityHost? _activities;

    /// <summary>
    /// Drives the activity host's clock, which is the only thing that retires an activity whose
    /// linger has run out. Four times a second: the windows it is expiring are seconds long, and
    /// the work per tick is a walk of a list with two things in it.
    /// </summary>
    private readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private NotesViewModel? _notesViewModel;
    private TodosViewModel? _todosViewModel;
    private AudioLoopbackSource? _audioSource;
    private IslandWindow? _islandWindow;
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

        CreateIsland(wingetService);
        CreateStackWindows();
        StartClipboardMonitor();
        StartSystemStats();

        if (_settingsStore.Load().ShowMediaIsland)
            StartMediaMonitoring();

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
    /// Builds the island. This is the application's surface now -- the drawer and the shelf used to
    /// be windows of their own for the sake of the taskbar buttons they carried, and both are
    /// sections of this panel instead. It exists for the whole run, regardless of the media
    /// setting: that only governs whether anything is playing in it.
    /// </summary>
    private void CreateIsland(IWingetService wingetService)
    {
        _mediaSource = new MediaSessionSource();
        _mediaViewModel = new MediaViewModel(_mediaSource);
        _notesViewModel = new NotesViewModel(new NotesStore());
        _todosViewModel = new TodosViewModel(new TodosStore());

        // Whose turn it is on the collapsed pill. Media is the only activity so far and so always
        // wins, but the arbitration and the grace period that used to live here are its business
        // now rather than this class's.
        _activities = new IslandActivityHost();
        _activities.Tick(DateTimeOffset.UtcNow);
        _activities.Register(_mediaViewModel);

        _activityTimer.Tick += (_, _) => _activities.Tick(DateTimeOffset.UtcNow);
        _activityTimer.Start();

        // Media notifications arrive on the thread pool, like the system stats below.
        _mediaSource.Changed += (_, snapshot) => Dispatcher.Invoke(() => _mediaViewModel.Apply(snapshot));

        // The equalizer bars read the speakers directly. Created here and owned for the whole run;
        // the island starts and stops the capture as the bars come and go.
        _audioSource = new AudioLoopbackSource();

        _islandWindow = new IslandWindow(
            _mediaViewModel, _activities, _notesViewModel, _todosViewModel, _viewModel!, wingetService,
            _audioSource, _settingsStore!.Load());

        _islandWindow.SettingsRequested += ShowSettingsWindow;
        _islandWindow.ExitRequested += Shutdown;
        _islandWindow.Show();
    }

    /// <summary>
    /// Stacks are the one thing still on the taskbar: a stack is a folder the user pinned there on
    /// purpose. Each needs a window of its own, because a taskbar button exists only while its
    /// window does -- so they are created up front and never hidden.
    /// </summary>
    private void CreateStackWindows()
    {
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

        // Every one of these used to open a window of its own. They now open the island on the
        // matching section, so shortcuts pinned back when the drawer and shelf had taskbar buttons
        // still land somewhere sensible.
        switch (panel.ToLowerInvariant())
        {
            case "exit":
                Shutdown();
                break;

            case "launch":
                _islandWindow?.ShowSection(IslandSection.Launcher);
                break;

            case "clipboard":
                _islandWindow?.ShowSection(IslandSection.Clipboard);
                break;

            case "settings":
                ShowSettingsWindow();
                break;

            case "shelf":
                _islandWindow?.ShowSection(IslandSection.Shelf);
                break;

            default:
                _islandWindow?.ShowSection(IslandSection.Quick);
                break;
        }
    }

    private void StartClipboardMonitor()
    {
        _clipboardMonitor = new ClipboardMonitor();
        _clipboardMonitor.ClipboardChanged += () => Dispatcher.Invoke(CaptureClipboardText);
        _clipboardMonitor.HotkeyPressed += () =>
            Dispatcher.Invoke(() => _islandWindow?.ShowSection(IslandSection.Clipboard));
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
            _islandWindow?.UpdateStats(stats.CpuPercent, stats.GpuPercent);
        });
        _systemStatsSource.Start();
    }

    /// <summary>
    /// Starts watching the system's media session. Separate from the island's own lifetime: the
    /// island is the app's UI and always there, while this is the one part of it the user can turn
    /// off, which leaves the panel with everything except a now-playing row.
    /// </summary>
    private void StartMediaMonitoring() => _mediaSource?.Start();

    /// <summary>
    /// Stops watching for media and empties the now-playing row. The island stays: everything else
    /// in it is unrelated to whether anything is playing.
    ///
    /// Retired outright rather than left to linger -- the grace period is for a session that went
    /// on its own, and this one was switched off deliberately.
    /// </summary>
    private void StopMediaMonitoring()
    {
        _mediaSource?.Stop();
        _mediaViewModel?.Apply(null);
        _mediaViewModel?.Retire();
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
                StartMediaMonitoring();
            else
                StopMediaMonitoring();
        };
        _settingsWindow.MediaIslandAppearanceChanged += settings =>
            _islandWindow?.ApplyAppearance(settings);
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

        _activityTimer.Stop();
        _islandWindow?.CloseForExit();
        _mediaSource?.Dispose();
        _audioSource?.Dispose();

        foreach (var window in _stackWindows.Values)
            window.CloseForExit();
        _stackWindows.Clear();

        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
