namespace Dock.Core.Models;

/// <summary>
/// A rectangle in physical pixels. Not <c>System.Windows.Rect</c>, which lives in WPF and so cannot
/// be referenced from here -- and the point of keeping this arithmetic in Dock.Core is that it can
/// be tested without one.
/// </summary>
public readonly record struct IslandRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public bool Contains(double x, double y) =>
        x >= Left && x <= Right && y >= Top && y <= Bottom;

    public bool Contains(IslandRect other) =>
        other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom;

    public bool IntersectsHorizontally(IslandRect other) =>
        other.Left < Right && Left < other.Right;
}

/// <summary>
/// The monitor's work area, in physical pixels, plus its scale. The one coordinate space every
/// screen agrees on -- see the DPI note in ARCHITECTURE.md.
/// </summary>
public readonly record struct IslandScreen(double Left, double Top, double Width, double Scale)
{
    public double Right => Left + Width;
}

/// <summary>
/// Where the island and its bubble sit, and which region of screen the pointer has to be in to keep
/// them there.
///
/// Extracted out of the window because it is arithmetic, not UI, and because the hit region and the
/// silhouette have to agree exactly: a pill drawn one place and kept alive by a region somewhere
/// else flickers, and no amount of looking at it tells you by how much. Everything here is a pure
/// function of the shape, the alignment and the state, so every combination can be asserted.
///
/// Distances are in DIPs until they are multiplied by <see cref="IslandScreen.Scale"/>; the results
/// are physical pixels.
/// </summary>
public static class IslandGeometry
{
    /// <summary>
    /// Widened from 260 when the badge chips arrived. They sit left of the album art and the clock
    /// sits right of the title, so the strip now has four things on it rather than two, and the
    /// track name was the one being squeezed out.
    /// </summary>
    public const double CollapsedWidth = 330;
    public const double CollapsedHeight = 34;

    /// <summary>
    /// Height of the invisible strip along the top edge that summons the pill when nothing is
    /// showing. Thin on purpose: a place to throw the pointer at, not a region to avoid.
    /// </summary>
    public const double PeekHeight = 3;

    /// <summary>
    /// Slack around whatever is showing. Without it the pill sits exactly on the boundary that
    /// decides its own state, and a pixel of pointer jitter flickers it.
    /// </summary>
    public const double HoverSlack = 8;

    /// <summary>Drop of the detached pill form below the screen edge.</summary>
    public const double PillTopGap = 8;

    /// <summary>Inset from the screen's side when the island is parked at one end of the edge.</summary>
    public const double EdgeMargin = 16;

    /// <summary>
    /// How far the notch form's flares hang off each side of the slab they frame. The notch covers
    /// this much more screen than the pill it draws, which the hit region has to account for.
    /// </summary>
    public const double FilletWidth = 14;

    /// <summary>
    /// The bubble is square and as tall as the collapsed pill, so at a radius of half its height it
    /// draws as a circle and the two forms share a baseline.
    /// </summary>
    public const double BubbleSize = CollapsedHeight;

    /// <summary>
    /// Clear air between the pill's outer edge and the bubble's, fillets included. Small: the two
    /// have to read as one island that split, not two things that happen to be near each other.
    /// </summary>
    public const double BubbleGap = 8;

    /// <summary>
    /// The bubble's own flare, in notch form. Narrower than the pill's: at 14 either side of a 34px
    /// shape the flares are most of the bubble, and it stops reading as round at all.
    /// </summary>
    public const double BubbleFillet = 8;

    /// <summary>Screen the collapsed pill covers, flares included.</summary>
    public static double CollapsedFootprint(IslandShape shape) =>
        CollapsedWidth + (shape == IslandShape.Notch ? FilletWidth * 2 : 0);

    /// <summary>The same for the bubble, whose flares are narrower.</summary>
    public static double BubbleFootprint(IslandShape shape) =>
        BubbleSize + (shape == IslandShape.Notch ? BubbleFillet * 2 : 0);

    /// <summary>
    /// How much screen the bubble adds beyond the pill's own footprint: the gap plus the bubble.
    /// The gap counts -- it is a strip the pointer crosses on the way over, and a region that
    /// excluded it would put the island away halfway there.
    /// </summary>
    public static double BubbleExtent(IslandShape shape) => BubbleGap + BubbleFootprint(shape);

    /// <summary>Whether the bubble sits to the left of the pill rather than the right.</summary>
    public static bool BubbleMirrored(IslandAlignment alignment) => alignment == IslandAlignment.Right;

    /// <summary>
    /// How far to translate the bubble from the pill's anchor, in DIPs.
    ///
    /// The anchor is not the same in every case, and that is the whole subtlety: the pill's *width*
    /// animates from collapsed to expanded, so anything measured from its centre drifts while it
    /// grows. Centred, the centre is the window's and stays put. Pinned to an end of the edge, the
    /// pinned edge is what stays put and the centre is what moves -- so the measurement flips to
    /// edge-to-edge, matching the element's own alignment.
    /// </summary>
    public static double BubbleOffset(IslandShape shape, IslandAlignment alignment) => alignment switch
    {
        IslandAlignment.Left => CollapsedFootprint(shape) + BubbleGap,
        IslandAlignment.Right => -(CollapsedFootprint(shape) + BubbleGap),
        _ => (CollapsedFootprint(shape) + BubbleFootprint(shape)) / 2 + BubbleGap
    };

    /// <summary>
    /// Where the collapsed pill is drawn, in physical pixels. Flares included, slack excluded.
    /// </summary>
    public static IslandRect CollapsedRect(IslandShape shape, IslandAlignment alignment, IslandScreen screen)
    {
        var width = CollapsedFootprint(shape) * screen.Scale;

        return new IslandRect(
            LeftFor(alignment, screen, width),
            screen.Top + TopGapFor(shape) * screen.Scale,
            width,
            CollapsedHeight * screen.Scale);
    }

    /// <summary>
    /// Where the bubble is drawn while the pill is collapsed, in physical pixels.
    ///
    /// Derived from <see cref="CollapsedRect"/> and <see cref="BubbleGap"/> rather than from the
    /// translate above, so the two are independent statements of the same intent and a test can
    /// hold them against each other.
    /// </summary>
    public static IslandRect BubbleRect(IslandShape shape, IslandAlignment alignment, IslandScreen screen)
    {
        var pill = CollapsedRect(shape, alignment, screen);
        var width = BubbleFootprint(shape) * screen.Scale;
        var gap = BubbleGap * screen.Scale;

        var left = BubbleMirrored(alignment) ? pill.Left - gap - width : pill.Right + gap;

        return new IslandRect(left, pill.Top, width, BubbleSize * screen.Scale);
    }

    /// <summary>
    /// The region the pointer has to be in to keep the island on screen, in physical pixels.
    ///
    /// It grows with the island: a thin strip while nothing is showing, the pill's own rectangle
    /// once there is, and the expanded panel's once that is open -- so reaching for a transport
    /// button never leaves the region that is keeping it open.
    /// </summary>
    public static IslandRect HitRect(
        IslandShape shape, IslandAlignment alignment, IslandScreen screen, IslandHitState state)
    {
        var (width, height, slack) = state switch
        {
            { Expanded: true } => (state.ExpandedWidth, state.ExpandedHeight, HoverSlack),
            { Shown: true } => (CollapsedWidth, CollapsedHeight, HoverSlack),
            _ => (CollapsedWidth, PeekHeight, 0d)
        };

        var footprint = (width + (shape == IslandShape.Notch ? FilletWidth * 2 : 0)) * screen.Scale;
        var scaledSlack = slack * screen.Scale;
        var left = LeftFor(alignment, screen, footprint);

        // The bubble hangs off one side only, so the combined footprint is not symmetric about the
        // pill: its extent widens the region without moving the pill's own placement. Centring the
        // combined width instead would shift the island half a bubble off centre.
        var bubble = state.BubbleShown ? BubbleExtent(shape) * screen.Scale : 0;

        // Measured from the screen edge down regardless of the gap above a detached pill: that gap
        // is a strip the pointer has to cross to reach it, and treating it as outside the region
        // would put the island away halfway there.
        var reach = height + TopGapFor(shape);

        return new IslandRect(
            left - scaledSlack - (BubbleMirrored(alignment) ? bubble : 0),
            screen.Top,
            footprint + scaledSlack * 2 + bubble,
            reach * screen.Scale + scaledSlack);
    }

    private static double TopGapFor(IslandShape shape) => shape == IslandShape.Pill ? PillTopGap : 0;

    private static double LeftFor(IslandAlignment alignment, IslandScreen screen, double width)
    {
        var margin = EdgeMargin * screen.Scale;

        return alignment switch
        {
            IslandAlignment.Left => screen.Left + margin,
            IslandAlignment.Right => screen.Right - margin - width,
            _ => screen.Left + (screen.Width - width) / 2
        };
    }
}

/// <summary>
/// What the island is doing, as far as the hit region is concerned.
/// </summary>
/// <param name="ExpandedWidth">Width of whichever section is open, which differs between the hover
/// panel and a section opened from the tab strip.</param>
/// <param name="ExpandedHeight">Measured, not assumed: a live stream has no timeline and its panel
/// comes out shorter, and every section is a different height again.</param>
public readonly record struct IslandHitState(
    bool Shown,
    bool Expanded,
    bool BubbleShown,
    double ExpandedWidth,
    double ExpandedHeight);
