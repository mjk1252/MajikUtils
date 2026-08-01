using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Core.Models;

namespace Dock.Core.ViewModels;

public partial class ShelfItemViewModel(ShelfItem item) : ObservableObject
{
    public ShelfItem Item => item;
    public string Name => item.Name;
    public string Path => item.Path;

    [ObservableProperty]
    private byte[]? iconPng;
}
