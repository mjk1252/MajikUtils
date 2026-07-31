using System.Windows;
using Dock.Core.Services;
using Dock.Interop.Shell;
using Dock.Interop.Windowing;

namespace Dock.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private bool _loaded;

    public SettingsWindow(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();

        var settings = _settingsStore.Load();
        HideTaskbarCheckBox.IsChecked = settings.HideTaskbar;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        _loaded = true;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;

        var settings = new Core.Models.AppSettings
        {
            HideTaskbar = HideTaskbarCheckBox.IsChecked == true,
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true
        };

        _settingsStore.Save(settings);

        if (settings.HideTaskbar)
        {
            TaskbarController.Hide();
            TaskbarSafety.MarkHidden();
        }
        else
        {
            TaskbarController.Show();
            TaskbarSafety.ClearFlag();
        }

        StartupRegistration.SetEnabled(settings.StartWithWindows);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
