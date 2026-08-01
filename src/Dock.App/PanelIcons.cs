using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dock.Core.Services;

namespace Dock.App;

/// <summary>
/// Supplies the artwork for MajikUtils' taskbar buttons: a user-supplied image where there is one,
/// otherwise a drawn badge.
///
/// Drawing them rather than shipping bitmaps keeps the repo free of binary assets and lets every
/// badge share one look. A pinned button is the one case that needs a real file on disk -- the
/// shell reads PKEY_AppUserModel_RelaunchIconResource while the app is *not* running, so it cannot
/// ask us -- which is what <see cref="EnsureIcoOnDisk"/> is for.
/// </summary>
public static class PanelIcons
{
    /// <summary>
    /// Drawn at 256 rather than at a taskbar's actual 16-32px: Windows scales down from whatever it
    /// is given, and downscaling is the direction that stays sharp. It is also the size the
    /// generated .ico wants for a pinned button on a high-DPI display.
    /// </summary>
    private const int IconSize = 256;

    private static readonly Typeface GlyphTypeface = new("Segoe MDL2 Assets");

    // Accents, one per button, so the badges are told apart by colour at a glance as well as by
    // their glyph -- at 16px on a taskbar the glyph alone is close to unreadable.
    public static readonly Color DrawerAccent = Color.FromRgb(0x4F, 0xC3, 0xF7);
    public static readonly Color ShelfAccent = Color.FromRgb(0xFF, 0xB7, 0x4D);

    /// <summary>
    /// A badge: rounded slate plate, a lit rim and top sheen, the glyph, and an accent bar along
    /// the bottom. The bar is what survives being scaled to taskbar size -- it stays a legible
    /// block of colour long after the glyph has turned to mush.
    /// </summary>
    public static BitmapSource RenderGlyph(string glyph, Color accent)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            const double radius = 56;
            var bounds = new Rect(0, 0, IconSize, IconSize);

            var plate = new LinearGradientBrush(
                Color.FromRgb(0x2E, 0x33, 0x42), Color.FromRgb(0x14, 0x16, 0x1C), 90);
            plate.Freeze();
            dc.DrawRoundedRectangle(plate, null, bounds, radius, radius);

            // A faint wash of the accent over the plate, so the badge reads as tinted rather than
            // as a grey box with a coloured stripe bolted on.
            var wash = new LinearGradientBrush(
                Color.FromArgb(0x2E, accent.R, accent.G, accent.B), Color.FromArgb(0x00, accent.R, accent.G, accent.B), 90);
            wash.Freeze();
            dc.DrawRoundedRectangle(wash, null, bounds, radius, radius);

            // Rim and sheen: a light edge along the top half only, which is what reads as a raised
            // surface rather than a flat fill.
            var rim = new LinearGradientBrush(
                Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 90);
            rim.Freeze();
            dc.DrawRoundedRectangle(null, new Pen(rim, 3), Deflate(bounds, 1.5), radius - 1.5, radius - 1.5);

            DrawGlyph(dc, glyph);

            var bar = new SolidColorBrush(accent);
            bar.Freeze();
            const double barWidth = 104;
            const double barHeight = 12;
            dc.DrawRoundedRectangle(bar, null,
                new Rect((IconSize - barWidth) / 2, IconSize - 40, barWidth, barHeight),
                barHeight / 2, barHeight / 2);
        }

        return Rasterize(visual);
    }

    private static void DrawGlyph(DrawingContext dc, string glyph)
    {
        var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, GlyphTypeface, 120, Brushes.White, 1.0);

        // Sits above centre to leave the accent bar its own space; without the offset the glyph and
        // the bar crowd each other and the badge looks bottom-heavy.
        var origin = new Point((IconSize - text.Width) / 2, (IconSize - text.Height) / 2 - 14);

        // Drawn once in black underneath as a cheap shadow, which keeps the glyph legible if a
        // custom accent ever lands close to white.
        dc.PushOpacity(0.35);
        dc.DrawText(
            new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GlyphTypeface, 120, Brushes.Black, 1.0),
            new Point(origin.X, origin.Y + 3));
        dc.Pop();

        dc.DrawText(text, origin);
    }

    private static Rect Deflate(Rect rect, double amount) =>
        new(rect.X + amount, rect.Y + amount, rect.Width - amount * 2, rect.Height - amount * 2);

    /// <summary>
    /// A custom icon supplied for <paramref name="name"/>, or null to fall back to drawn artwork.
    ///
    /// Two locations, checked in order. The first is a drop-in folder in the user's own data
    /// directory, so an icon can be swapped on a shipped build with nothing but a file copy; the
    /// second ships alongside the exe and is what a build provides by default.
    /// </summary>
    public static BitmapSource? LoadCustom(string name)
    {
        string[] roots =
        [
            AppPaths.CustomIconsDirectory,
            Path.Combine(AppContext.BaseDirectory, "assets", "icons")
        ];

        foreach (var root in roots)
        {
            foreach (var extension in new[] { ".png", ".ico" })
            {
                var path = Path.Combine(root, name + extension);
                if (File.Exists(path) && Decode(path) is { } image)
                    return image;
            }
        }

        return null;
    }

    private static BitmapSource? Decode(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            // An .ico holds several sizes; take the largest, since the taskbar and the generated
            // pinned icon both want more pixels than the 16px frame that tends to come first.
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException
                                      or ArgumentException or FileFormatException)
        {
            // A corrupt or unreadable file falls back to the drawn badge rather than failing to
            // build a window that the taskbar button depends on existing.
            return null;
        }
    }

    /// <summary>
    /// Wraps shell-extracted PNG bytes (as carried on the view models) as an image source, for
    /// windows whose taskbar icon should be the real folder icon rather than drawn artwork.
    /// </summary>
    public static BitmapSource? FromPng(byte[]? png)
    {
        if (png is null || png.Length == 0)
            return null;

        try
        {
            var decoder = new PngBitmapDecoder(new MemoryStream(png),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FileFormatException)
        {
            return null;
        }
    }

    private static BitmapSource Rasterize(DrawingVisual visual)
    {
        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Writes <paramref name="image"/> into the app's icons folder and returns the "path,index"
    /// string the shell expects, or null if it could not be written. The .ico wraps the PNG bytes
    /// verbatim -- Vista and later read PNG-compressed icon entries directly, so no BMP/DIB
    /// conversion is needed.
    ///
    /// The filename carries a hash of the artwork, so swapping an icon produces a *different*
    /// path. That is not cosmetic: the shell caches a taskbar button's icon against the path it
    /// was read from and does not re-read a file whose name has not changed, so a fixed filename
    /// left the old artwork on the button no matter what the app had actually loaded.
    /// </summary>
    public static string? EnsureIcoOnDisk(string name, BitmapSource image)
    {
        try
        {
            var dir = AppPaths.IconsDirectory;
            Directory.CreateDirectory(dir);

            image = ToIconCanvas(image);

            using var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(png);
            var pngBytes = png.ToArray();

            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pngBytes))[..8]
                .ToLowerInvariant();
            var path = Path.Combine(dir, $"{name}-{hash}.ico");

            PruneOlderVersions(dir, name, keep: Path.GetFileName(path));

            // Same artwork as last run: the file is already correct, and rewriting it would only
            // risk tripping over a shell that still has it open.
            if (File.Exists(path))
                return path + ",0";

            using var file = File.Create(path);
            using var writer = new BinaryWriter(file);

            // A dimension of 256 is encoded as 0; the source bitmap is not always our own artwork
            // -- shell folder icons come through here too, at whatever size the shell handed us.
            writer.Write((ushort)0);        // reserved
            writer.Write((ushort)1);        // type: icon
            writer.Write((ushort)1);        // image count
            writer.Write((byte)(image.PixelWidth >= 256 ? 0 : image.PixelWidth));
            writer.Write((byte)(image.PixelHeight >= 256 ? 0 : image.PixelHeight));
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

    /// <summary>
    /// Redraws artwork onto the full <see cref="IconSize"/> square, scaled to fit and centred.
    ///
    /// Both parts of that matter to the shell, and neither matters to WPF -- which is why a stack
    /// button could show the right icon on its *window* while the taskbar showed the generic
    /// blank-document one. The shell refuses a non-square frame outright, and reads a
    /// PNG-compressed frame reliably only at 256: below that it wants a BMP/DIB, so a 42px PNG
    /// entry loads as nothing at all.
    ///
    /// Only shell-extracted artwork is ever affected. The badges drawn above are already 256
    /// squares; a folder icon arrives at whatever size the shell had, trimmed to its opaque bounds
    /// by <c>ShellIconProvider</c> -- and a folder is wider than it is tall.
    /// </summary>
    private static BitmapSource ToIconCanvas(BitmapSource image)
    {
        if (image.PixelWidth == IconSize && image.PixelHeight == IconSize)
            return image;

        if (image.PixelWidth == 0 || image.PixelHeight == 0)
            return image;

        var scale = Math.Min((double)IconSize / image.PixelWidth, (double)IconSize / image.PixelHeight);
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

        using (var dc = visual.RenderOpen())
            dc.DrawImage(image, new Rect((IconSize - width) / 2, (IconSize - height) / 2, width, height));

        return Rasterize(visual);
    }

    /// <summary>
    /// Removes earlier hashes for the same icon, so changing artwork a few times does not leave a
    /// drift of dead files. A file the shell still has open simply stays until next time.
    /// </summary>
    private static void PruneOlderVersions(string directory, string name, string keep)
    {
        foreach (var stale in Directory.EnumerateFiles(directory, name + "-*.ico"))
        {
            if (string.Equals(Path.GetFileName(stale), keep, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(stale);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
