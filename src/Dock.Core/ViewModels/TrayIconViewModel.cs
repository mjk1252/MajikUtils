using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class TrayIconViewModel : ObservableObject
{
    private readonly ITraySource _traySource;

    public TrayIcon Info { get; }

    [ObservableProperty]
    private byte[]? iconPng;

    public TrayIconViewModel(TrayIcon info, ITraySource traySource)
    {
        Info = info;
        _traySource = traySource;
    }

    [RelayCommand]
    private void Click() => _traySource.Invoke(Info, rightClick: false);

    [RelayCommand]
    private void RightClick() => _traySource.Invoke(Info, rightClick: true);
}
