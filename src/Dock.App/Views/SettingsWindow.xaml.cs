using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Interop.Shell;
using Dock.Interop.Windowing;

namespace Dock.App.Views;

public partial class SettingsWindow : Window
{
    // RegisterHotKey's own bit values -- duplicated from Dock.Interop.Native.NativeMethods rather
    // than referenced, because a modifier held here has to survive a round trip through
    // Dock.Core.Models.HotkeyBinding, which cannot see that project at all.
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly SettingsStore _settingsStore;
    private bool _loaded;

    /// <summary>The button currently waiting for a key combination, or null when none is.</summary>
    private Button? _recordingButton;

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

    /// <summary>Raised when announcements are switched on or off -- starts and stops their watchers.</summary>
    public event Action<bool>? AnnouncementsToggled;

    /// <summary>Raised when the standing conditions are switched on or off -- starts and stops their watchers.</summary>
    public event Action<bool>? ConditionsToggled;

    /// <summary>Raised when the volume mixer's right to claim the island changes.</summary>
    public event Action<bool>? VolumeMixerToggled;

    /// <summary>Raised the instant a new clipboard-history shortcut is recorded.</summary>
    public event Action<HotkeyBinding>? ClipboardHotkeyChanged;

    /// <summary>Raised the instant a new command-palette shortcut is recorded.</summary>
    public event Action<HotkeyBinding>? PaletteHotkeyChanged;

    public SettingsWindow(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();

        var settings = _settingsStore.Load();
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        MediaIslandCheckBox.IsChecked = settings.ShowMediaIsland;
        PrivacyIndicatorCheckBox.IsChecked = settings.ShowPrivacyIndicator;
        AnnouncementsCheckBox.IsChecked = settings.ShowAnnouncements;
        ConditionsCheckBox.IsChecked = settings.ShowConditions;
        VolumeMixerCheckBox.IsChecked = settings.ShowVolumeMixer;

        IslandShapeCombo.SelectedIndex = (int)settings.IslandShape;
        IslandAlignmentCombo.SelectedIndex = (int)settings.IslandAlignment;
        PopulateMonitors(settings.IslandMonitor);

        ClipboardHotkeyButton.Content = FormatHotkey(settings.ClipboardHotkey);
        PaletteHotkeyButton.Content = FormatHotkey(settings.PaletteHotkey);

        PreviewKeyDown += OnPreviewKeyDownWhileRecording;

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

        var showAnnouncements = AnnouncementsCheckBox.IsChecked == true;
        var announcementsChanged = settings.ShowAnnouncements != showAnnouncements;
        settings.ShowAnnouncements = showAnnouncements;

        var showConditions = ConditionsCheckBox.IsChecked == true;
        var conditionsChanged = settings.ShowConditions != showConditions;
        settings.ShowConditions = showConditions;

        var showVolumeMixer = VolumeMixerCheckBox.IsChecked == true;
        var volumeMixerChanged = settings.ShowVolumeMixer != showVolumeMixer;
        settings.ShowVolumeMixer = showVolumeMixer;

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

        if (announcementsChanged)
            AnnouncementsToggled?.Invoke(showAnnouncements);

        if (conditionsChanged)
            ConditionsToggled?.Invoke(showConditions);

        if (volumeMixerChanged)
            VolumeMixerToggled?.Invoke(showVolumeMixer);

        MediaIslandAppearanceChanged?.Invoke(settings);
    }

    /// <summary>
    /// Arms one of the two hotkey buttons. The button's own content becomes the prompt, so there is
    /// nothing else on screen to say recording is happening -- the box that will show the answer is
    /// the one asking the question.
    /// </summary>
    private void OnRecordHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (_recordingButton is { } previous)
            RestoreLabel(previous);

        _recordingButton = (Button)sender;
        _recordingButton.Content = "Press a key combination…";
    }

    /// <summary>
    /// Reads the next real key while a button is armed. Modifier keys alone are not a hotkey and
    /// have to be waited past, since Ctrl on its own arriving first would otherwise be recorded as
    /// "no key at all".
    /// </summary>
    private void OnPreviewKeyDownWhileRecording(object sender, KeyEventArgs e)
    {
        if (_recordingButton is not { } button)
            return;

        // Alt-chorded keys arrive as Key.System with the real key on SystemKey instead.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            e.Handled = true;
            return;
        }

        if (key == Key.Escape)
        {
            RestoreLabel(button);
            _recordingButton = null;
            e.Handled = true;
            return;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var modifiers = ToWin32Modifiers(Keyboard.Modifiers);

        // A hotkey with no modifier at all would swallow an ordinary keystroke everywhere in
        // Windows, so a bare key is ignored rather than accepted -- recording just keeps waiting.
        if (modifiers == 0)
        {
            e.Handled = true;
            return;
        }

        var binding = new HotkeyBinding(modifiers, vk);
        button.Content = FormatHotkey(binding);
        _recordingButton = null;
        e.Handled = true;

        if (ReferenceEquals(button, ClipboardHotkeyButton))
        {
            SaveHotkey(s => s.ClipboardHotkey = binding);
            ClipboardHotkeyChanged?.Invoke(binding);
        }
        else if (ReferenceEquals(button, PaletteHotkeyButton))
        {
            SaveHotkey(s => s.PaletteHotkey = binding);
            PaletteHotkeyChanged?.Invoke(binding);
        }
    }

    private void SaveHotkey(Action<AppSettings> apply)
    {
        var settings = _settingsStore.Load();
        apply(settings);
        _settingsStore.Save(settings);
    }

    private void RestoreLabel(Button button)
    {
        var settings = _settingsStore.Load();
        button.Content = ReferenceEquals(button, ClipboardHotkeyButton)
            ? FormatHotkey(settings.ClipboardHotkey)
            : FormatHotkey(settings.PaletteHotkey);
    }

    private static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= ModWin;
        return result;
    }

    private static string FormatHotkey(HotkeyBinding binding)
    {
        var parts = new List<string>();
        if ((binding.Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((binding.Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((binding.Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((binding.Modifiers & ModWin) != 0) parts.Add("Win");

        parts.Add(KeyInterop.KeyFromVirtualKey((int)binding.Key).ToString());

        return string.Join(" + ", parts);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
