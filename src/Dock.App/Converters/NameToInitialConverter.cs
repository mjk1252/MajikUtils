using System.Globalization;
using System.Windows.Data;

namespace Dock.App.Converters;

public sealed class NameToInitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        return string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
