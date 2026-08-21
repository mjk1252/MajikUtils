using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class BadgeCountViewModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static TaskbarBadgeSnapshot Snapshot(int centre = 0, params (string App, int Count)[] apps) =>
        new([.. apps.Select(a => new TaskbarBadge($"Appid.{a.App}", a.App, a.Count))], centre);

    [Fact]
    public void Apply_WithNothingWaiting_ShowsNothing()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(TaskbarBadgeSnapshot.Empty, Start);

        Assert.Equal(0, badges.Count);
        Assert.False(badges.HasCount);
    }

    [Fact]
    public void Apply_ShowsWhatIsWaitingRightNow()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Snapshot(0, ("Outlook", 3)), Start);

        Assert.Equal(3, badges.Count);
        Assert.True(badges.HasCount);
        Assert.Equal("Outlook 3", badges.Summary);
    }

    [Fact]
    public void Apply_CountsUpAsThingsArrive()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(TaskbarBadgeSnapshot.Empty, Start);

        badges.Apply(Snapshot(0, ("Outlook", 1)), Start + TimeSpan.FromSeconds(2));
        Assert.Equal(1, badges.Count);

        badges.Apply(Snapshot(0, ("Outlook", 2)), Start + TimeSpan.FromSeconds(4));
        Assert.Equal(2, badges.Count);
    }

    /// <summary>
    /// The case that sank the baseline model, kept as a test so it cannot come back.
    ///
    /// Discord reports a dot as "Attention requested, 0 notifications", which floors to one thing
    /// waiting, and then reports a real one as "1 notifications". Differenced against a baseline
    /// taken at the dot, a genuine arrival produced no change at all. Read as a live total it is
    /// simply one waiting thing throughout, which is true.
    /// </summary>
    [Fact]
    public void Apply_ADotFollowedByARealCountOfOneStaysOne()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Snapshot(0, ("Discord", 0)), Start);
        Assert.Equal(1, badges.Count);
        Assert.True(badges.HasCount);

        badges.Apply(Snapshot(0, ("Discord", 1)), Start + TimeSpan.FromSeconds(2));
        Assert.Equal(1, badges.Count);
        Assert.True(badges.HasCount);

        // And the one after that is visibly two, which is the part that has to work.
        badges.Apply(Snapshot(0, ("Discord", 2)), Start + TimeSpan.FromSeconds(4));
        Assert.Equal(2, badges.Count);
    }

    /// <summary>One number across every source, which is the question "have I missed anything".</summary>
    [Fact]
    public void Apply_CombinesEveryAppAndTheNotificationCentre()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Snapshot(2, ("Outlook", 3), ("Discord", 1)), Start);

        Assert.Equal(6, badges.Count);
        Assert.Equal("Outlook 3 · Discord 1 · 2 in notifications", badges.Summary);
    }

    /// <summary>
    /// A badge with no number on it is one thing waiting. Counting it as zero would make an app
    /// that only ever badges with a dot -- Discord, most chat apps -- invisible.
    /// </summary>
    [Fact]
    public void Apply_CountsANumberlessBadgeAsOne()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Snapshot(0, ("Discord", 0)), Start);

        Assert.Equal(1, badges.Count);
        Assert.Equal("Discord", badges.Summary);
    }

    [Fact]
    public void Apply_FallsBackToNothingAsThingsAreRead()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Snapshot(0, ("Outlook", 3)), Start);

        badges.Apply(Snapshot(0, ("Outlook", 1)), Start + TimeSpan.FromSeconds(2));
        Assert.Equal(1, badges.Count);

        badges.Apply(TaskbarBadgeSnapshot.Empty, Start + TimeSpan.FromSeconds(4));
        Assert.Equal(0, badges.Count);
        Assert.False(badges.HasCount);
    }

    [Fact]
    public void IsNew_MarksAnArrivalAndThenLetsItSettle()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(TaskbarBadgeSnapshot.Empty, Start);

        badges.Apply(Snapshot(0, ("Outlook", 1)), Start + TimeSpan.FromSeconds(2));
        Assert.True(badges.IsNew);

        badges.Tick(Start + TimeSpan.FromSeconds(2) + BadgeCountViewModel.Highlight);
        Assert.False(badges.IsNew);

        // Settling is only about how it looks. It is still waiting, and still counted.
        Assert.Equal(1, badges.Count);
        Assert.True(badges.HasCount);
    }

    /// <summary>Reading something is not an event to flash about. That was the user.</summary>
    [Fact]
    public void IsNew_StaysDownWhenTheCountFalls()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Snapshot(0, ("Outlook", 4)), Start);
        badges.Tick(Start + BadgeCountViewModel.Highlight);

        badges.Apply(Snapshot(0, ("Outlook", 2)), Start + TimeSpan.FromMinutes(1));

        Assert.False(badges.IsNew);
        Assert.Equal(2, badges.Count);
    }

    [Fact]
    public void Clear_TakesEverythingOffTheIsland()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Snapshot(0, ("Outlook", 2)), Start);

        badges.Clear();

        Assert.Equal(0, badges.Count);
        Assert.False(badges.HasCount);
        Assert.Empty(badges.Badges);
        Assert.Equal("", badges.Summary);
    }

    /// <summary>
    /// The walk can hand over the same app twice, and the first reconcile threw on it: a Move past
    /// the end of a collection with one item in it. The exception landed on the UI thread inside a
    /// handler that swallows them, so the count silently never updated at all -- which is exactly
    /// the failure mode this whole feature cannot have.
    /// </summary>
    [Fact]
    public void Apply_SurvivesTheSameAppAppearingTwice()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Snapshot(0, ("Discord", 1)), Start);

        var duplicated = new TaskbarBadgeSnapshot(
            [new TaskbarBadge("Appid.Discord", "Discord", 1), new TaskbarBadge("Appid.Discord", "Discord", 1)],
            0);

        var exception = Record.Exception(() => badges.Apply(duplicated, Start + TimeSpan.FromSeconds(2)));

        Assert.Null(exception);
        Assert.Equal(2, badges.Count);
    }

    [Fact]
    public void Apply_KeepsTheRowsRatherThanRebuildingThem()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Snapshot(0, ("Discord", 1), ("Outlook", 2)), Start);

        var changes = 0;
        badges.Badges.CollectionChanged += (_, _) => changes++;

        badges.Apply(Snapshot(0, ("Discord", 1), ("Outlook", 2)), Start + TimeSpan.FromSeconds(2));

        Assert.Equal(0, changes);
    }

    /// <summary>
    /// A chip per badged app, loudest first, so the app with the most waiting keeps its place when
    /// there is not room for everybody.
    /// </summary>
    [Fact]
    public void Apply_MakesAChipPerAppLoudestFirst()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Snapshot(0, ("Discord", 1), ("Outlook", 7)), Start);

        Assert.Collection(badges.Badges,
            first => Assert.Equal("Outlook", first.AppName),
            second => Assert.Equal("Discord", second.AppName));

        Assert.Equal("7", badges.Badges[0].CountText);
        Assert.False(badges.HasOverflow);
    }

    /// <summary>
    /// The pill is a strip with album art and a clock on it too, so past three the rest are counted
    /// rather than drawn. Silently dropping them would be the one unacceptable answer.
    /// </summary>
    [Fact]
    public void Apply_CountsWhatDoesNotFitRatherThanDroppingIt()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Snapshot(0, ("A", 5), ("B", 4), ("C", 3), ("D", 2), ("E", 1)), Start);

        Assert.Equal(BadgeCountViewModel.MaxChips, badges.Badges.Count);
        Assert.Equal(2, badges.Overflow);
        Assert.True(badges.HasOverflow);

        // The total still counts every one of them, fitting or not.
        Assert.Equal(15, badges.Count);
    }

    /// <summary>
    /// A wordless badge draws no number. The icon has already said the app has something, and a
    /// made-up "1" beside it would claim a precision Windows never gave -- it could be thirty.
    /// </summary>
    [Fact]
    public void Apply_LeavesAWordlessBadgeWithoutANumber()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Snapshot(0, ("Discord", 0)), Start);

        Assert.Equal("", badges.Badges[0].CountText);
        Assert.False(badges.Badges[0].HasNumber);

        // Still one waiting thing as far as the total is concerned.
        Assert.Equal(1, badges.Count);
    }

    /// <summary>
    /// A count ticking over updates the chip in place. Replacing it would refetch nothing -- the
    /// icon is cached -- but would blink the row every two seconds for no reason.
    /// </summary>
    [Fact]
    public void Apply_UpdatesAChipInPlaceWhenOnlyTheCountMoves()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Snapshot(0, ("Outlook", 1)), Start);

        var chip = badges.Badges[0];

        badges.Apply(Snapshot(0, ("Outlook", 2)), Start + TimeSpan.FromSeconds(2));

        Assert.Same(chip, badges.Badges[0]);
        Assert.Equal("2", chip.CountText);
    }
}
