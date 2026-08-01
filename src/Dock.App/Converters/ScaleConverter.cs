using System.Globalization;
using System.Windows.Data;

namespace Dock.App.Converters;

/// <summary>
/// Scales a base dimension (given as ConverterParameter, measured at the default 52px icon
/// size) proportionally to the current icon size, so dock chrome elements other than the icons
/// themselves (quick tools grid, overflow chevron) grow/shrink along with the drag handle too.
/// </summary>
public sealed class ScaleConverter : IValueConverter
{
    private const double BaseIconSize = 52.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double iconSize ||
            parameter is not string paramText ||
            !double.TryParse(paramText, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseValue))
        {
            return 0.0;
        }

        return iconSize / BaseIconSize * baseValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
