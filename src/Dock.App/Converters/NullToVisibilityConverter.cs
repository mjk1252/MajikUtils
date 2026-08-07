using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Dock.App.Converters;

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is null;

        // Same "invert" flag the bool converter takes, for the commoner case of showing something
        // only once a value has arrived.
        if (parameter is string flag && flag.Equals("invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
