using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dock.App;

/// <summary>
/// Draws the two taskbar-button icons at runtime.
///
/// Nothing ships as a binary asset: the Launch button is a Segoe MDL2 glyph on a rounded plate,
/// and the Drawer button is a live CPU/GPU gauge that has to be redrawn every second anyway.
/// A pinned button is the one case that needs a real file on disk -- the shell reads
/// PKEY_AppUserModel_RelaunchIconResource while the app is *not* running, so it cannot ask us --
/// which is what <see cref="EnsureIcoOnDisk"/> is for.
/// </summary>
public static class PanelIcons
{
    private const int IconSize = 64;

    private static readonly Typeface GlyphTypeface = new("Segoe MDL2 Assets");
    private static readonly Brush PlateBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x26)));
    private static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush CpuBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)));
    private static readonly Brush GpuBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7)));

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public static BitmapSource RenderGlyph(string glyph)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawPlate(dc);

            var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GlyphTypeface, 34, Brushes.White, 1.0);

            dc.DrawText(text, new Point((IconSize - text.Width) / 2, (IconSize - text.Height) / 2));
        }

        return Rasterize(visual);
    }

    /// <summary>
    /// Two concentric arcs sweeping clockwise from 12 o'clock: outer ring is CPU, inner is GPU.
    /// Both percentages are 0-100.
    /// </summary>
    public static BitmapSource RenderStatsGauge(double cpuPercent, double gpuPercent)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawPlate(dc);
            DrawRing(dc, radius: 23, thickness: 7, fraction: cpuPercent / 100.0, brush: CpuBrush);
            DrawRing(dc, radius: 13, thickness: 6, fraction: gpuPercent / 100.0, brush: GpuBrush);
        }

        return Rasterize(visual);
    }

    private static void DrawPlate(DrawingContext dc) =>
        dc.DrawRoundedRectangle(PlateBrush, null, new Rect(0, 0, IconSize, IconSize), 12, 12);

    private static void DrawRing(DrawingContext dc, double radius, double thickness, double fraction, Brush brush)
    {
        var centre = new Point(IconSize / 2.0, IconSize / 2.0);
        dc.DrawEllipse(null, new Pen(TrackBrush, thickness), centre, radius, radius);

        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction <= 0)
            return;

        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        // A full sweep can't be expressed as a single arc segment (start and end points coincide,
        // which ArcSegment renders as nothing at all), so cap it just short of a closed circle.
        var sweep = Math.Min(fraction, 0.999) * 360.0;
        var start = new Point(centre.X, centre.Y - radius);
        var endAngle = (sweep - 90) * Math.PI / 180.0;
        var end = new Point(centre.X + radius * Math.Cos(endAngle), centre.Y + radius * Math.Sin(endAngle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0,
            isLargeArc: sweep > 180, SweepDirection.Clockwise, isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }

    private static BitmapSource Rasterize(DrawingVisual visual)
    {
        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Writes <paramref name="image"/> to %LOCALAPPDATA%\Dock\icons\{name}.ico and returns the
    /// "path,index" string the shell expects, or null if it could not be written. The .ico wraps
    /// the PNG bytes verbatim -- Vista and later read PNG-compressed icon entries directly, so no
    /// BMP/DIB conversion is needed.
    /// </summary>
    public static string? EnsureIcoOnDisk(string name, BitmapSource image)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dock", "icons");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name + ".ico");

            using var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(png);
            var pngBytes = png.ToArray();

            using var file = File.Create(path);
            using var writer = new BinaryWriter(file);

            writer.Write((ushort)0);        // reserved
            writer.Write((ushort)1);        // type: icon
            writer.Write((ushort)1);        // image count
            writer.Write((byte)IconSize);   // width
            writer.Write((byte)IconSize);   // height
            writer.Write((byte)0);          // palette size (0 = no palette)
            writer.Write((byte)0);          // reserved
            writer.Write((ushort)1);        // colour planes
            writer.Write((ushort)32);       // bits per pixel
            writer.Write(pngBytes.Length);
            writer.Write(22);               // payload offset: 6-byte dir + 16-byte entry
            writer.Write(pngBytes);

            return path + ",0";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only costs the pinned button its custom artwork; it still pins and still launches.
            return null;
        }
    }
}
