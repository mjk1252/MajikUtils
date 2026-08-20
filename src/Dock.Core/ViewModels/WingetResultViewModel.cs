using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class WingetResultViewModel(
    WingetResult result,
    IWingetService wingetService,
    IWingetProgress? progress = null) : ObservableObject
{
    public string Name => result.Name;

    public string Id => result.Id;

    [ObservableProperty]
    private bool isInstalling;

    /// <summary>
    /// Starts the install and hands it to a background thread.
    ///
    /// It used to be fire-and-forget because it only launched a console window and returned. Now
    /// that it waits for winget so it can say when the install finished, running it inline would
    /// freeze the island for the length of the install -- which is exactly the interval the whole
    /// change exists to make visible.
    /// </summary>
    [RelayCommand]
    private void Install()
    {
        if (IsInstalling)
            return;

        IsInstalling = true;

        Task.Run(() => wingetService.Install(result, new Relay(this, progress)));
    }

    /// <summary>
    /// Passes the install's progress on to whoever is drawing it, and takes the one piece of it
    /// this row needs for itself: knowing when to stop saying "installing".
    /// </summary>
    private sealed class Relay(WingetResultViewModel row, IWingetProgress? outer) : IWingetProgress
    {
        public void Progress(string label, double? fraction) => outer?.Progress(label, fraction);

        public void Finished(string label, bool succeeded)
        {
            row.IsInstalling = false;
            outer?.Finished(label, succeeded);
        }
    }
}
