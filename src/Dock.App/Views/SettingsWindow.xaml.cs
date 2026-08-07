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

    /// <summary>
    /// Raised when the media island is switched on or off. The island is a live window owned by
    /// the application, so the toggle has to reach it now rather than at the next start.
    /// </summary>
    public event Action<bool>? MediaIslandToggled;

    /// <summary>
    /// Raised when the island's shape, edge position or monitor changes, for the same reason: these
    /// are worth seeing as they are picked, not after a restart.
    /// </summary>
    public event Action<AppSettings>? MediaIslandAppearanceChanged;

    /// <summary>
    /// Raised when the microphone and camera indicator is switched on or off, for the same reason
    /// as the media toggle: it starts and stops a live watch on the registry.
    /// </summary>
    public event Action<bool>? PrivacyIndicatorToggled;

    public SettingsWindow(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();

        var settings = _settingsStore.Load();
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        MediaIslandCheckBox.IsChecked = settings.ShowMediaIsland;
        PrivacyIndicatorCheckBox.IsChecked = settings.ShowPrivacyIndicator;

        IslandShapeCombo.SelectedIndex = (int)settings.IslandShape;
        IslandAlignmentCombo.SelectedIndex = (int)settings.IslandAlignment;
        PopulateMonitors(settings.IslandMonitor);

        _loaded = true;
    }

    /// <summary>
    /// Lists the attached screens, ahead of a "follow the primary" entry that stores no name at all
    /// -- the right answer for anyone who just wants it wherever their main screen is, and the
    /// fallback for a saved screen that has since been unplugged.
    /// </summary>
    private void PopulateMonitors(string selectedDeviceName)
    {
        IslandMonitorCombo.Items.Add(new MonitorInfo("", "Primary monitor", true));

        foreach (var monitor in MonitorPlacement.Enumerate())
        {
            IslandMonitorCombo.Items.Add(monitor with
            {
                Label = monitor.IsPrimary ? $"{monitor.Label} — primary" : monitor.Label
            });
        }

        for (var i = 0; i < IslandMonitorCombo.Items.Count; i++)
        {
            if (IslandMonitorCombo.Items[i] is MonitorInfo m &&
                string.Equals(m.DeviceName, selectedDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                IslandMonitorCombo.SelectedIndex = i;
                return;
            }
        }

        IslandMonitorCombo.SelectedIndex = 0;
    }

    /// <summary>ComboBox hands out its own event args, which the shared handler has no use for.</summary>
    private void OnComboChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        OnSettingChanged(sender, e);

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

        var showPrivacy = PrivacyIndicatorCheckBox.IsChecked == true;
        var privacyChanged = settings.ShowPrivacyIndicator != showPrivacy;
        settings.ShowPrivacyIndicator = showPrivacy;

        settings.IslandShape = (IslandShape)Math.Max(0, IslandShapeCombo.SelectedIndex);
        settings.IslandAlignment = (IslandAlignment)Math.Max(0, IslandAlignmentCombo.SelectedIndex);
        settings.IslandMonitor = IslandMonitorCombo.SelectedItem is MonitorInfo monitor
            ? monitor.DeviceName
            : "";

        _settingsStore.Save(settings);

        StartupRegistration.SetEnabled(settings.StartWithWindows);

        if (islandChanged)
            MediaIslandToggled?.Invoke(showIsland);

        if (privacyChanged)
            PrivacyIndicatorToggled?.Invoke(showPrivacy);

        MediaIslandAppearanceChanged?.Invoke(settings);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
