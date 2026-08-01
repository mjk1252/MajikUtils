using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class StackEntryViewModel : ObservableObject
{
    private readonly IAppLauncher _launcher;

    public string Path { get; }
    public string Name { get; }
    public bool IsDirectory { get; }

    [ObservableProperty]
    private byte[]? iconPng;

    // Position along the fan arc, relative to the stack icon -- computed by DockWindow right
    // before the flyout opens (see ComputeFanOffsets), not on construction, since it depends on
    // dock edge and sibling count rather than anything about this entry itself.
    [ObservableProperty]
    private double fanOffsetX;

    [ObservableProperty]
    private double fanOffsetY;

    public StackEntryViewModel(string path, bool isDirectory, IAppLauncher launcher, string? displayName = null)
    {
        Path = path;
        IsDirectory = isDirectory;
        Name = displayName ?? System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        _launcher = launcher;
    }

    [RelayCommand]
    private void Open() => _launcher.Launch(Path);
}
