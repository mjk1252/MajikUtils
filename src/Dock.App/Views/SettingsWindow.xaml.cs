using System.Windows;
using Dock.Core.Services;
using Dock.Interop.Shell;

namespace Dock.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private bool _loaded;

    /// <summary>
    /// Raised when the media island is switched on or off. The island is a live window owned by
    /// the application, so the toggle has to reach it now rather than at the next start.
    /// </summary>
    public event Action<bool>? MediaIslandToggled;

    public SettingsWindow(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();

        var settings = _settingsStore.Load();
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        MediaIslandCheckBox.IsChecked = settings.ShowMediaIsland;
        _loaded = true;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;

        // Read-modify-write rather than building a fresh AppSettings: the panels persist their own
        // placements into the same file, and rebuilding would wipe them on every toggle.
        var settings = _settingsStore.Load();
        settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;

        var showIsland = MediaIslandCheckBox.IsChecked == true;
        var islandChanged = settings.ShowMediaIsland != showIsland;
        settings.ShowMediaIsland = showIsland;

        _settingsStore.Save(settings);

        StartupRegistration.SetEnabled(settings.StartWithWindows);

        if (islandChanged)
            MediaIslandToggled?.Invoke(showIsland);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
