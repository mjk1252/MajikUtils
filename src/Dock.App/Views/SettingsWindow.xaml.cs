using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    /// <summary>Raised when birthdays are allowed to claim the island, or stopped from doing so.</summary>
    public event Action<bool>? BirthdaysToggled;

    /// <summary>Raised by "Edit list...", which the App answers by opening birthdays.csv.</summary>
    public event Action? EditBirthdaysRequested;

    /// <summary>
    /// Raised on every keystroke in the three colour boxes that leaves a usable set of colours.
    ///
    /// Per keystroke rather than on losing focus, because picking a colour is a thing you do by
    /// looking at it: a box that only applied once you clicked elsewhere would make choosing a
    /// gradient a sequence of guesses.
    /// </summary>
    public event Action<AppSettings>? ThemeChanged;

    /// <summary>Raised the instant a new clipboard-history shortcut is recorded.</summary>
    public event Action<HotkeyBinding>? ClipboardHotkeyChanged;


    /// <summary>Raised the instant a new command-palette shortcut is recorded.</summary>
    public event Action<HotkeyBinding>? PaletteHotkeyChanged;

    public SettingsWindow(SettingsStore settingsStore, string version)
    {
        _settingsStore = settingsStore;
        InitializeComponent();

        VersionText.Text = $"v{version}";

        var settings = _settingsStore.Load();
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        MediaIslandCheckBox.IsChecked = settings.ShowMediaIsland;
        ClockCheckBox.IsChecked = settings.ShowClock;
        PrivacyIndicatorCheckBox.IsChecked = settings.ShowPrivacyIndicator;
        AnnouncementsCheckBox.IsChecked = settings.ShowAnnouncements;
        ConditionsCheckBox.IsChecked = settings.ShowConditions;
        VolumeMixerCheckBox.IsChecked = settings.ShowVolumeMixer;
        BirthdaysCheckBox.IsChecked = settings.ShowBirthdays;

        GradientFromBox.Text = settings.ThemeGradientFrom;
        GradientToBox.Text = settings.ThemeGradientTo;
        FontColorBox.Text = settings.ThemeFontColor;
        UpdateThemePreview();

        IslandShapeCombo.SelectedIndex = (int)settings.IslandShape;
        IslandAlignmentCombo.SelectedIndex = (int)settings.IslandAlignment;
        AllMonitorsCheckBox.IsChecked = settings.IslandOnAllMonitors;
        PopulateMonitors(settings);

        ClipboardHotkeyButton.Content = FormatHotkey(settings.ClipboardHotkey);
        PaletteHotkeyButton.Content = FormatHotkey(settings.PaletteHotkey);

        PreviewKeyDown += OnPreviewKeyDownWhileRecording;

        _loaded = true;
    }

    /// <summary>
    /// Lists the attached screens as a row apiece, ticked where the island is currently shown.
    ///
    /// A list rather than the dropdown this replaced, because the island can now be on several at
    /// once and a dropdown can only ask which one. There is no "primary monitor" row any more
    /// either: it existed to mean "follow whichever screen is primary", and it is now what an empty
    /// selection falls back to rather than something to pick.
    /// </summary>
    private void PopulateMonitors(AppSettings settings)
    {
        var attached = MonitorPlacement.Enumerate().Select(m => m.DeviceName).ToList();
        var showing = settings.EffectiveMonitors(attached);

        foreach (var monitor in MonitorPlacement.Enumerate())
        {
            MonitorList.Items.Add(new MonitorChoice
            {
                DeviceName = monitor.DeviceName,
                Label = monitor.IsPrimary ? $"{monitor.Label} — primary" : monitor.Label,
                IsSelected = showing.Contains(monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            });
        }

        UpdateMonitorListState();
    }

    /// <summary>
    /// Greys the per-monitor list out while every monitor is ticked, since the list is then
    /// describing a choice nothing is consulting.
    /// </summary>
    private void UpdateMonitorListState()
    {
        var all = AllMonitorsCheckBox.IsChecked == true;

        MonitorList.IsEnabled = !all;
        MonitorList.Opacity = all ? 0.4 : 1;
        MonitorFallbackNote.Visibility = all ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>One screen in the list, and whether the island is on it.</summary>
    private sealed class MonitorChoice
    {
        public required string DeviceName { get; init; }
        public required string Label { get; init; }
        public bool IsSelected { get; set; }
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

        var showBirthdays = BirthdaysCheckBox.IsChecked == true;
        var birthdaysChanged = settings.ShowBirthdays != showBirthdays;
        settings.ShowBirthdays = showBirthdays;


        // No event of its own: the clock is a flag the island reads out of the settings object,
        // and MediaIslandAppearanceChanged below already hands it the whole thing.
        settings.ShowClock = ClockCheckBox.IsChecked == true;

        settings.IslandShape = (IslandShape)Math.Max(0, IslandShapeCombo.SelectedIndex);
        settings.IslandAlignment = (IslandAlignment)Math.Max(0, IslandAlignmentCombo.SelectedIndex);
        settings.IslandOnAllMonitors = AllMonitorsCheckBox.IsChecked == true;

        settings.IslandMonitors = MonitorList.Items
            .OfType<MonitorChoice>()
            .Where(m => m.IsSelected)
            .Select(m => m.DeviceName)
            .ToList();

        // Still written, so that a downgrade to a build that only knows about one monitor opens on
        // one of the screens the island was actually on rather than defaulting back to the primary.
        settings.IslandMonitor = settings.IslandMonitors.Count > 0 ? settings.IslandMonitors[0] : "";

        UpdateMonitorListState();

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

        if (birthdaysChanged)
            BirthdaysToggled?.Invoke(showBirthdays);


        MediaIslandAppearanceChanged?.Invoke(settings);
    }

    private void OnEditBirthdaysClick(object sender, RoutedEventArgs e) =>
        EditBirthdaysRequested?.Invoke();

    /// <summary>
    /// Saves and applies the colours on every keystroke.
    ///
    /// Half-typed text is not an error here and is not reported as one. "#1e" is a colour someone
    /// is part way through entering, and what <see cref="ThemeColors.Resolve"/> does with it -- fall
    /// back to the default -- is exactly right for a preview: the box shows the default until the
    /// sixth digit lands, and then it shows the colour. Nothing flashes red at anybody mid-word.
    /// </summary>
    private void OnThemeTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateThemePreview();

        if (!_loaded)
            return;

        var settings = _settingsStore.Load();
        settings.ThemeGradientFrom = GradientFromBox.Text.Trim();
        settings.ThemeGradientTo = GradientToBox.Text.Trim();
        settings.ThemeFontColor = FontColorBox.Text.Trim();
        _settingsStore.Save(settings);

        ThemeChanged?.Invoke(settings);
    }

    /// <summary>
    /// Empties all three boxes, which is what "default" means here -- see
    /// <see cref="AppSettings.ThemeGradientFrom"/>. Writing the default hex codes in instead would
    /// look identical and behave differently: the boxes would then be pinning the current defaults
    /// rather than following them.
    /// </summary>
    private void OnResetThemeClick(object sender, RoutedEventArgs e)
    {
        GradientFromBox.Text = "";
        GradientToBox.Text = "";
        FontColorBox.Text = "";
    }

    /// <summary>
    /// Redraws the sample: the gradient behind, the three steps of the text ramp on it, and a
    /// swatch beside each box.
    ///
    /// Built here rather than by binding to <c>Theme</c>'s own brushes, because the preview has to
    /// show what the colours in the boxes <em>would</em> do -- including while they are being typed,
    /// and including when the boxes have not been saved. A preview reading the applied theme would
    /// only ever tell you what you already changed.
    /// </summary>
    private void UpdateThemePreview()
    {
        var from = ThemeColors.Resolve(GradientFromBox.Text, ThemeColors.DefaultSurface);
        var to = ThemeColors.Resolve(GradientToBox.Text, ThemeColors.DefaultSurface);
        var text = ThemeColors.Resolve(FontColorBox.Text, ThemeColors.DefaultText);

        // Opaque here, unlike the island itself: the preview sits on a solid settings window, and
        // showing the surface at its real 95% would blend it with a grey that is not part of it.
        ThemePreview.Background = new LinearGradientBrush(
            Solid(from), Solid(to), new Point(0, 0), new Point(1, 1));

        PreviewTitle.Foreground = new SolidColorBrush(Solid(text));
        PreviewBody.Foreground = new SolidColorBrush(Faded(text, ThemeColors.SecondaryAlpha));
        PreviewMeta.Foreground = new SolidColorBrush(Faded(text, ThemeColors.TertiaryAlpha));

        GradientFromSwatch.Background = new SolidColorBrush(Solid(from));
        GradientToSwatch.Background = new SolidColorBrush(Solid(to));
        FontColorSwatch.Background = new SolidColorBrush(Solid(text));
    }

    private static Color Solid(uint rgb) => Faded(rgb, 0xFF);

    private static Color Faded(uint rgb, byte alpha) =>
        Color.FromArgb(alpha, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

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
