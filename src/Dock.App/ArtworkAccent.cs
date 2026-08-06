using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dock.App;

/// <summary>
/// Picks the two colours that read as "this album" out of a piece of artwork.
///
/// A plain average is the wrong answer here: averaging a cover mixes its accents back into the
/// grey-brown its background already tends towards, and every record ends up the same colour. So
/// the pixels are bucketed by hue instead and the heaviest buckets win, with each pixel weighted
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
    /// How far apart in hue the second colour has to sit from the first, in buckets. Two shades of
    /// the same blue make a gradient nobody can see; this forces the pair at least 45 degrees apart.
    /// </summary>
    private const int MinBucketSeparation = 3;

    /// <summary>Hue shift used to invent a partner when the cover only really has one colour.</summary>
    private const double InventedPartnerShift = 42;

    /// <summary>
    /// What the bars have always been, and what a cover with no usable colour in it still gets.
    /// </summary>
    public static readonly Color Fallback = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);

    /// <summary>The cool end of the fallback gradient, so even a colourless cover has one.</summary>
    public static readonly Color FallbackSecondary = Color.FromArgb(0xDD, 0xB4, 0xC4, 0xE4);

    /// <summary>
    /// The two accents for the given PNG bytes, most prominent first, falling back to a plain
    /// white-to-cool-grey pair when the artwork is missing, undecodable, or has no colour in it.
    /// </summary>
    public static (Color Primary, Color Secondary) PairFromPng(byte[]? png)
    {
        if (png is not { Length: > 0 })
            return (Fallback, FallbackSecondary);

        var pixels = TryDecode(png);
        if (pixels is null)
            return (Fallback, FallbackSecondary);

        return Dominant(pixels) ?? (Fallback, FallbackSecondary);
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
    /// Votes every pixel into a hue bucket, then averages each winning bucket's own pixels rather
    /// than returning the bucket's nominal hue -- the average keeps the shade the cover actually
    /// used, where the bucket alone would flatten a dozen sleeves onto the same dozen hues.
    ///
    /// The runner-up has to be a genuinely different hue to count, and a cover that has only one
    /// colour gets a partner invented from it rather than a gradient between two identical stops.
    /// </summary>
    private static (Color Primary, Color Secondary)? Dominant(byte[] pixels)
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

        var ranked = Enumerable.Range(0, HueBuckets)
            .Where(i => sums[i].Weight > 0)
            .OrderByDescending(i => weights[i])
            .ToList();

        if (ranked.Count == 0)
            return null;

        var primary = Average(sums[ranked[0]]);

        var partner = ranked.Skip(1).FirstOrDefault(i => Separation(i, ranked[0]) >= MinBucketSeparation, -1);
        var secondary = partner >= 0 ? Average(sums[partner]) : Shifted(primary, InventedPartnerShift);

        return (primary, secondary);
    }

    /// <summary>Circular distance between two hue buckets, in buckets.</summary>
    private static int Separation(int a, int b)
    {
        var distance = Math.Abs(a - b);
        return Math.Min(distance, HueBuckets - distance);
    }

    private static Color Average((double R, double G, double B, double Weight) bucket) =>
        Legible(bucket.R / bucket.Weight / 255.0, bucket.G / bucket.Weight / 255.0,
            bucket.B / bucket.Weight / 255.0);

    /// <summary>The same colour rotated around the wheel, for covers with nothing to pair with.</summary>
    private static Color Shifted(Color colour, double degrees)
    {
        var (hue, saturation, value) = ToHsv(colour.R / 255.0, colour.G / 255.0, colour.B / 255.0);
        var (r, g, b) = FromHsv((hue + degrees) % 360, saturation, value);

        return Color.FromArgb(colour.A, (byte)Math.Round(r * 255), (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
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
