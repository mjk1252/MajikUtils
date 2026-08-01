using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dock.Core.ViewModels;

namespace Dock.App.Views.Panels;

public partial class ClipboardPanel : UserControl
{
    public ClipboardPanel()
    {
        InitializeComponent();
    }

    private DockViewModel ViewModel => (DockViewModel)DataContext;

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ClipboardEntryViewModel item })
            return;

        item.CopyCommand.Execute(null);
    }

    private void OnClearClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel.ClearClipboardHistoryCommand.Execute(null);
        e.Handled = true;
    }
}
