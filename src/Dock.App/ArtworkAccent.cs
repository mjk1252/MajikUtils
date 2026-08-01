using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dock.App;

/// <summary>
/// Picks the one colour that reads as "this album" out of a piece of artwork.
///
/// A plain average is the wrong answer here: averaging a cover mixes its accents back into the
/// grey-brown its background already tends towards, and every record ends up the same colour. So
/// the pixels are bucketed by hue instead and the heaviest bucket wins, with each pixel weighted
/// by how colourful it is -- a mostly-black sleeve with one red stripe should come back red.
/// </summary>
internal static class ArtworkAccent
{
    /// <summary>
    /// Artwork is only sampled, never shown, so it is decoded far smaller than it arrives. Covers
    /// come in at up to 1000px square and the dominant hue of one does not change between that and
    /// a thumbnail -- this is ~1k pixels to walk instead of a million.
    /// </summary>
    private const int SampleSize = 32;

    /// <summary>Hue buckets. Coarse enough that a gradient stays one colour, fine enough to keep red off orange.</summary>
    private const int HueBuckets = 24;

    /// <summary>
    /// Below this saturation a pixel is treated as ink or paper rather than colour: it carries no
    /// hue worth voting with, and letting greys vote hands every monochrome cover a random tint.
    /// </summary>
    private const double MinSaturation = 0.18;

    /// <summary>Near-black and blown-out pixels have unstable hues, so they are left out of the vote.</summary>
    private const double MinValue = 0.12;
    private const double MaxValue = 0.97;

    /// <summary>
    /// What the bars have always been, and what a cover with no usable colour in it still gets.
    /// </summary>
    public static readonly Color Fallback = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);

    /// <summary>
    /// The accent for the given PNG bytes, or <see cref="Fallback"/> when the artwork is missing,
    /// undecodable, or has no colour to speak of.
    /// </summary>
    public static Color FromPng(byte[]? png)
    {
        if (png is not { Length: > 0 })
            return Fallback;

        var pixels = TryDecode(png);
        if (pixels is null)
            return Fallback;

        return Dominant(pixels) ?? Fallback;
    }

    /// <summary>
    /// Decodes to a small Bgra32 buffer. Anything the imaging stack refuses -- a truncated stream, a
    /// format it has no codec for -- is a missing accent, not a crash: the island is a passenger to
    /// whatever bytes the media session happened to hand over.
    /// </summary>
    private static byte[]? TryDecode(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png);
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.DecodePixelWidth = SampleSize;
            source.DecodePixelHeight = SampleSize;
            source.StreamSource = stream;
            source.EndInit();

            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var buffer = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(buffer, stride, 0);
            return buffer;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Votes every pixel into a hue bucket, then averages the winning bucket's own pixels rather
    /// than returning the bucket's nominal hue -- the average keeps the shade the cover actually
    /// used, where the bucket alone would flatten a dozen sleeves onto the same dozen hues.
    /// </summary>
    private static Color? Dominant(byte[] pixels)
    {
        var weights = new double[HueBuckets];
        var sums = new (double R, double G, double B, double Weight)[HueBuckets];

        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            var alpha = pixels[i + 3] / 255.0;
            if (alpha < 0.5)
                continue;

            var (hue, saturation, value) = ToHsv(r / 255.0, g / 255.0, b / 255.0);
            if (saturation < MinSaturation || value < MinValue || value > MaxValue)
                continue;

            // Weighted by saturation so a wash of nearly-grey never outvotes a smaller, louder area.
            var weight = saturation * saturation * alpha;
            var bucket = (int)(hue / 360.0 * HueBuckets) % HueBuckets;

            weights[bucket] += weight;
            sums[bucket] = (sums[bucket].R + r * weight, sums[bucket].G + g * weight,
                sums[bucket].B + b * weight, sums[bucket].Weight + weight);
        }

        var winner = -1;
        for (var i = 0; i < HueBuckets; i++)
        {
            if (winner < 0 || weights[i] > weights[winner])
                winner = i;
        }

        if (winner < 0 || sums[winner].Weight <= 0)
            return null;

        var (sr, sg, sb, total) = sums[winner];
        return Legible(sr / total / 255.0, sg / total / 255.0, sb / total / 255.0);
    }

    /// <summary>
    /// Pulls the sampled colour into the range that still reads as four 2.5px bars on a near-black
    /// pill. Covers are printed on white as often as not, and a colour taken straight off one is
    /// regularly too dark or too muted to survive at that size.
    /// </summary>
    private static Color Legible(double r, double g, double b)
    {
        var (hue, saturation, value) = ToHsv(r, g, b);
        saturation = Math.Clamp(saturation * 1.25, 0.55, 0.95);
        value = Math.Clamp(value * 1.2, 0.82, 1.0);

        var (br, bg, bb) = FromHsv(hue, saturation, value);
        return Color.FromArgb(0xDD, (byte)Math.Round(br * 255), (byte)Math.Round(bg * 255),
            (byte)Math.Round(bb * 255));
    }

    private static (double Hue, double Saturation, double Value) ToHsv(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var hue = 0.0;
        if (delta > 0)
        {
            hue = max == r ? 60 * (((g - b) / delta) % 6)
                : max == g ? 60 * (((b - r) / delta) + 2)
                : 60 * (((r - g) / delta) + 4);
            if (hue < 0)
                hue += 360;
        }

        return (hue, max <= 0 ? 0 : delta / max, max);
    }

    private static (double R, double G, double B) FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var section = hue / 60;
        var second = chroma * (1 - Math.Abs((section % 2) - 1));
        var match = value - chroma;

        var (r, g, b) = (int)section switch
        {
            0 => (chroma, second, 0.0),
            1 => (second, chroma, 0.0),
            2 => (0.0, chroma, second),
            3 => (0.0, second, chroma),
            4 => (second, 0.0, chroma),
            _ => (chroma, 0.0, second)
        };

        return (r + match, g + match, b + match);
    }
}
