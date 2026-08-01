using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Interop.Shell;
using Dock.Interop.Windowing;

namespace Dock.App.Views;

public partial class SettingsWindow : Window
{
    private static readonly string[] PresetColors =
    {
        "#1E1E1E", "#2D3142", "#264653", "#3A2E5C", "#4A2C2A", "#1B4332"
    };

    private readonly SettingsStore _settingsStore;
    private bool _loaded;
    private string _accentColor = "#1E1E1E";

    public SettingsWindow(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();

        var settings = _settingsStore.Load();
        HideTaskbarCheckBox.IsChecked = settings.HideTaskbar;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        _accentColor = settings.AccentColor;
        OpacitySlider.Value = settings.TintOpacity;
        DockPaddingSlider.Value = settings.DockPadding;
        IconSpacingSlider.Value = settings.IconSpacing;
        DockMarginSlider.Value = settings.DockMargin;
        AppClearanceSlider.Value = settings.AppClearance;
        UpdateSpacingLabels();

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

        CustomColorTextBox.Text = _accentColor;
        BuildSwatches();
        _loaded = true;
    }

    private void BuildSwatches()
    {
        SwatchPanel.Children.Clear();

        foreach (var hex in PresetColors)
            SwatchPanel.Children.Add(CreateSwatch(hex));
    }

    private Border CreateSwatch(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var border = new Border
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(color),
            BorderBrush = string.Equals(hex, _accentColor, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = hex
        };
        border.MouseLeftButtonUp += (_, _) => SelectAccentColor(hex);
        return border;
    }

    private void OnCustomColorTextChanged(object sender, RoutedEventArgs e) => TryApplyCustomColor();

    private void OnCustomColorKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            TryApplyCustomColor();
    }

    private void TryApplyCustomColor()
    {
        var text = CustomColorTextBox.Text.Trim();
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(text)!;
        }
        catch
        {
            return;
        }

        SelectAccentColor(text, updateTextBox: false);
    }

    private void SelectAccentColor(string hex, bool updateTextBox = true)
    {
        _accentColor = hex;
        BuildSwatches();
        if (updateTextBox)
            CustomColorTextBox.Text = hex;
        OnSettingChanged(this, new RoutedEventArgs());
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel is not null)
            OpacityLabel.Text = $"{(int)e.NewValue}%";

        OnSettingChanged(sender, new RoutedEventArgs());
    }

    private void OnSpacingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSpacingLabels();
        OnSettingChanged(sender, new RoutedEventArgs());
    }

    private void UpdateSpacingLabels()
    {
        // Called from the constructor too, where the second slider may not be built yet.
        if (DockPaddingLabel is not null)
            DockPaddingLabel.Text = $"{(int)DockPaddingSlider.Value}";
        if (IconSpacingLabel is not null)
            IconSpacingLabel.Text = $"{(int)IconSpacingSlider.Value}";
        if (DockMarginLabel is not null)
            DockMarginLabel.Text = $"{(int)DockMarginSlider.Value}";
        if (AppClearanceLabel is not null)
            AppClearanceLabel.Text = $"{(int)AppClearanceSlider.Value}";
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
            Position = position,
            AccentColor = _accentColor,
            TintOpacity = (int)OpacitySlider.Value,
            DockPadding = DockPaddingSlider.Value,
            IconSpacing = IconSpacingSlider.Value,
            DockMargin = DockMarginSlider.Value,
            AppClearance = AppClearanceSlider.Value,

            // Carried across explicitly: this builds a fresh AppSettings rather than mutating the
            // loaded one, so anything the settings window does not surface has to be copied or it
            // is silently reset on the next save. Icon sizes are set by dragging the dock itself.
            IconSize = previousSettings.IconSize,
            IconSizeByMonitor = previousSettings.IconSizeByMonitor
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

        var appearanceChanged = settings.AccentColor != previousSettings.AccentColor
            || settings.TintOpacity != previousSettings.TintOpacity;

        if (System.Windows.Application.Current is not App app)
            return;

        if (settings.Position != previousSettings.Position || appearanceChanged)
        {
            app.RebuildDockWindows(settings.Position, settings.AccentColor, settings.TintOpacity);
            return;
        }

        // Spacing is a dependency property on the live windows, so it updates in place -- a rebuild
        // here would make the dock flicker on every tick of a slider drag.
        if (settings.DockPadding != previousSettings.DockPadding
            || settings.IconSpacing != previousSettings.IconSpacing
            || settings.DockMargin != previousSettings.DockMargin
            || settings.AppClearance != previousSettings.AppClearance)
        {
            app.ApplyDockSpacing(settings.DockPadding, settings.IconSpacing, settings.DockMargin, settings.AppClearance);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
