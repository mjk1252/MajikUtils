using System.Windows;
using System.Windows.Media;
using Dock.Core.Models;

namespace Dock.App;

/// <summary>
/// Paints the application from the two gradient colours and the font colour in settings.
///
/// Four brushes, written into the application's resources: the island's surface, and the three
/// steps of the text ramp. Everything else in the app already reads from those keys, which is why
/// this is a small class -- the work was done when the ramp was cut down to three levels and given
/// names, not here.
///
/// The same rule as the artwork accent applies and for the same reason: a ResourceDictionary seals
/// every Freezable put into it, so a themed brush can never be recoloured in place. Each one is
/// *replaced*, and every reference to these keys in the XAML is a DynamicResource -- a
/// StaticResource would resolve once at load and keep whatever it started with for the rest of the
/// session. That is the whole reason applying a theme live works at all.
/// </summary>
internal static class Theme
{
    /// <summary>
    /// The gradient's direction: top-left to bottom-right.
    ///
    /// Diagonal rather than horizontal because the surface it paints is two very different shapes
    /// -- a 260x34 pill and a 440-wide panel several hundred tall -- and a horizontal gradient that
    /// reads well across the pill is a flat wash of the left-hand colour down the open panel.
    /// A diagonal travels across both.
    /// </summary>
    private static readonly Point GradientStart = new(0, 0);
    private static readonly Point GradientEnd = new(1, 1);

    /// <summary>
    /// Applies the settings, or the defaults for anything left blank.
    ///
    /// Called at startup before the first island is built, and again whenever the colours change in
    /// Settings. Idempotent, and cheap enough that there is no reason to check whether anything
    /// actually moved before running it.
    /// </summary>
    public static void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (System.Windows.Application.Current is not { } app)
            return;

        var from = ThemeColors.Resolve(settings.ThemeGradientFrom, ThemeColors.DefaultSurface);
        var to = ThemeColors.Resolve(settings.ThemeGradientTo, ThemeColors.DefaultSurface);
        var text = ThemeColors.Resolve(settings.ThemeFontColor, ThemeColors.DefaultText);

        app.Resources["IslandSurfaceBrush"] = SurfaceBrush(from, to);

        app.Resources["TextPrimaryBrush"] = new SolidColorBrush(Opaque(text));
        app.Resources["TextSecondaryBrush"] = new SolidColorBrush(Fade(text, ThemeColors.SecondaryAlpha));
        app.Resources["TextTertiaryBrush"] = new SolidColorBrush(Fade(text, ThemeColors.TertiaryAlpha));
    }

    /// <summary>
    /// The island's background: the two colours at the island's own fixed opacity.
    ///
    /// Always a gradient, even when the two stops are identical -- which is what the defaults are,
    /// so an untouched install gets exactly the flat near-black it had before any of this existed.
    /// A branch that returned a SolidColorBrush for that case would be two code paths for one
    /// appearance, and the renderer does not care.
    /// </summary>
    private static Brush SurfaceBrush(uint from, uint to) =>
        new LinearGradientBrush(
            Translucent(from, ThemeColors.SurfaceAlpha),
            Translucent(to, ThemeColors.SurfaceAlpha),
            GradientStart,
            GradientEnd);

    private static Color Opaque(uint rgb) => Translucent(rgb, 0xFF);

    /// <summary>The same colour at a step down the ramp -- alpha, not a darker shade.</summary>
    private static Color Fade(uint rgb, byte alpha) => Translucent(rgb, alpha);

    private static Color Translucent(uint rgb, byte alpha) =>
        Color.FromArgb(alpha, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
}
