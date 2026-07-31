using System.Windows;
using Dock.Core.Models;
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

        switch (settings.Position)
        {
            case DockPosition.Left:
                PositionLeftRadio.IsChecked = true;
                break;
            case DockPosition.Right:
                PositionRightRadio.IsChecked = true;
                break;
            default:
                PositionBottomRadio.IsChecked = true;
                break;
        }

        _loaded = true;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;

        var position = PositionLeftRadio.IsChecked == true ? DockPosition.Left
            : PositionRightRadio.IsChecked == true ? DockPosition.Right
            : DockPosition.Bottom;

        var previousSettings = _settingsStore.Load();
        var settings = new AppSettings
        {
            HideTaskbar = HideTaskbarCheckBox.IsChecked == true,
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            Position = position
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

        if (settings.Position != previousSettings.Position && System.Windows.Application.Current is App app)
            app.RebuildDockWindows(settings.Position);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
