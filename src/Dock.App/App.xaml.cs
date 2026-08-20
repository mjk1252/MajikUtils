using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Dock.App.Views;
using Dock.Core.Models;
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

    private HotkeyListener? _hotkeys;
    private VolumeMixerSource? _volumeMixerSource;

    /// <summary>
    /// The island's one slot for long-running work. Shared rather than one per job: the pill has
    /// room for a single ring, and two installs at once is a rarer thing than the complexity of
    /// deciding which of them gets to be drawn.
    /// </summary>
    private ProgressActivity? _progress;
    private VolumeMixerActivity? _volumeMixer;

    private readonly UpdateService _updates = new();

    /// <summary>
    /// Long enough that a background utility running for days does not hammer GitHub's API, short
    /// enough that a release does not sit unnoticed for a week on a machine that is rarely
    /// restarted. The first check happens right at startup, outside this timer.
    /// </summary>
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };

    private readonly LrcLibLyricsProvider _lyrics = new();

    /// <summary>The (artist, title) lyrics were last fetched for, so a snapshot that has not
    /// actually changed track does not re-fetch on every one of the several a second the media
    /// session republishes while nothing about the song has moved.</summary>
    private (string Artist, string Title)? _lyricsFor;

    // Conditions keep their watchers running even when switched off in Settings -- this flag is
    // what actually gates the dot, so the toggle takes effect the instant it is clicked rather than
    // needing SystemConditionSource and BatterySource restarted around it. Announcements take the
    // same approach but the gate lives on AnnouncementActivity itself; see its Enabled property.
    private bool _showConditions = true;

    private const int ClipboardHotkeyId = 1;
    private const int PaletteHotkeyId = 2;

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

    /// <summary>
    /// The two brushes every accented thing in the app reads from: the colour taken off whatever
    /// artwork is playing, and the same hue at plate strength behind a lit control.
    ///
    /// Seeded here rather than declared in App.xaml only so that there is one obvious place to read
    /// the starting colours. They are replaced wholesale every time the artwork changes: a
    /// ResourceDictionary freezes every Freezable put into it, so an accent brush can never be
    /// edited in place, and every reference to these two keys in the XAML is a DynamicResource for
    /// exactly that reason. See IslandWindow.Recolour.
    ///
    /// The seeds are what the island looks like with nothing playing: plain white, and a faint
    /// white plate. <see cref="Views.IslandWindow"/> is the only thing that ever writes them.
    /// </summary>
    private static void InstallAccentBrushes()
    {
        Current.Resources["IslandAccentBrush"] =
            new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));

        Current.Resources["IslandAccentSoftBrush"] =
            new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        InstallAccentBrushes();

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
        _viewModel.AttachWingetService(wingetService, new IslandProgressReporter(this));
        LoadInstalledAppsAsync();

        var startupSettings = _settingsStore.Load();
        _showConditions = startupSettings.ShowConditions;

        CreateIsland(wingetService, startupSettings);
        _announcements!.Enabled = startupSettings.ShowAnnouncements;
        CreateStackWindows();
        StartUpdateChecks();
        StartClipboardMonitor();
        StartHotkeys(startupSettings);
        StartSystemStats();

        if (startupSettings.ShowMediaIsland)
            StartMediaMonitoring();

        if (startupSettings.ShowPrivacyIndicator)
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

    /// <summary>
    /// Puts a background job onto the island.
    ///
    /// This is where the marshalling lives, and it lives here because this is the first layer that
    /// knows there is a dispatcher at all: <c>IWingetProgress</c> is called from whatever thread is
    /// doing the work, and Dock.Core has no idea what a UI thread is.
    /// </summary>
    private sealed class IslandProgressReporter(App app) : IWingetProgress
    {
        public void Progress(string label, double? fraction) =>
            app.Dispatcher.Invoke(() => app._progress?.Report(label, fraction));

        public void Finished(string label, bool succeeded) =>
            app.Dispatcher.Invoke(() =>
            {
                // A failure is worth a moment of the pill too. It says what happened and goes on
                // its own, which is more than the console window it replaced ever managed.
                app._progress?.Finish(DateTimeOffset.UtcNow, label);
                app._announcements?.Announce(DateTimeOffset.UtcNow, label,
                    succeeded ? "\uE930" : "\uE783", string.Empty);
            });
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
    private void CreateIsland(IWingetService wingetService, AppSettings startupSettings)
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
        _progress = new ProgressActivity();

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

        // The mixer's source is polled rather than pushed (see VolumeMixerSource), but it is
        // otherwise wired exactly like every other reading here: Changed on a background thread,
        // applied to the view model on the dispatcher.
        _volumeMixerSource = new VolumeMixerSource();
        _volumeMixer = new VolumeMixerActivity(_iconProvider!, _volumeMixerSource)
        {
            AllowPillClaim = startupSettings.ShowVolumeMixer
        };
        _volumeMixerSource.Changed += (_, sessions) => Dispatcher.Invoke(() => _volumeMixer.Apply(sessions));
        _volumeMixerSource.Start();

        _activities = new IslandActivityHost();
        _activities.Tick(DateTimeOffset.UtcNow);
        _activities.Register(_mediaViewModel);
        _activities.Register(_privacyViewModel);
        _activities.Register(_announcements);
        _activities.Register(_timer);
        _activities.Register(_progress);
        _activities.Register(_doNotDisturb);
        _activities.Register(_restartPending);
        _activities.Register(_lowDisk);
        _activities.Register(_volumeMixer);

        // One clock for everything that measures time on the island: the host's linger windows, an
        // announcement's two and a half seconds, and the timer's countdown.
        _activityTimer.Tick += (_, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            _announcements.Tick(now);
            _timer.Tick(now);
            _progress!.Tick(now);
            _activities.Tick(now);
        };

        _activityTimer.Start();

        // Media notifications arrive on the thread pool, like the system stats below.
        _mediaSource.Changed += (_, snapshot) => Dispatcher.Invoke(() =>
        {
            _mediaViewModel.Apply(snapshot);
            RequestLyricsIfTrackChanged(snapshot);
        });

        // The equalizer bars read the speakers directly. Created here and owned for the whole run;
        // the island starts and stops the capture as the bars come and go.
        _audioSource = new AudioLoopbackSource();

        // Notes and todos are still two stores and two view models -- they are genuinely different
        // things and they persist differently. They are merged into one surface, not one model.
        var capture = new CaptureViewModel(_todosViewModel!, _notesViewModel!, new ClipboardWriter());

        _islandWindow = new IslandWindow(
            _mediaViewModel, _activities, _privacyViewModel, _timer, capture, CreatePalette(),
            _viewModel!, wingetService, _audioSource, _volumeMixer, startupSettings);

        _islandWindow.SettingsRequested += ShowSettingsWindow;
        _islandWindow.ExitRequested += Shutdown;
        _islandWindow.RestartForUpdateRequested += _updates.ApplyAndRestart;
        _islandWindow.CheckForUpdatesRequested += () => _ = CheckForUpdatesManuallyAsync();
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

    /// <summary>
    /// Builds the command palette. Its window is created once, alongside the island, and shown and
    /// hidden from then on -- the same lifetime a taskbar-less popup like this needs is the one
    /// PanelWindow gives its windows for a different reason (a live taskbar button), so it is worth
    /// repeating by hand here rather than pulled in from a base class built around one.
    /// </summary>
    /// <summary>
    /// Builds the palette's view model. There is no palette window any more -- the island hosts the
    /// results, so this is the ranking layer and its one outward wire, nothing else.
    /// </summary>
    private CommandPaletteViewModel CreatePalette()
    {
        var palette = new CommandPaletteViewModel(_viewModel!);

        palette.StackActivationRequested += stack =>
        {
            if (_stackWindows.TryGetValue(stack, out var window))
                window.ShowPanel();
        };

        return palette;
    }

    /// <summary>
    /// Checks for an update immediately, then again on <see cref="_updateTimer"/>'s interval for
    /// as long as MajikUtils keeps running. Once one downloads, the gear menu is told and stops
    /// checking -- there is nothing more recent to find until this one is applied.
    /// </summary>
    private void StartUpdateChecks()
    {
        _ = RunUpdateCheckAsync();

        _updateTimer.Tick += (_, _) => _ = RunUpdateCheckAsync();
        _updateTimer.Start();
    }

    private async Task RunUpdateCheckAsync()
    {
        if (_updates.UpdateReady)
            return;

        await _updates.CheckAndDownloadAsync();

        if (_updates.UpdateReady)
            _islandWindow?.SetUpdateAvailable(true);
    }

    /// <summary>
    /// The gear menu's "Check for updates". Unlike the silent background check, this one owes
    /// whoever clicked it an answer -- announced the same way a volume change or a copy is, since
    /// it is exactly that shape: something just happened, say so for a couple of seconds, done.
    /// </summary>
    private async Task CheckForUpdatesManuallyAsync()
    {
        _announcements?.Announce(DateTimeOffset.UtcNow, "Checking for updates", "");

        var result = await _updates.CheckAndDownloadAsync();

        var (label, glyph) = result switch
        {
            UpdateCheckResult.UpdateReady => ("Update ready to install", ""),
            UpdateCheckResult.UpToDate => ("MajikUtils is up to date", ""),
            UpdateCheckResult.NotInstalled => ("Auto-update isn't available in this copy", ""),
            _ => ("Couldn't check for updates", "")
        };

        _announcements?.Announce(DateTimeOffset.UtcNow, label, glyph);

        if (result == UpdateCheckResult.UpdateReady)
            _islandWindow?.SetUpdateAvailable(true);
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
        _clipboardMonitor.ClipboardChanged += () => Dispatcher.Invoke(CaptureClipboard);
        _clipboardMonitor.Start();
    }

    /// <summary>
    /// The two hotkeys share one listener now (see <see cref="HotkeyListener"/>), which is what
    /// lets Settings rebind either one without touching the other's registration.
    /// </summary>
    private void StartHotkeys(AppSettings settings)
    {
        _hotkeys = new HotkeyListener();
        _hotkeys.Start();
        _hotkeys.Register(ClipboardHotkeyId, settings.ClipboardHotkey.Modifiers, settings.ClipboardHotkey.Key);
        _hotkeys.Register(PaletteHotkeyId, settings.PaletteHotkey.Modifiers, settings.PaletteHotkey.Key);

        _hotkeys.HotkeyPressed += id => Dispatcher.Invoke(() =>
        {
            if (id == ClipboardHotkeyId)
                _islandWindow?.ShowSection(IslandSection.Clipboard);
            else if (id == PaletteHotkeyId)
                _islandWindow?.ShowSearch();
        });
    }

    /// <summary>
    /// Records whatever just landed on the clipboard.
    ///
    /// The order the three formats are tried in is the whole of the logic, and it is the part worth
    /// reading twice: a copy usually offers several formats at once, and only one of them is what
    /// the user meant.
    ///
    /// Files first, because a set of paths is never incidentally present -- an app that offers a
    /// drop list is an app that was asked to copy files.
    ///
    /// Then text, ahead of the image, which is the counter-intuitive one. Excel, Word and
    /// PowerPoint all put a *picture* of the selection on the clipboard next to its text, so trying
    /// the image first turns every copied spreadsheet cell into a screenshot of a spreadsheet cell.
    /// Nothing is lost by the other ordering: the things that genuinely are images -- a snip, a
    /// browser's "copy image" -- put no plain text on at all, so they still fall through to here.
    /// </summary>
    private void CaptureClipboard()
    {
        try
        {
            var entry = ReadClipboardEntry();
            if (entry is null)
                return;

            _viewModel!.AddClipboardEntry(entry);

            // Nearly free: this monitor was already firing on every copy to build the history, and
            // nothing was showing that it had.
            _announcements?.Announce(DateTimeOffset.UtcNow, "Copied", GlyphFor(entry.Kind),
                Summarise(entry.Text));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard is transiently locked by whichever app just wrote to it -- that write is
            // exactly what triggered this notification, so nothing to capture is actually lost.
        }
        catch (System.OutOfMemoryException)
        {
            // GetImage on something enormous. Dropping the entry is the right outcome; taking the
            // whole app down over a copy that was too big to hold is not.
        }
    }

    private static ClipboardEntry? ReadClipboardEntry()
    {
        var now = DateTime.Now;

        if (System.Windows.Clipboard.ContainsFileDropList())
        {
            var paths = System.Windows.Clipboard.GetFileDropList()
                .Cast<string?>()
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .ToList();

            if (paths.Count > 0)
                return ClipboardEntry.ForFiles(paths, now);
        }

        if (System.Windows.Clipboard.ContainsText())
        {
            var text = System.Windows.Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(text))
                return ClipboardEntry.ForText(text, now);
        }

        if (System.Windows.Clipboard.ContainsImage() && System.Windows.Clipboard.GetImage() is { } image)
        {
            var png = EncodePng(image);
            if (png.Length > 0)
                return ClipboardEntry.ForImage(png, image.PixelWidth, image.PixelHeight, now);
        }

        return null;
    }

    /// <summary>
    /// PNG rather than the BitmapSource itself, because the history holds these for the life of the
    /// process and a decoded 4K frame is four times the size of the file it came from. It is also
    /// the format everything else in this app already passes pictures around as.
    /// </summary>
    private static byte[] EncodePng(System.Windows.Media.Imaging.BitmapSource image)
    {
        try
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));

            using var stream = new System.IO.MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception)
        {
            // Whatever was on the clipboard claimed to be an image and would not encode as one.
            // An entry that cannot be put back is not worth a row.
            return [];
        }
    }

    private static string GlyphFor(ClipboardKind kind) => kind switch
    {
        ClipboardKind.Image => "\uEB9F",
        ClipboardKind.Files => "\uE8B7",
        _ => "\uE8C8"
    };

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
        _lyricsFor = null;
    }

    /// <summary>
    /// Kicks off a lyrics lookup the moment the title or artist actually changes, rather than on
    /// every snapshot -- the session republishes several times a second while nothing about the
    /// track has moved, and re-fetching on each one would hammer lrclib.net for the same answer.
    /// </summary>
    private void RequestLyricsIfTrackChanged(MediaSnapshot? snapshot)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Title))
            return;

        var key = (snapshot.Artist, snapshot.Title);
        if (_lyricsFor == key)
            return;

        _lyricsFor = key;
        _mediaViewModel!.ClearLyrics();

        Task.Run(() => _lyrics.GetLyricsAsync(
                snapshot.Artist, snapshot.Title, snapshot.Duration, CancellationToken.None))
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully || t.Result is not { Count: > 0 } lines)
                    return;

                Dispatcher.Invoke(() =>
                {
                    // Playback may have moved on to a different track while this was in flight;
                    // the answer is only worth applying if it is still the one on screen.
                    if (_lyricsFor == key)
                        _mediaViewModel!.SetLyrics(lines);
                });
            });
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

    // Raised from watcher threads and WinRT callbacks, like every other source here. Whether this
    // actually reaches the island is AnnouncementActivity.Enabled's business, not this method's --
    // see its doc comment for why the gate lives there instead of on each of these watchers.
    private void OnSystemEvent(object? sender, SystemEvent occurrence) =>
        Dispatcher.Invoke(() => _announcements?.Announce(
            DateTimeOffset.UtcNow, occurrence.Label, occurrence.Glyph, occurrence.Detail));

    private void StartSystemConditions()
    {
        _conditions = new SystemConditionSource();
        _conditions.Changed += (_, conditions) => Dispatcher.Invoke(() =>
        {
            if (!_showConditions)
                return;

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
            _lowBattery.IsActive = _showConditions && !status.IsCharging
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

        _settingsWindow = new SettingsWindow(_settingsStore!, _updates.CurrentVersion);
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
        _settingsWindow.AnnouncementsToggled += show =>
        {
            if (_announcements is not null)
                _announcements.Enabled = show;
        };

        _settingsWindow.ConditionsToggled += show =>
        {
            _showConditions = show;

            // Stopping the watcher would leave whatever it last reported stuck lit; forcing every
            // reading to false is what actually takes the dot off the island the instant this is
            // switched off, rather than at its next poll.
            if (!show)
            {
                _doNotDisturb!.IsActive = false;
                _restartPending!.IsActive = false;
                _lowDisk!.IsActive = false;

                if (_lowBattery is not null)
                    _lowBattery.IsActive = false;
            }
        };

        _settingsWindow.VolumeMixerToggled += show =>
        {
            if (_volumeMixer is not null)
                _volumeMixer.AllowPillClaim = show;
        };

        _settingsWindow.ClipboardHotkeyChanged += binding =>
            _hotkeys?.Register(ClipboardHotkeyId, binding.Modifiers, binding.Key);

        _settingsWindow.PaletteHotkeyChanged += binding =>
            _hotkeys?.Register(PaletteHotkeyId, binding.Modifiers, binding.Key);

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
        _hotkeys?.Dispose();
        _systemStatsSource?.Dispose();
        _deviceUsageMonitor?.Dispose();
        _systemEvents?.Dispose();
        _bluetooth?.Dispose();
        _conditions?.Dispose();
        _volume?.Dispose();
        _battery?.Dispose();
        _audioDevices?.Dispose();
        _volumeMixerSource?.Dispose();

        _activityTimer.Stop();
        _debugTimer?.Stop();
        _updateTimer.Stop();
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
