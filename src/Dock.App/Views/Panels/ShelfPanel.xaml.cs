using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dock.Core.ViewModels;
using Dock.Interop.Shell;

namespace Dock.App.Views.Panels;

public partial class ShelfPanel : UserControl
{
    private readonly FileDragGesture _drag = new();

    public ShelfPanel()
    {
        InitializeComponent();
    }

    private DockViewModel ViewModel => (DockViewModel)DataContext;

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        foreach (var path in paths)
            ViewModel.AddToShelf(path);
    }

    private void OnRowMouseDown(object sender, MouseButtonEventArgs e) => _drag.Begin(e);

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShelfItemViewModel item })
            return;

        new ProcessAppLauncher().Launch(item.Path);
    }

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShelfItemViewModel item } element)
            _drag.TryDrag(e, element, item.Path);
    }

    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShelfItemViewModel item } element)
            return;

        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Remove from shelf" };
        remove.Click += (_, _) => ViewModel.RemoveFromShelfCommand.Execute(item);
        menu.Items.Add(remove);

        element.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
