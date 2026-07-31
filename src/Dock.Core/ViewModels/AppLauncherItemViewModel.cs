using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class AppLauncherItemViewModel : ObservableObject
{
    private readonly IAppLauncher _launcher;

    public InstalledApp App { get; }
    public string Name => App.Name;

    [ObservableProperty]
    private byte[]? iconPng;

    public AppLauncherItemViewModel(InstalledApp app, IAppLauncher launcher)
    {
        App = app;
        _launcher = launcher;
    }

    [RelayCommand]
    private void Launch() => _launcher.Launch(App.ExecutablePath);
}
