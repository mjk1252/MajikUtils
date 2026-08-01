using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dock.Core.ViewModels;
using Dock.Interop.Shell;

namespace Dock.App.Views.Panels;

public partial class RecentPanel : UserControl
{
    private readonly FileDragGesture _drag = new();

    public RecentPanel()
    {
        InitializeComponent();
    }

    private DockViewModel ViewModel => (DockViewModel)DataContext;

    /// <summary>
    /// Re-read on every show rather than kept live: the shell's recent-items folder has no change
    /// notification worth subscribing to, and enumerating it (plus extracting an icon per entry)
    /// is too slow to sit on the UI thread, so it happens off-thread each time the tab is opened.
    /// </summary>
    public void Refresh()
    {
        var viewModel = ViewModel;

        Task.Run(() =>
        {
            var files = new RecentFilesProvider().GetRecentFiles(30);
            return viewModel.BuildRecentFileItems(files);
        }).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                Dispatcher.Invoke(() => viewModel.SetRecentFiles(t.Result));
        });
    }

    private void OnRowMouseDown(object sender, MouseButtonEventArgs e) => _drag.Begin(e);

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentFileItemViewModel item })
            return;

        item.OpenCommand.Execute(null);
    }

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentFileItemViewModel item } element)
            _drag.TryDrag(e, element, item.File.Path);
    }
}
