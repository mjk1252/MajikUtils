namespace Dock.Core.Models;

/// <summary>
/// The three colours the user gets to choose, and the parsing that turns what they typed into
/// something drawable.
///
/// Here rather than in Dock.App because it is all decisions and no drawing: which text is a colour,
/// what an empty box falls back to, and how a chosen colour is stepped down into the ramp the
/// island's text has always used. The WPF end of it is three brush assignments once this has
/// answered.
///
/// Colours are held as packed <c>0xRRGGBB</c> rather than as strings, so everything downstream is
/// spared re-parsing, and as plain <c>uint</c> rather than a colour type, because Dock.Core has no
/// business knowing what a Color is.
/// </summary>
public static class ThemeColors
{
    /// <summary>The island's near-black, and what an unset gradient falls back to at both ends.</summary>
    public const uint DefaultSurface = 0x101010;

    /// <summary>White, and what an unset font colour falls back to.</summary>
    public const uint DefaultText = 0xFFFFFF;

    /// <summary>
    /// How opaque the island's surface is, whatever colour it has been given.
    ///
    /// Not the user's to choose, and that is deliberate: the island sits over other windows and
    /// reads as glass at this value. A colour picker that also let the surface go fully transparent
    /// would let somebody make the island invisible and then have nothing to click to get it back.
    /// </summary>
    public const byte SurfaceAlpha = 0xF2;

    /// <summary>
    /// The two steps below the chosen font colour, as alpha values.
    ///
    /// The ramp is the one the island has always had -- full, 0x99 and 0x61 -- kept as *alpha* on
    /// the chosen colour rather than as three separately chosen colours. Three pickers would be
    /// three ways to break the hierarchy, and a secondary that is a different hue from the primary
    /// reads as an error rather than as a step down.
    /// </summary>
    public const byte SecondaryAlpha = 0x99;

    public const byte TertiaryAlpha = 0x61;

    /// <summary>
    /// Reads a <c>#RRGGBB</c> (or bare <c>RRGGBB</c>, or shorthand <c>#RGB</c>) colour.
    ///
    /// Returns false rather than throwing or substituting, because the caller is a text box being
    /// typed into: "#1e" is not an error to report, it is a colour half-entered, and the right
    /// response is to leave the last good one alone until the rest arrives.
    /// </summary>
    public static bool TryParse(string? text, out uint rgb)
    {
        rgb = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim().TrimStart('#');

        // Shorthand, expanded the way CSS does it: #f0c is #ff00cc.
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        if (value.Length != 6)
            return false;

        return uint.TryParse(value, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out rgb);
    }

    /// <summary>
    /// The colour to use, given what the user typed and what to fall back to. The one place the
    /// "blank means default" rule lives, so no caller has to remember it.
    /// </summary>
    public static uint Resolve(string? text, uint fallback) =>
        TryParse(text, out var rgb) ? rgb : fallback;

    /// <summary>Back to <c>#RRGGBB</c>, for seeding the settings boxes from a stored value.</summary>
    public static string ToHex(uint rgb) => $"#{rgb & 0xFFFFFF:X6}";

    /// <summary>
    /// Whether a colour is light enough that black text would sit on it better than white.
    ///
    /// Used for one thing: the settings preview's swatch labels, so a pale gradient does not get a
    /// white caption nobody can read. The island's own text is the user's choice and is not
    /// second-guessed -- if they pick white on white, the preview is where they find that out.
    /// </summary>
    public static bool IsLight(uint rgb)
    {
        // Rec. 709 luma. The cheap average makes green and blue equally bright, which they are not.
        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;

        return (0.2126 * r + 0.7152 * g + 0.0722 * b) > 140;
    }
}
