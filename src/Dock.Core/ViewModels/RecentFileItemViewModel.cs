using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class RecentFileItemViewModel : ObservableObject
{
    private readonly IAppLauncher _launcher;

    public RecentFile File { get; }
    public string Name => File.Name;

    [ObservableProperty]
    private byte[]? iconPng;

    public RecentFileItemViewModel(RecentFile file, IAppLauncher launcher)
    {
        File = file;
        _launcher = launcher;
    }

    [RelayCommand]
    private void Open() => _launcher.Launch(File.Path);
}
