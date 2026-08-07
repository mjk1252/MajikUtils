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
    private PrivacyViewModel? _privacyViewModel;
    private DeviceUsageMonitor? _deviceUsageMonitor;
    private DebugActivity? _debugActivity;
    private DispatcherTimer? _debugTimer;

    private AnnouncementActivity? _announcements;
    private TimerActivity? _timer;
    private ConditionActivity? _doNotDisturb;
    private ConditionActivity? _restartPending;
    private SystemEventSource? _systemEvents;
    private BluetoothSource? _bluetooth;
    private SystemConditionSource? _conditions;
    private VolumeSource? _volume;
    private BatterySource? _battery;
    private AudioDeviceSource? _audioDevices;
    private ConditionActivity? _lowBattery;
    private ConditionActivity? _lowDisk;

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

        if (_settingsStore.Load().ShowPrivacyIndicator)
            StartDeviceUsageMonitoring();

        StartSystemEvents();
        StartSystemConditions();
        StartVolumeWatch();
        StartBatteryWatch();

        if (e.Args.Any(a => string.Equals(a, "--debug-activity", StringComparison.OrdinalIgnoreCase)))
            StartDebugActivity();

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
        _privacyViewModel = new PrivacyViewModel(_iconProvider!);
        _announcements = new AnnouncementActivity();
        _timer = new TimerActivity();

        // Two instances of one class: what separates these is a label and a glyph.
        _doNotDisturb = new ConditionActivity
        {
            Key = "dnd", Label = "Do not disturb", Glyph = "\uE7ED"
        };

        _restartPending = new ConditionActivity
        {
            Key = "restart", Label = "Restart pending", Glyph = "\uE777"
        };

        _lowDisk = new ConditionActivity { Key = "disk", Glyph = "\uEDA2" };

        // Its label carries the percentage, so unlike the others it is written as readings arrive.
        _lowBattery = new ConditionActivity { Key = "battery", Glyph = "\uE850" };

        _activities = new IslandActivityHost();
        _activities.Tick(DateTimeOffset.UtcNow);
        _activities.Register(_mediaViewModel);
        _activities.Register(_privacyViewModel);
        _activities.Register(_announcements);
        _activities.Register(_timer);
        _activities.Register(_doNotDisturb);
        _activities.Register(_restartPending);
        _activities.Register(_lowDisk);

        // One clock for everything that measures time on the island: the host's linger windows, an
        // announcement's two and a half seconds, and the timer's countdown.
        _activityTimer.Tick += (_, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            _announcements.Tick(now);
            _timer.Tick(now);
            _activities.Tick(now);
        };

        _activityTimer.Start();

        // Media notifications arrive on the thread pool, like the system stats below.
        _mediaSource.Changed += (_, snapshot) => Dispatcher.Invoke(() => _mediaViewModel.Apply(snapshot));

        // The equalizer bars read the speakers directly. Created here and owned for the whole run;
        // the island starts and stops the capture as the bars come and go.
        _audioSource = new AudioLoopbackSource();

        _islandWindow = new IslandWindow(
            _mediaViewModel, _activities, _privacyViewModel, _timer, _notesViewModel, _todosViewModel,
            _viewModel!, wingetService, _audioSource, _settingsStore!.Load());

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
                {
                    _viewModel!.AddClipboardEntry(text);

                    // Nearly free: this monitor was already firing on every copy to build the
                    // history, and nothing was showing that it had.
                    _announcements?.Announce(DateTimeOffset.UtcNow, "Copied", "\uE8C8",
                        Summarise(text));
                }
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard is transiently locked by whichever app just wrote to it -- that write is
            // exactly what triggered this notification, so nothing to capture is actually lost.
        }
    }

    /// <summary>
    /// A few words of whatever was copied, on one line. Newlines out, because a clipboard entry is
    /// as often a paragraph as a word and the pill is one line tall.
    /// </summary>
    private static string Summarise(string text)
    {
        var flattened = string.Join(' ', text.Split(
            ['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return flattened.Length <= 32 ? flattened : flattened[..31] + "…";
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

    /// <summary>
    /// Starts watching which applications are using the camera. Created lazily rather than at
    /// startup: someone who has turned the indicator off should have no registry watch running on
    /// their machine at all.
    /// </summary>
    private void StartDeviceUsageMonitoring()
    {
        if (_deviceUsageMonitor is not null)
            return;

        _deviceUsageMonitor = new DeviceUsageMonitor();

        // Raised from the registry watcher's own thread, like the media and stats sources.
        _deviceUsageMonitor.Changed += (_, usages) =>
            Dispatcher.Invoke(() => _privacyViewModel?.Apply(usages));

        _deviceUsageMonitor.Start();
    }

    /// <summary>
    /// Stops the watch and takes the indicator off the island. Retired outright for the same reason
    /// media is: the grace period is for a camera that was released, not for a feature switched off.
    /// </summary>
    private void StopDeviceUsageMonitoring()
    {
        _deviceUsageMonitor?.Dispose();
        _deviceUsageMonitor = null;

        _privacyViewModel?.Apply([]);
        _privacyViewModel?.Retire();
    }

    /// <summary>
    /// Everything momentary, funnelled into the one announcement. Downloads, screenshots, drives
    /// and the network all come from one watcher; Bluetooth needs a WinRT one of its own but says
    /// the same kind of thing when it fires.
    /// </summary>
    private void StartSystemEvents()
    {
        _systemEvents = new SystemEventSource();
        _systemEvents.Occurred += OnSystemEvent;
        _systemEvents.Start();

        _bluetooth = new BluetoothSource();
        _bluetooth.Occurred += OnSystemEvent;
        _bluetooth.Start();
    }

    // Raised from watcher threads and WinRT callbacks, like every other source here.
    private void OnSystemEvent(object? sender, SystemEvent occurrence) =>
        Dispatcher.Invoke(() => _announcements?.Announce(
            DateTimeOffset.UtcNow, occurrence.Label, occurrence.Glyph, occurrence.Detail));

    private void StartSystemConditions()
    {
        _conditions = new SystemConditionSource();
        _conditions.Changed += (_, conditions) => Dispatcher.Invoke(() =>
        {
            _doNotDisturb!.IsActive = conditions.DoNotDisturb;
            _restartPending!.IsActive = conditions.RestartPending;

            if (conditions.FullDrive is { } drive)
                _lowDisk!.Label = $"{drive.Name} {drive.PercentFree}% free";

            _lowDisk!.IsActive = conditions.FullDrive is not null;
        });

        _conditions.Start();
    }

    /// <summary>
    /// The volume readout. Announced rather than given an activity of its own: it is on screen for
    /// two seconds and then gone, which is exactly what an announcement is.
    /// </summary>
    private void StartVolumeWatch()
    {
        _volume = new VolumeSource();
        _volume.Changed += (_, reading) => Dispatcher.Invoke(() =>
            _announcements?.Announce(
                DateTimeOffset.UtcNow,
                reading.IsMuted ? "Muted" : $"Volume {reading.Level * 100:0}%",
                reading.IsMuted ? "" : ""));

        _volume.Start();

        _audioDevices = new AudioDeviceSource();
        _audioDevices.DefaultOutputChanged += (_, name) => Dispatcher.Invoke(() =>
            _announcements?.Announce(DateTimeOffset.UtcNow, "Output", "", name));

        _audioDevices.Start();
    }

    /// <summary>
    /// Power, on the machines that have any. Asked before anything is registered: a desktop has
    /// nothing to report ever, and an activity that can never light up should not be on the island
    /// at all, let alone a watcher behind it.
    /// </summary>
    private void StartBatteryWatch()
    {
        _battery = new BatterySource();

        if (!_battery.IsPresent)
        {
            _battery.Dispose();
            _battery = null;
            return;
        }

        _activities!.Register(_lowBattery!);

        var charging = (bool?)null;

        _battery.Changed += (_, status) => Dispatcher.Invoke(() =>
        {
            // The charger going in or out is the moment worth interrupting for. A percentage
            // drifting down on its own is not, so only the transition announces -- and never on the
            // very first reading, which would announce the state the machine was already in.
            if (charging is { } was && was != status.IsCharging)
            {
                _announcements?.Announce(
                    DateTimeOffset.UtcNow,
                    status.IsCharging ? "Charging" : "On battery",
                    status.IsCharging ? "" : "",
                    Describe(status));
            }

            charging = status.IsCharging;

            // Running out is a standing condition, so it takes the dot rather than the pill.
            _lowBattery!.Label = $"Battery {status.PercentRemaining ?? 0}%";
            _lowBattery.IsActive = !status.IsCharging
                && status.PercentRemaining is { } percent && percent <= LowBatteryPercent;
        });

        _battery.Start();
    }

    /// <summary>Where a battery stops being background information and starts being a problem.</summary>
    private const int LowBatteryPercent = 20;

    private static string Describe(BatteryStatus status)
    {
        if (status.PercentRemaining is not { } percent)
            return string.Empty;

        // The estimate is missing for the first minute after unplugging, and wrong for a while
        // after that, so it is shown only once Windows is willing to commit to one.
        return status is { IsCharging: false, Remaining: { } left } && left > TimeSpan.FromMinutes(1)
            ? $"{percent}% · {Format(left)} left"
            : $"{percent}%";
    }

    private static string Format(TimeSpan value) =>
        value.TotalHours >= 1 ? $"{(int)value.TotalHours}h {value.Minutes}m" : $"{value.Minutes}m";

    /// <summary>
    /// Registers a stand-in activity that switches itself on and off, for watching the bubble
    /// arrive and leave without needing a camera pointed at anybody.
    ///
    /// It cycles rather than sitting there because the interesting part is the transition: the
    /// bubble growing out of the pill, and the pill taking its place back afterwards.
    /// </summary>
    private void StartDebugActivity()
    {
        _debugActivity = new DebugActivity { Key = "debug", Label = "Debug activity" };
        _activities!.Register(_debugActivity);
        _debugActivity.IsActive = true;

        _debugTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _debugTimer.Tick += (_, _) => _debugActivity.IsActive = !_debugActivity.IsActive;
        _debugTimer.Start();
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
        _settingsWindow.PrivacyIndicatorToggled += show =>
        {
            if (show)
                StartDeviceUsageMonitoring();
            else
                StopDeviceUsageMonitoring();
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
        _deviceUsageMonitor?.Dispose();
        _systemEvents?.Dispose();
        _bluetooth?.Dispose();
        _conditions?.Dispose();
        _volume?.Dispose();
        _battery?.Dispose();
        _audioDevices?.Dispose();

        _activityTimer.Stop();
        _debugTimer?.Stop();
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
