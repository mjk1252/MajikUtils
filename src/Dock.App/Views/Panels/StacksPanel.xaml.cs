using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dock.Core.ViewModels;

namespace Dock.App.Views.Panels;

/// <summary>
/// Manages which folders are stacks. Opening one happens at its own taskbar button
/// (<see cref="StackWindow"/>), not here -- the whole point of a stack having its own button is
/// that reaching its contents does not route through this panel.
/// </summary>
public partial class StacksPanel : UserControl
{
    public StacksPanel()
    {
        InitializeComponent();
    }

    private DockViewModel ViewModel => (DockViewModel)DataContext;

    private void OnAddFolderClick(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Add a folder as a stack" };
        if (dialog.ShowDialog() == true)
            ViewModel.AddStack(dialog.FolderName);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        // A dropped file is taken as a request to add the folder holding it, since only folders
        // can be stacks and dropping a file into this panel has no other sensible meaning.
        foreach (var path in paths)
            ViewModel.AddStack(Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path);
    }

    private void OnRemoveClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StackItemViewModel item })
            return;

        ViewModel.RemoveStackCommand.Execute(item);
        e.Handled = true;
    }

    private void OnStackRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StackItemViewModel item } element)
            return;

        e.Handled = true;

        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Remove stack" };
        remove.Click += (_, _) => ViewModel.RemoveStackCommand.Execute(item);
        menu.Items.Add(remove);

        element.ContextMenu = menu;
        menu.IsOpen = true;
    }
}
