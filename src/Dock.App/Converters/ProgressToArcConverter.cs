using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Dock.App.Converters;

/// <summary>
/// Turns a 0..1 progress into the arc that draws it, for the ring in the bubble.
///
/// A ring rather than a bar because the bubble is round and 34px across: a bar in there would be a
/// dozen pixels long and unreadable, where a ring uses the whole silhouette it is sitting in.
/// </summary>
public sealed class ProgressToArcConverter : IValueConverter
{
    private const double Radius = 11;
    private const double Centre = 13;

    /// <summary>
    /// A single arc segment cannot express a full turn -- start and end coincide and it collapses
    /// to nothing -- so a completed ring stops a fraction short. At this radius the gap is well
    /// under a pixel.
    /// </summary>
    private const double MaxSweep = 359.9;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var progress = value is double d ? Math.Clamp(d, 0, 1) : 0;
        var sweep = progress * MaxSweep;

        if (sweep <= 0)
            return Geometry.Empty;

        // Twelve o'clock, so a timer empties the way a clock face fills.
        var start = PointOn(-90);
        var end = PointOn(-90 + sweep);

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            end, new System.Windows.Size(Radius, Radius), rotationAngle: 0,
            isLargeArc: sweep > 180, SweepDirection.Clockwise, isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static System.Windows.Point PointOn(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new System.Windows.Point(
            Centre + Radius * Math.Cos(radians),
            Centre + Radius * Math.Sin(radians));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
