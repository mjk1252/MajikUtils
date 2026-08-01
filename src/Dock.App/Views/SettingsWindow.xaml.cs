using System.Windows;
using Dock.Core.Services;
using Dock.Interop.Shell;

namespace Dock.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private bool _loaded;

    public SettingsWindow(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();

        StartWithWindowsCheckBox.IsChecked = _settingsStore.Load().StartWithWindows;
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
        _settingsStore.Save(settings);

        StartupRegistration.SetEnabled(settings.StartWithWindows);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
