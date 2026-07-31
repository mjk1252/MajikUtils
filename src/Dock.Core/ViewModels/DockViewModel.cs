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

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

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
        if (item is null)
            return;

        _config.PinnedApps.RemoveAll(a => a.Id == item.App.Id);
        Items.Remove(item);
        _configStore.Save(_config);
    }
}
