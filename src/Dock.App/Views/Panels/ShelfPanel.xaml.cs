using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dock.Core.ViewModels;
using Dock.Interop.Shell;

namespace Dock.App.Views.Panels;

/// <summary>
/// The drop shelf: files parked here on the way from one place to another.
///
/// Used to be a window of its own with a taskbar button, because the shell restores a minimised
/// window when a drag hovers its button, and that was the only gesture that got a drag to a drop
/// target. The island reaches the same place more directly -- a drag that touches the top of the
/// screen opens it -- so this is now just the list, hosted there.
/// </summary>
public partial class ShelfPanel : UserControl
{
    private static readonly Brush DropHintBorder = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
    private static readonly Brush DropHintFill = new SolidColorBrush(Color.FromArgb(0x14, 0x4F, 0xC3, 0xF7));

    private readonly FileDragGesture _drag = new();

    public ShelfPanel()
    {
        InitializeComponent();
    }

    private DockViewModel ViewModel => (DockViewModel)DataContext;

    /// <summary>Lights the panel up as a target while a drag is over the island holding it.</summary>
    public void SetDropHint(bool active)
    {
        DropHint.BorderBrush = active ? DropHintBorder : Brushes.Transparent;
        DropHint.Background = active ? DropHintFill : Brushes.Transparent;
    }

    /// <summary>Takes the dropped paths. Returns false when the drop carried no files to take.</summary>
    public bool AcceptDrop(IDataObject data)
    {
        SetDropHint(false);

        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return false;
        }

        foreach (var path in paths)
            ViewModel.AddToShelf(path);

        return true;
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
