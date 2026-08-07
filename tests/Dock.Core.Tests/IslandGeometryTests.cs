using Dock.Core.Models;

namespace Dock.Core.Tests;

public class IslandGeometryTests
{
    /// <summary>A 2560x1440 primary at 100%, and the same offset and scaled, so nothing can pass
    /// by quietly assuming the screen starts at zero or that a DIP is a pixel.</summary>
    public static TheoryData<IslandScreen> Screens =>
    [
        new IslandScreen(0, 0, 2560, 1.0),
        new IslandScreen(-1920, 40, 1920, 1.5)
    ];

    public static TheoryData<IslandShape, IslandAlignment> Combinations
    {
        get
        {
            TheoryData<IslandShape, IslandAlignment> data = [];

            foreach (var shape in new[] { IslandShape.Notch, IslandShape.Pill })
            {
                foreach (var alignment in new[]
                    { IslandAlignment.Left, IslandAlignment.Center, IslandAlignment.Right })
                {
                    data.Add(shape, alignment);
                }
            }

            return data;
        }
    }

    // ---- The bubble sits beside the pill, never on it -----------------------------------------

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Bubble_ClearsThePillByExactlyTheGap(IslandShape shape, IslandAlignment alignment)
    {
        var screen = new IslandScreen(0, 0, 2560, 1);
        var pill = IslandGeometry.CollapsedRect(shape, alignment, screen);
        var bubble = IslandGeometry.BubbleRect(shape, alignment, screen);

        // Both silhouettes carry their own flares, and a gap measured without them lets the two
        // shapes touch in notch form -- which is the one thing that would look broken.
        var gap = IslandGeometry.BubbleMirrored(alignment)
            ? pill.Left - bubble.Right
            : bubble.Left - pill.Right;

        Assert.Equal(IslandGeometry.BubbleGap, gap, 6);
        Assert.False(pill.IntersectsHorizontally(bubble));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Bubble_ShareTheirTopEdgeWithThePill(IslandShape shape, IslandAlignment alignment)
    {
        var screen = new IslandScreen(0, 0, 2560, 1);
        var pill = IslandGeometry.CollapsedRect(shape, alignment, screen);
        var bubble = IslandGeometry.BubbleRect(shape, alignment, screen);

        // A detached pill drops clear of the screen edge; the bubble has to drop with it or the two
        // read as unrelated things at different heights.
        Assert.Equal(pill.Top, bubble.Top, 6);
        Assert.Equal(pill.Height, bubble.Height, 6);
    }

    [Theory]
    [InlineData(IslandAlignment.Left)]
    [InlineData(IslandAlignment.Center)]
    public void Bubble_SitsRightOfThePill_ExceptWhenPinnedToTheRightEdge(IslandAlignment alignment)
    {
        var screen = new IslandScreen(0, 0, 2560, 1);

        Assert.True(IslandGeometry.BubbleRect(IslandShape.Notch, alignment, screen).Left
            > IslandGeometry.CollapsedRect(IslandShape.Notch, alignment, screen).Right);
    }

    [Fact]
    public void Bubble_MirrorsWhenThereIsNoRoomToTheRight()
    {
        var screen = new IslandScreen(0, 0, 2560, 1);
        var pill = IslandGeometry.CollapsedRect(IslandShape.Notch, IslandAlignment.Right, screen);
        var bubble = IslandGeometry.BubbleRect(IslandShape.Notch, IslandAlignment.Right, screen);

        // A right-anchored pill sits EdgeMargin from the side of the screen: outboard of it there
        // is nothing but the edge, so the bubble goes to the pill's other side.
        Assert.True(bubble.Right < pill.Left);
        Assert.True(IslandGeometry.BubbleOffset(IslandShape.Notch, IslandAlignment.Right) < 0);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Bubble_StaysOnScreen(IslandScreen screen)
    {
        foreach (var (shape, alignment) in All())
        {
            var bubble = IslandGeometry.BubbleRect(shape, alignment, screen);

            Assert.True(bubble.Left >= screen.Left, $"{shape}/{alignment} ran off the left");
            Assert.True(bubble.Right <= screen.Right, $"{shape}/{alignment} ran off the right");
        }
    }

    /// <summary>
    /// Ties the number the *view* uses to the rectangle everything else here is proved against.
    ///
    /// BubbleOffset is a translate applied to an anchored element; BubbleRect is an absolute
    /// position derived from the pill. They are two statements of the same intent, and without this
    /// they could drift apart silently -- every invariant below would still pass while the bubble
    /// drew somewhere else entirely.
    /// </summary>
    [Theory]
    [MemberData(nameof(Combinations))]
    public void BubbleOffset_AppliedTheWayTheViewAppliesIt_LandsOnTheBubbleRect(
        IslandShape shape, IslandAlignment alignment)
    {
        var screen = new IslandScreen(-1920, 40, 1920, 1.5);
        var pill = IslandGeometry.CollapsedRect(shape, alignment, screen);
        var expected = IslandGeometry.BubbleRect(shape, alignment, screen);

        var x = IslandGeometry.BubbleOffset(shape, alignment) * screen.Scale;
        var width = IslandGeometry.BubbleFootprint(shape) * screen.Scale;

        // Exactly what IslandWindow.PlaceBubble does: anchor the element to the same edge as the
        // island, then translate it.
        var left = alignment switch
        {
            IslandAlignment.Left => pill.Left + x,
            IslandAlignment.Right => pill.Right + x - width,
            _ => pill.Left + pill.Width / 2 + x - width / 2
        };

        Assert.Equal(expected.Left, left, 6);
    }

    // ---- The hit region ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Combinations))]
    public void HitRect_Collapsed_CoversThePillAndItsSlack(IslandShape shape, IslandAlignment alignment)
    {
        var screen = new IslandScreen(0, 0, 2560, 1);
        var pill = IslandGeometry.CollapsedRect(shape, alignment, screen);
        var rect = IslandGeometry.HitRect(shape, alignment, screen, Collapsed(bubble: false));

        Assert.True(rect.Contains(pill));
        Assert.Equal(IslandGeometry.HoverSlack, pill.Left - rect.Left, 6);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void HitRect_WithABubble_CoversTheBubbleAndTheGapToIt(
        IslandShape shape, IslandAlignment alignment)
    {
        var screen = new IslandScreen(0, 0, 2560, 1);
        var rect = IslandGeometry.HitRect(shape, alignment, screen, Collapsed(bubble: true));

        var pill = IslandGeometry.CollapsedRect(shape, alignment, screen);
        var bubble = IslandGeometry.BubbleRect(shape, alignment, screen);

        // The gap is a strip the pointer crosses on the way over; a region that excluded it would
        // put the island away halfway there.
        Assert.True(rect.Contains(pill));
        Assert.True(rect.Contains(bubble));
        Assert.True(rect.Contains(BetweenX(pill, bubble), pill.Top + 1));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void HitRect_WithABubble_WidensOneSideOnlyAndLeavesThePillWhereItWas(
        IslandShape shape, IslandAlignment alignment)
    {
        var screen = new IslandScreen(0, 0, 2560, 1);
        var without = IslandGeometry.HitRect(shape, alignment, screen, Collapsed(bubble: false));
        var with = IslandGeometry.HitRect(shape, alignment, screen, Collapsed(bubble: true));

        var extent = IslandGeometry.BubbleExtent(shape);
        Assert.Equal(without.Width + extent, with.Width, 6);

        // Widening symmetrically would drag the pill half a bubble off its alignment.
        if (IslandGeometry.BubbleMirrored(alignment))
        {
            Assert.Equal(without.Left - extent, with.Left, 6);
            Assert.Equal(without.Right, with.Right, 6);
        }
        else
        {
            Assert.Equal(without.Left, with.Left, 6);
            Assert.Equal(without.Right + extent, with.Right, 6);
        }
    }

    /// <summary>
    /// The one that cannot be checked by looking at it, and the reason this class exists.
    ///
    /// Resting the pointer on the bubble expands the island, and expanding hides the bubble. If the
    /// expanded region did not still cover where the bubble had been, the pointer would fall out of
    /// it, the island would collapse, the bubble would come back under the pointer, and the whole
    /// thing would strobe.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void HitRect_Expanded_StillCoversWhereTheBubbleWas(IslandScreen screen)
    {
        foreach (var (shape, alignment) in All())
        {
            var bubble = IslandGeometry.BubbleRect(shape, alignment, screen);
            var expanded = IslandGeometry.HitRect(shape, alignment, screen, Expanded());

            Assert.True(expanded.Contains(bubble),
                $"{shape}/{alignment}: expanding drops the pointer off the bubble, which oscillates");
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void HitRect_Idle_IsAThinStripWithNoSlack(IslandShape shape, IslandAlignment alignment)
    {
        var screen = new IslandScreen(0, 0, 2560, 1);
        var idle = IslandGeometry.HitRect(shape, alignment, screen,
            new IslandHitState(Shown: false, Expanded: false, BubbleShown: false, 480, 132));

        // Thin on purpose: somewhere to throw the pointer at, not a region to avoid. Slack here
        // would make a band of the screen edge permanently reactive.
        var expected = IslandGeometry.PeekHeight
            + (shape == IslandShape.Pill ? IslandGeometry.PillTopGap : 0);

        Assert.Equal(expected, idle.Height, 6);
        Assert.Equal(screen.Top, idle.Top, 6);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void HitRect_StartsAtTheScreenEdgeInEveryState(IslandScreen screen)
    {
        foreach (var (shape, alignment) in All())
        {
            foreach (var state in new[] { Idle(), Collapsed(false), Collapsed(true), Expanded() })
            {
                // Measured from the edge down regardless of a detached pill's gap: that gap is a
                // strip the pointer crosses on the way to the pill.
                Assert.Equal(screen.Top, IslandGeometry.HitRect(shape, alignment, screen, state).Top, 6);
            }
        }
    }

    [Fact]
    public void HitRect_ScalesWithTheMonitor()
    {
        var oneToOne = new IslandScreen(0, 0, 2560, 1);
        var scaled = new IslandScreen(0, 0, 2560, 2);

        var a = IslandGeometry.HitRect(IslandShape.Notch, IslandAlignment.Left, oneToOne, Collapsed(true));
        var b = IslandGeometry.HitRect(IslandShape.Notch, IslandAlignment.Left, scaled, Collapsed(true));

        // Everything here is DIPs until it meets the scale, so a 200% monitor doubles the region.
        Assert.Equal(a.Width * 2, b.Width, 6);
        Assert.Equal(a.Height * 2, b.Height, 6);
    }

    [Fact]
    public void NotchFootprint_IsWiderThanThePillItDraws()
    {
        // The flares hang off both sides of the slab they frame. Forgetting them is what lets the
        // hit region come up short of the shape on screen.
        Assert.Equal(
            IslandGeometry.CollapsedFootprint(IslandShape.Pill) + IslandGeometry.FilletWidth * 2,
            IslandGeometry.CollapsedFootprint(IslandShape.Notch), 6);

        Assert.Equal(
            IslandGeometry.BubbleFootprint(IslandShape.Pill) + IslandGeometry.BubbleFillet * 2,
            IslandGeometry.BubbleFootprint(IslandShape.Notch), 6);
    }

    private static IslandHitState Idle() => new(false, false, false, 480, 132);

    private static IslandHitState Collapsed(bool bubble) => new(true, false, bubble, 480, 132);

    private static IslandHitState Expanded() => new(true, true, false, 660, 380);

    private static double BetweenX(IslandRect a, IslandRect b) =>
        (Math.Min(a.Right, b.Right) + Math.Max(a.Left, b.Left)) / 2;

    private static IEnumerable<(IslandShape Shape, IslandAlignment Alignment)> All()
    {
        foreach (var shape in new[] { IslandShape.Notch, IslandShape.Pill })
        {
            foreach (var alignment in new[]
                { IslandAlignment.Left, IslandAlignment.Center, IslandAlignment.Right })
            {
                yield return (shape, alignment);
            }
        }
    }
}
