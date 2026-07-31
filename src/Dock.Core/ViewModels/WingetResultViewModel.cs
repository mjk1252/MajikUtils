using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class WingetResultViewModel(WingetResult result, IWingetService wingetService) : ObservableObject
{
    public string Name => result.Name;
    public string Id => result.Id;

    [ObservableProperty]
    private bool isInstalling;

    [RelayCommand]
    private void Install()
    {
        IsInstalling = true;
        wingetService.Install(result);
    }
}
