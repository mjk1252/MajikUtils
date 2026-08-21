using Dock.Core.Models;

namespace Dock.Core.Tests;

/// <summary>
/// The self-diagnosis. Written because the fault it exists for turned up on a machine nobody
/// debugging it could reach, and reading the code produced four plausible causes and no way to
/// tell which was real.
/// </summary>
public class FlickerWatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 21, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_SaysNothingAboutOrdinaryUse()
    {
        var watch = new FlickerWatch();

        // Opening the island, pinning it, unpinning and closing: four transitions over seconds,
        // which is a person using it.
        Assert.Null(watch.Record(Start, "shown (pointer)"));
        Assert.Null(watch.Record(Start + TimeSpan.FromMilliseconds(200), "expanded (pointer on it)"));
        Assert.Null(watch.Record(Start + TimeSpan.FromSeconds(4), "collapsed (pointer away)"));
        Assert.Null(watch.Record(Start + TimeSpan.FromSeconds(5), "hidden (poll: nothing)"));
    }

    [Fact]
    public void Record_ReportsOnceTheTransitionsStopMakingSense()
    {
        var watch = new FlickerWatch();
        string? report = null;

        for (var i = 0; i < FlickerWatch.Threshold; i++)
            report ??= watch.Record(Start + TimeSpan.FromMilliseconds(i * 100), "shown (poll: activity)");

        Assert.NotNull(report);
        Assert.Contains("6 times", report);
        Assert.Contains("shown (poll: activity)", report);
    }

    /// <summary>
    /// The reasons are the whole point. A report saying only that it flickered would leave whoever
    /// reads it exactly where they started.
    /// </summary>
    /// <summary>
    /// The reasons carry pointer coordinates, so every one of them is a unique string. Tallying
    /// them literally would report six groups of one and say less than no tally at all.
    /// </summary>
    [Fact]
    public void Record_TalliesByKindRatherThanByExactReason()
    {
        var watch = new FlickerWatch();
        string? report = null;

        for (var i = 0; i < FlickerWatch.Threshold; i++)
        {
            report ??= watch.Record(
                Start + TimeSpan.FromMilliseconds(i * 100),
                $"collapsed (pointer away @{1200 + i},{4 + i} outside [0,0 588x42])");
        }

        Assert.NotNull(report);
        Assert.Contains("collapsed (pointer away x6", report);

        // And the particulars survive underneath, which is the whole reason they are collected.
        Assert.Contains("@1203,7", report);
    }

    [Fact]
    public void Record_NamesWhatDroveEachChange()
    {
        var watch = new FlickerWatch();
        string? report = null;

        for (var i = 0; i < FlickerWatch.Threshold; i++)
        {
            var reason = i % 2 == 0 ? "shown (poll: activity)" : "hidden (poll: nothing)";
            report ??= watch.Record(Start + TimeSpan.FromMilliseconds(i * 100), reason);
        }

        Assert.NotNull(report);
        Assert.Contains("shown (poll: activity) x3", report);
        Assert.Contains("hidden (poll: nothing) x3", report);
        Assert.Contains("19:00:00", report);
    }

    /// <summary>Transitions spread out past the window are not a flicker, however many there are.</summary>
    [Fact]
    public void Record_IgnoresTransitionsSpreadOverTime()
    {
        var watch = new FlickerWatch();

        for (var i = 0; i < 20; i++)
            Assert.Null(watch.Record(Start + TimeSpan.FromSeconds(i * 2), "shown (poll: activity)"));
    }

    /// <summary>
    /// A flicker does not stop because it was written down. Reporting every burst would fill the
    /// log at ten lines a second and say nothing the first one did not.
    /// </summary>
    [Fact]
    public void Record_StaysQuietForAWhileAfterReporting()
    {
        var watch = new FlickerWatch();
        var at = Start;

        string? Burst()
        {
            string? first = null;
            for (var i = 0; i < FlickerWatch.Threshold; i++)
            {
                first ??= watch.Record(at, "shown (poll: activity)");
                at += TimeSpan.FromMilliseconds(100);
            }
            return first;
        }

        Assert.NotNull(Burst());
        Assert.Null(Burst());

        at += FlickerWatch.Cooldown;
        Assert.NotNull(Burst());
    }

    /// <summary>
    /// The watch is shared between islands, and that is why. Per-window, two islands flipping
    /// three times each is six changes on screen and three in each watch -- under the threshold in
    /// both, so a visibly flickering machine produced no log at all. Pooled, it trips.
    /// </summary>
    [Fact]
    public void Record_TripsOnTwoIslandsFlickeringTogether()
    {
        var watch = new FlickerWatch();
        string? report = null;
        var at = Start;

        for (var i = 0; i < 3; i++)
        {
            report ??= watch.Record(at, @"[\.\DISPLAY1] expanded (pointer on it)");
            at += TimeSpan.FromMilliseconds(100);

            report ??= watch.Record(at, @"[\.\DISPLAY2] expanded (pointer on it)");
            at += TimeSpan.FromMilliseconds(100);
        }

        Assert.NotNull(report);
        Assert.Contains("DISPLAY1", report);
        Assert.Contains("DISPLAY2", report);
    }

    /// <summary>The list cannot grow without bound on an app left running for weeks.</summary>
    [Fact]
    public void Record_DoesNotAccumulate()
    {
        var watch = new FlickerWatch();

        for (var i = 0; i < 10_000; i++)
            watch.Record(Start + TimeSpan.FromSeconds(i), "shown (poll: activity)");

        // Still reports on a genuine burst afterwards, which it could not do if it were confused
        // by ten thousand stale entries.
        var at = Start + TimeSpan.FromSeconds(20_000);
        string? report = null;

        for (var i = 0; i < FlickerWatch.Threshold; i++)
            report ??= watch.Record(at + TimeSpan.FromMilliseconds(i * 100), "hidden (poll: nothing)");

        Assert.NotNull(report);
    }
}
