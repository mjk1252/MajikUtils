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
        Assert.Contains("->", report);
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
