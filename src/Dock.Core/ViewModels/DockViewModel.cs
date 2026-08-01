using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class DockViewModel : ObservableObject
{
    private readonly ConfigStore _configStore;
    private readonly ShelfStore _shelfStore = new();
    private readonly StackStore _stackStore = new();
    private readonly IIconProvider _iconProvider;
    private readonly IAppLauncher _launcher;
    private readonly DockConfig _config;
    private readonly Dictionary<string, DockItemViewModel> _transientRunningItems = new(StringComparer.OrdinalIgnoreCase);

    private const int MaxClipboardEntries = 25;

    private IWindowActivator? _windowActivator;
    private ITraySource? _traySource;
    private IWingetService? _wingetService;
    private IClipboardWriter? _clipboardWriter;
    private List<AppLauncherItemViewModel> _allLauncherItems = [];
    private string _launcherQuery = "";

    public ObservableCollection<DockItemViewModel> Items { get; } = [];
    public ObservableCollection<TrayIconViewModel> OverflowTrayIcons { get; } = [];
    public ObservableCollection<AppLauncherItemViewModel> LauncherResults { get; } = [];
    public ObservableCollection<WingetResultViewModel> WingetResults { get; } = [];
    public ObservableCollection<RecentFileItemViewModel> RecentFiles { get; } = [];
    public ObservableCollection<ShelfItemViewModel> ShelfItems { get; } = [];
    public ObservableCollection<StackItemViewModel> Stacks { get; } = [];
    public ObservableCollection<ClipboardEntryViewModel> ClipboardHistory { get; } = [];

    [ObservableProperty]
    private bool hasTrayIcons;

    [ObservableProperty]
    private TrayIconViewModel? chevronTrayIcon;

    [ObservableProperty]
    private TrayIconViewModel? clockTrayIcon;

    [ObservableProperty]
    private bool isWingetSearching;

    [ObservableProperty]
    private double cpuPercent;

    [ObservableProperty]
    private double gpuPercent;

    public void UpdateSystemStats(double cpuPercent, double gpuPercent)
    {
        CpuPercent = cpuPercent;
        GpuPercent = gpuPercent;
    }

    public DockViewModel(ConfigStore configStore, IIconProvider iconProvider, IAppLauncher launcher)
    {
        _configStore = configStore;
        _iconProvider = iconProvider;
        _launcher = launcher;
        _config = configStore.Load();

        foreach (var app in _config.PinnedApps)
            Items.Add(CreateItem(app));

        foreach (var shelfItem in _shelfStore.Load())
            ShelfItems.Add(CreateShelfItem(shelfItem));

        foreach (var folder in _stackStore.Load())
            Stacks.Add(CreateStack(folder));
    }

    private ShelfItemViewModel CreateShelfItem(ShelfItem item) => new(item)
    {
        IconPng = _iconProvider.GetIconPng(item.Path, 32)
    };

    private StackItemViewModel CreateStack(StackFolder folder)
    {
        var vm = new StackItemViewModel(folder) { IconPng = _iconProvider.GetIconPng(folder.Path, 48) };
        vm.Refresh(_iconProvider, _launcher);
        return vm;
    }

    public void AddStack(string path)
    {
        if (!Directory.Exists(path))
            return;

        if (Stacks.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        var folder = new StackFolder { Id = Guid.NewGuid().ToString(), Path = path };
        Stacks.Add(CreateStack(folder));
        SaveStacks();
    }

    [RelayCommand]
    private void RemoveStack(StackItemViewModel? item)
    {
        if (item is null)
            return;

        Stacks.Remove(item);
        SaveStacks();
    }

    private void SaveStacks() => _stackStore.Save(Stacks.Select(s => s.Folder).ToList());

    private DockItemViewModel CreateItem(PinnedApp app) => new(app, _launcher)
    {
        IconPng = _iconProvider.GetIconPng(app.ExecutablePath, 48)
    };

    public void AttachRunningApps(IWindowActivator activator)
    {
        _windowActivator = activator;
    }

    public void UpdateRunningApps(IReadOnlyList<RunningAppGroup> groups)
    {
        var activator = _windowActivator;
        if (activator is null)
            return;

        var groupsByPath = new Dictionary<string, RunningAppGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
            groupsByPath[group.ProcessPath] = group;

        var pinnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            if (!item.IsPinned)
                continue;

            pinnedPaths.Add(item.ExecutablePath);
            item.SetRunningState(
                groupsByPath.TryGetValue(item.ExecutablePath, out var group) ? group.Windows : [],
                activator);
        }

        foreach (var key in _transientRunningItems.Keys.ToList())
        {
            if (groupsByPath.ContainsKey(key) && !pinnedPaths.Contains(key))
                continue;

            Items.Remove(_transientRunningItems[key]);
            _transientRunningItems.Remove(key);
        }

        foreach (var group in groups)
        {
            if (pinnedPaths.Contains(group.ProcessPath))
                continue;

            if (!_transientRunningItems.TryGetValue(group.ProcessPath, out var item))
            {
                item = new DockItemViewModel(group.ProcessPath, group.DisplayName, _launcher)
                {
                    IconPng = _iconProvider.GetIconPng(group.ProcessPath, 48)
                };
                _transientRunningItems[group.ProcessPath] = item;
                Items.Add(item);
            }

            item.SetRunningState(group.Windows, activator);
        }
    }

    public void AttachTraySource(ITraySource traySource)
    {
        _traySource = traySource;
    }

    public void UpdateTrayIcons(IReadOnlyList<TrayIcon> icons)
    {
        var traySource = _traySource;
        if (traySource is null)
            return;

        var currentKeys = new HashSet<string>(OverflowTrayIcons.Select(t => TrayIconKey(t.Info)));
        var newKeys = new HashSet<string>(icons.Select(TrayIconKey));

        if (currentKeys.SetEquals(newKeys))
            return;

        OverflowTrayIcons.Clear();
        ChevronTrayIcon = null;
        ClockTrayIcon = null;

        foreach (var icon in icons)
        {
            var vm = new TrayIconViewModel(icon, traySource) { IconPng = icon.IconPng };
            OverflowTrayIcons.Add(vm);

            if (icon.IsChevron)
                ChevronTrayIcon = vm;
            else if (icon.IsClock)
                ClockTrayIcon = vm;
        }

        HasTrayIcons = icons.Count > 0;
    }

    private static string TrayIconKey(TrayIcon icon) => icon.OwnerHandle is { } handle
        ? $"h:{handle}:{icon.IconId}"
        : $"a:{icon.Name}:{(icon.ClickX ?? 0) / 8}:{(icon.ClickY ?? 0) / 8}";

    public void AddPinned(string executablePath)
    {
        if (_config.PinnedApps.Any(a => string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)))
            return;

        var name = Path.GetFileNameWithoutExtension(executablePath);
        var app = new PinnedApp
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.IsNullOrWhiteSpace(name) ? executablePath : name,
            ExecutablePath = executablePath
        };

        _config.PinnedApps.Add(app);
        Items.Add(CreateItem(app));
        _configStore.Save(_config);
    }

    [RelayCommand]
    private void Unpin(DockItemViewModel? item)
    {
        if (item?.App is null)
            return;

        _config.PinnedApps.RemoveAll(a => a.Id == item.App.Id);
        Items.Remove(item);
        _configStore.Save(_config);
    }

    public void AttachWingetService(IWingetService wingetService)
    {
        _wingetService = wingetService;
    }

    public void SetLauncherItems(List<AppLauncherItemViewModel> items)
    {
        _allLauncherItems = items;
        FilterLauncherApps(_launcherQuery);
    }

    public void FilterLauncherApps(string query)
    {
        _launcherQuery = query;
        LauncherResults.Clear();

        var matches = string.IsNullOrWhiteSpace(query)
            ? _allLauncherItems.Take(60)
            : _allLauncherItems.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(60);

        foreach (var item in matches)
            LauncherResults.Add(item);
    }

    public void SetWingetResults(IReadOnlyList<WingetResult> results)
    {
        var wingetService = _wingetService;
        if (wingetService is null)
            return;

        WingetResults.Clear();
        foreach (var result in results)
            WingetResults.Add(new WingetResultViewModel(result, wingetService));

        IsWingetSearching = false;
    }

    public void BeginWingetSearch() => IsWingetSearching = true;

    public void ClearWingetResults()
    {
        WingetResults.Clear();
        IsWingetSearching = false;
    }

    /// <summary>
    /// Builds the view-model wrappers (including icon extraction) for a set of recent files.
    /// Deliberately just returns a plain list rather than touching <see cref="RecentFiles"/>
    /// directly, so callers can do the (slower, icon-extracting) work off the UI thread and
    /// only call <see cref="SetRecentFiles"/> with the finished result on the UI thread.
    /// </summary>
    public List<RecentFileItemViewModel> BuildRecentFileItems(IReadOnlyList<RecentFile> files) =>
        files.Select(f => new RecentFileItemViewModel(f, _launcher) { IconPng = _iconProvider.GetIconPng(f.Path, 32) }).ToList();

    public void SetRecentFiles(List<RecentFileItemViewModel> items)
    {
        RecentFiles.Clear();
        foreach (var item in items)
            RecentFiles.Add(item);
    }

    public void AddToShelf(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (ShelfItems.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        var item = new ShelfItem { Name = name, Path = path };
        ShelfItems.Add(CreateShelfItem(item));
        SaveShelf();
    }

    [RelayCommand]
    private void RemoveFromShelf(ShelfItemViewModel? item)
    {
        if (item is null)
            return;

        ShelfItems.Remove(item);
        SaveShelf();
    }

    private void SaveShelf() => _shelfStore.Save(ShelfItems.Select(s => s.Item).ToList());

    public void AttachClipboardWriter(IClipboardWriter writer)
    {
        _clipboardWriter = writer;
    }

    public void AddClipboardEntry(string text)
    {
        if (_clipboardWriter is not { } writer || string.IsNullOrWhiteSpace(text))
            return;

        // Copying an entry back out writes it to the real clipboard too, which re-triggers the
        // capture path -- without this check that would push a duplicate right back onto the
        // top of the list it was just selected from.
        if (ClipboardHistory.Count > 0 && ClipboardHistory[0].Text == text)
            return;

        ClipboardHistory.Insert(0, new ClipboardEntryViewModel(
            new ClipboardEntry { Text = text, CapturedAt = DateTime.Now }, writer));

        while (ClipboardHistory.Count > MaxClipboardEntries)
            ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);
    }

    [RelayCommand]
    private void ClearClipboardHistory() => ClipboardHistory.Clear();
}
