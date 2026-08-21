using Dock.Core.Models;

namespace Dock.Core.Tests;

/// <summary>
/// The flicker, as a test.
///
/// The island reported changing state six times in three seconds with the pointer, in the user's
/// words, nowhere near it. It was: at y=0, moving along the top edge of the screen between two
/// monitors, straight through the three-pixel strip that summons a hidden island. Every crossing
/// brought it out and put it away again. The strip has no slack, so a single poll inside it was
/// enough.
/// </summary>
public class HoverGateTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 22, 0, 58, 5, TimeSpan.Zero);

    /// <summary>
    /// The reported case, replayed. A pointer crossing at any ordinary speed is inside the strip
    /// for one or two polls of the 120ms loop, and must not count as a hover.
    /// </summary>
    [Fact]
    public void Update_IgnoresAPointerPassingThrough()
    {
        var gate = new HoverGate();
        var at = Start;

        for (var pass = 0; pass < 4; pass++)
        {
            // Two polls inside the strip -- about 240ms of travel across 260 pixels.
            Assert.False(gate.Update(at, inside: true));
            at += TimeSpan.FromMilliseconds(120);
            Assert.False(gate.Update(at, inside: true));
            at += TimeSpan.FromMilliseconds(120);

            // And gone again.
            Assert.False(gate.Update(at, inside: false));
            at += TimeSpan.FromSeconds(1);
        }
    }

    [Fact]
    public void Update_OpensForSomebodyWhoStops()
    {
        var gate = new HoverGate();

        Assert.False(gate.Update(Start, inside: true));
        Assert.True(gate.Update(Start + HoverGate.Dwell, inside: true));
        Assert.True(gate.IsOpen);
    }

    /// <summary>Leaving needs the same patience, or the island drops on a single stray reading.</summary>
    [Fact]
    public void Update_StaysOpenThroughAMomentaryExcursion()
    {
        var gate = new HoverGate();
        gate.Update(Start, inside: true);
        Assert.True(gate.Update(Start + HoverGate.Dwell, inside: true));

        var at = Start + HoverGate.Dwell;

        // One poll's worth outside, then back.
        Assert.True(gate.Update(at + TimeSpan.FromMilliseconds(120), inside: false));
        Assert.True(gate.Update(at + TimeSpan.FromMilliseconds(240), inside: true));
    }

    [Fact]
    public void Update_ClosesOnceThePointerHasGenuinelyGone()
    {
        var gate = new HoverGate();
        gate.Update(Start, inside: true);
        gate.Update(Start + HoverGate.Dwell, inside: true);

        var at = Start + HoverGate.Dwell;

        Assert.True(gate.Update(at + TimeSpan.FromMilliseconds(120), inside: false));
        Assert.False(gate.Update(at + TimeSpan.FromMilliseconds(120) + HoverGate.Dwell, inside: false));
    }

    /// <summary>
    /// A pointer sitting exactly on the boundary flips either side of it between polls. Those
    /// moments must not accumulate into a decision -- each change of mind starts the clock again.
    /// </summary>
    [Fact]
    public void Update_DoesNotAccumulateAcrossAFlappingPointer()
    {
        var gate = new HoverGate();
        var at = Start;

        for (var i = 0; i < 20; i++)
        {
            gate.Update(at, inside: i % 2 == 0);
            at += TimeSpan.FromMilliseconds(120);
        }

        Assert.False(gate.IsOpen);
    }

    /// <summary>A hotkey is not a hover and has nothing to prove.</summary>
    [Fact]
    public void ForceOpen_OpensWithoutWaiting()
    {
        var gate = new HoverGate();

        gate.ForceOpen(Start);

        Assert.True(gate.IsOpen);
        Assert.True(gate.Update(Start, inside: true));
    }

    /// <summary>
    /// And having been forced open, it still closes properly once the pointer is elsewhere -- the
    /// island opened by a hotkey has to go away again like any other.
    /// </summary>
    [Fact]
    public void ForceOpen_StillClosesAfterwards()
    {
        var gate = new HoverGate();
        gate.ForceOpen(Start);

        Assert.True(gate.Update(Start + TimeSpan.FromMilliseconds(120), inside: false));
        Assert.False(gate.Update(Start + TimeSpan.FromMilliseconds(120) + HoverGate.Dwell, inside: false));
    }
}
