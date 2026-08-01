using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Dock.App.Views;

/// <summary>
/// The media island's silhouette: a rounded slab hanging from the top of the screen whose top
/// corners curve back <em>outwards</em> to meet the edge, rather than meeting it at a right angle.
///
/// That outward flare is the whole reason this is not a Border with a CornerRadius. A rounded
/// rectangle stuck to the top of the screen reads as a window someone pushed off the top; the
/// concave fillets make the same shape read as part of the screen edge, which is what a notch is.
///
/// The pill's own size is animated, so the geometry is rebuilt whenever it changes -- cheap, since
/// it is eight segments.
/// </summary>
public sealed class NotchShape : Shape
{
    public static readonly DependencyProperty PillWidthProperty = Register(nameof(PillWidth), 260d);
    public static readonly DependencyProperty PillHeightProperty = Register(nameof(PillHeight), 34d);
    public static readonly DependencyProperty BottomRadiusProperty = Register(nameof(BottomRadius), 17d);
    public static readonly DependencyProperty FilletProperty = Register(nameof(Fillet), 14d);

    /// <summary>Width of the slab itself, excluding the two fillets flanking it.</summary>
    public double PillWidth
    {
        get => (double)GetValue(PillWidthProperty);
        set => SetValue(PillWidthProperty, value);
    }

    public double PillHeight
    {
        get => (double)GetValue(PillHeightProperty);
        set => SetValue(PillHeightProperty, value);
    }

    /// <summary>Radius of the two bottom corners, the only ones that curve inwards.</summary>
    public double BottomRadius
    {
        get => (double)GetValue(BottomRadiusProperty);
        set => SetValue(BottomRadiusProperty, value);
    }

    /// <summary>How far the top corners flare out to blend into the screen edge.</summary>
    public double Fillet
    {
        get => (double)GetValue(FilletProperty);
        set => SetValue(FilletProperty, value);
    }

    protected override Geometry DefiningGeometry => Build();

    private Geometry Build()
    {
        var width = Math.Max(0, PillWidth);
        var height = Math.Max(0, PillHeight);
        var fillet = Math.Max(0, Fillet);

        // A radius larger than the box it rounds produces a self-crossing outline rather than a
        // fuller curve, which shows up the instant the pill is mid-animation.
        var radius = Math.Max(0, Math.Min(BottomRadius, Math.Min(width / 2, height)));

        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),

            // Left open on purpose: the fill closes the shape along the top by itself, while the
            // stroke does not, so no hairline is drawn along the screen edge where nothing but the
            // top half of it would be visible anyway.
            IsClosed = false,
            IsFilled = true
        };

        var flare = new Size(fillet, fillet);
        var corner = new Size(radius, radius);

        // Down the left flare, which curves away from the pill and into the screen edge.
        figure.Segments.Add(Arc(fillet, fillet, flare, SweepDirection.Clockwise));

        figure.Segments.Add(Line(fillet, height - radius));
        figure.Segments.Add(Arc(fillet + radius, height, corner, SweepDirection.Counterclockwise));

        figure.Segments.Add(Line(fillet + width - radius, height));
        figure.Segments.Add(Arc(fillet + width, height - radius, corner, SweepDirection.Counterclockwise));

        figure.Segments.Add(Line(fillet + width, fillet));
        figure.Segments.Add(Arc(fillet + width + fillet, 0, flare, SweepDirection.Clockwise));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static LineSegment Line(double x, double y) => new(new Point(x, y), isStroked: true);

    private static ArcSegment Arc(double x, double y, Size size, SweepDirection direction) =>
        new(new Point(x, y), size, rotationAngle: 0, isLargeArc: false, direction, isStroked: true);

    private static DependencyProperty Register(string name, double defaultValue) =>
        DependencyProperty.Register(name, typeof(double), typeof(NotchShape),
            new FrameworkPropertyMetadata(defaultValue,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
}
