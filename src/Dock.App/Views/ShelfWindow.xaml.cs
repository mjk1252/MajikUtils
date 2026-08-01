using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dock.App.Views.Panels;
using Dock.Core.ViewModels;
using Dock.Interop.Shell;

namespace Dock.App.Views;

/// <summary>
/// The drop shelf, as its own taskbar button.
///
/// Split out of the drawer specifically to make dropping easy: the shell restores a minimised
/// window when a drag hovers over its taskbar button, so dragging a file down to the Shelf button
/// and waiting a moment opens this window right there, ready to drop into. That gesture needs a
/// button of its own -- it cannot work through a tab inside another panel.
/// </summary>
public partial class ShelfWindow : PanelWindow
{
    /// <summary>Segoe MDL2 "Tiles" -- a tray of held items.</summary>
    private const string ShelfGlyph = "";

    private static readonly BitmapSource ButtonIcon = PanelIcons.RenderGlyph(ShelfGlyph);
    private static readonly string? PinnedIcon = PanelIcons.EnsureIcoOnDisk("shelf", ButtonIcon);

    private static readonly Brush DropHintBorder = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
    private static readonly Brush DropHintFill = new SolidColorBrush(Color.FromArgb(0x14, 0x4F, 0xC3, 0xF7));

    private readonly FileDragGesture _drag = new();

    public ShelfWindow(DockViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        Icon = ButtonIcon;
    }

    protected override string AppId => "Dock.Shelf";
    protected override string PanelArgument => "shelf";
    protected override string DisplayName => "Dock Shelf";
    protected override string? RelaunchIconResource => PinnedIcon;

    private DockViewModel ViewModel => (DockViewModel)DataContext;

    /// <summary>
    /// The dragging application owns the foreground for the whole gesture, so without this the
    /// window would minimise itself out from under the drop the instant it was restored.
    /// </summary>
    private void OnDragEnter(object sender, DragEventArgs e)
    {
        SuppressAutoMinimise = true;
        DropHint.BorderBrush = DropHintBorder;
        DropHint.Background = DropHintFill;
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => EndDrag();

    private void OnDrop(object sender, DragEventArgs e)
    {
        EndDrag();

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        foreach (var path in paths)
            ViewModel.AddToShelf(path);
    }

    private void EndDrag()
    {
        SuppressAutoMinimise = false;
        DropHint.BorderBrush = Brushes.Transparent;
        DropHint.Background = Brushes.Transparent;
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

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnMinimiseClick(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;
}
