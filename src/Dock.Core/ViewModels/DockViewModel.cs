using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class DockViewModel : ObservableObject
{
    private readonly ConfigStore _configStore;
    private readonly IIconProvider _iconProvider;
    private readonly IAppLauncher _launcher;
    private readonly DockConfig _config;
    private readonly Dictionary<string, DockItemViewModel> _transientRunningItems = new(StringComparer.OrdinalIgnoreCase);

    private IWindowActivator? _windowActivator;
    private ITraySource? _traySource;

    public ObservableCollection<DockItemViewModel> Items { get; } = [];
    public ObservableCollection<TrayIconViewModel> OverflowTrayIcons { get; } = [];

    [ObservableProperty]
    private bool hasTrayIcons;

    [ObservableProperty]
    private TrayIconViewModel? chevronTrayIcon;

    [ObservableProperty]
    private TrayIconViewModel? clockTrayIcon;

    public DockViewModel(ConfigStore configStore, IIconProvider iconProvider, IAppLauncher launcher)
    {
        _configStore = configStore;
        _iconProvider = iconProvider;
        _launcher = launcher;
        _config = configStore.Load();

        foreach (var app in _config.PinnedApps)
            Items.Add(CreateItem(app));
    }

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
}
