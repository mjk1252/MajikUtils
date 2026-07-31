using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class DockItemViewModel : ObservableObject
{
    private readonly IAppLauncher _launcher;

    public PinnedApp App { get; }

    public string Name => App.Name;

    [ObservableProperty]
    private byte[]? iconPng;

    public DockItemViewModel(PinnedApp app, IAppLauncher launcher)
    {
        App = app;
        _launcher = launcher;
    }

    [RelayCommand]
    private void Launch() => _launcher.Launch(App.ExecutablePath, App.Arguments);
}
