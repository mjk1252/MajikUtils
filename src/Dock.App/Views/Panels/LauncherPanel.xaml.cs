using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.App.Views.Panels;

public partial class LauncherPanel : UserControl
{
    private readonly DispatcherTimer _wingetDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>Raised when a winget install starts, so the host window can show progress on its taskbar button.</summary>
    public event Action? InstallStarted;

    public IWingetService? WingetService { get; set; }

    public LauncherPanel()
    {
        InitializeComponent();
        _wingetDebounceTimer.Tick += OnWingetDebounceElapsed;
        Unloaded += (_, _) => _wingetDebounceTimer.Stop();
    }

    private DockViewModel ViewModel => (DockViewModel)DataContext;

    /// <summary>Puts the caret in the search box every time the panel is shown, so the button acts as "start typing".</summary>
    public void FocusSearch()
    {
        SearchBox.SelectAll();
        Keyboard.Focus(SearchBox);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => FocusSearch();

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        ViewModel.FilterLauncherApps(query);

        _wingetDebounceTimer.Stop();

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            ViewModel.ClearWingetResults();
            return;
        }

        _wingetDebounceTimer.Start();
    }

    private void OnWingetDebounceElapsed(object? sender, EventArgs e)
    {
        _wingetDebounceTimer.Stop();

        var wingetService = WingetService;
        var query = SearchBox.Text;
        if (wingetService is null || string.IsNullOrWhiteSpace(query))
            return;

        ViewModel.BeginWingetSearch();

        Task.Run(() => wingetService.Search(query))
            .ContinueWith(t =>
            {
                var results = t.IsCompletedSuccessfully ? t.Result : [];
                Dispatcher.Invoke(() => ViewModel.SetWingetResults(results));
            });
    }

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppLauncherItemViewModel item })
            return;

        item.LaunchCommand.Execute(null);
    }

    /// <summary>
    /// The Button's own Command does the installing; this only tells the host that one is under
    /// way. Click and Command both fire for the same press, so nothing is being intercepted here.
    /// </summary>
    private void OnInstallClick(object sender, RoutedEventArgs e) => InstallStarted?.Invoke();
}
