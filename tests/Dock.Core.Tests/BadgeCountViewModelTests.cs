using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class BadgeCountViewModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Notifications in Windows' own centre: a real app id and a real count.</summary>
    private static AppNotifications[] Centre(params (string App, int Count)[] apps) =>
        [.. apps.Select(a => new AppNotifications($"Appid.{a.App}", a.App, a.Count, $"latest from {a.App}"))];

    /// <summary>Windows flashing for attention: an executable path, and never a number.</summary>
    private static AttentionRequest[] Attention(params string[] apps) =>
        [.. apps.Select(a => new AttentionRequest($@"C:\Apps\{a}\{a}.exe", a))];

    [Fact]
    public void Apply_WithNothingWaiting_ShowsNothing()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Array.Empty<AppNotifications>(), Start);

        Assert.Equal(0, badges.Count);
        Assert.False(badges.HasCount);
        Assert.Empty(badges.Badges);
    }

    [Fact]
    public void Apply_ShowsWhatIsWaitingRightNow()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Centre(("Outlook", 3)), Start);

        Assert.Equal(3, badges.Count);
        Assert.True(badges.HasCount);
        Assert.Equal("Outlook 3", badges.Summary);
        Assert.Equal("3", badges.Badges[0].CountText);
    }

    [Fact]
    public void Apply_CountsUpAsThingsArrive()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Array.Empty<AppNotifications>(), Start);

        badges.Apply(Centre(("Outlook", 1)), Start + TimeSpan.FromSeconds(2));
        Assert.Equal(1, badges.Count);

        badges.Apply(Centre(("Outlook", 2)), Start + TimeSpan.FromSeconds(4));
        Assert.Equal(2, badges.Count);
    }

    [Fact]
    public void Apply_FallsBackToNothingAsThingsAreRead()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Centre(("Outlook", 3)), Start);

        badges.Apply(Array.Empty<AppNotifications>(), Start + TimeSpan.FromSeconds(2));

        Assert.Equal(0, badges.Count);
        Assert.False(badges.HasCount);
    }

    /// <summary>
    /// The case Windows' notification centre cannot see: an app that draws its own notifications
    /// and raises no toast, but flashes. Most chat applications, and the reason this source exists.
    /// </summary>
    [Fact]
    public void Apply_Attention_ShowsAnAppTheCentreDoesNotKnow()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Attention("Discord"), Start);

        Assert.Single(badges.Badges);
        Assert.Equal("Discord", badges.Badges[0].AppName);
        Assert.Equal(1, badges.Count);

        // A flash carries no number, so the chip draws the icon alone. Printing a "1" would claim
        // a precision Windows never gave -- it could be thirty.
        Assert.False(badges.Badges[0].HasNumber);
    }

    /// <summary>
    /// A flash must not displace a real count. Letting it in beside "Outlook 3" would replace the
    /// three with a numberless chip and lose the part worth reading.
    /// </summary>
    [Fact]
    public void Apply_Attention_NeverReplacesARealCount()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Centre(("Outlook", 3)), Start);
        badges.Apply([new AttentionRequest(@"C:\Apps\Outlook\Outlook.exe", "Outlook")], Start + TimeSpan.FromSeconds(1));

        Assert.Single(badges.Badges);
        Assert.Equal(3, badges.Count);
        Assert.Equal("3", badges.Badges[0].CountText);
    }

    [Fact]
    public void Apply_Attention_ClearsWhenTheFlashingStops()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Attention("Discord"), Start);
        Assert.True(badges.HasCount);

        badges.Apply(Array.Empty<AttentionRequest>(), Start + TimeSpan.FromSeconds(1));

        Assert.Equal(0, badges.Count);
        Assert.False(badges.HasCount);
    }

    /// <summary>
    /// The bug that reached the screen. The two sources do not agree on what identifies an app:
    /// the notification centre carries an AppUserModelID, a flashing window carries only the
    /// executable behind it, because that is all a window has. Keyed on the identifier alone, one
    /// Discord arrived as two and drew two chips.
    /// </summary>
    [Fact]
    public void Apply_OneAppUnderTwoIdentifiersIsStillOneChip()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply([new AppNotifications("com.squirrel.Discord.Discord", "Discord", 2, "hi")], Start);

        badges.Apply(
            [new AttentionRequest(@"C:\Users\me\AppData\Local\Discord\app-1.0.9254\Discord.exe", "Discord")],
            Start + TimeSpan.FromSeconds(1));

        Assert.Single(badges.Badges);
        Assert.Equal("Discord", badges.Badges[0].AppName);
        Assert.Equal(2, badges.Count);
    }

    [Fact]
    public void Apply_DoesNotMergeUnrelatedApps()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Centre(("Outlook", 2), ("Teams", 1)), Start);
        badges.Apply(Attention("Discord"), Start + TimeSpan.FromSeconds(1));

        Assert.Equal(3, badges.Badges.Count);
        Assert.Equal(4, badges.Count);
    }

    /// <summary>One source reporting must not blank what the other last said.</summary>
    [Fact]
    public void Apply_OneSourceReportingDoesNotWipeTheOther()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Centre(("Outlook", 4)), Start);

        badges.Apply(Array.Empty<AttentionRequest>(), Start + TimeSpan.FromSeconds(1));

        Assert.Equal(4, badges.Count);
        Assert.Single(badges.Badges);
    }

    /// <summary>
    /// A chip per app, loudest first, so the app with the most waiting keeps its place when there
    /// is not room for everybody.
    /// </summary>
    [Fact]
    public void Apply_MakesAChipPerAppLoudestFirst()
    {
        var badges = new BadgeCountViewModel();

        badges.Apply(Centre(("Discord", 1), ("Outlook", 7)), Start);

        Assert.Collection(badges.Badges,
            first => Assert.Equal("Outlook", first.AppName),
            second => Assert.Equal("Discord", second.AppName));

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

        badges.Apply(Centre(("A", 5), ("B", 4), ("C", 3), ("D", 2), ("E", 1)), Start);

        Assert.Equal(BadgeCountViewModel.MaxChips, badges.Badges.Count);
        Assert.Equal(2, badges.Overflow);
        Assert.True(badges.HasOverflow);

        // The total still counts every one of them, fitting or not.
        Assert.Equal(15, badges.Count);
    }

    /// <summary>
    /// A count ticking over updates the chip in place. Replacing it would blink the icon every
    /// couple of seconds for no reason.
    /// </summary>
    [Fact]
    public void Apply_UpdatesAChipInPlaceWhenOnlyTheCountMoves()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Centre(("Outlook", 1)), Start);

        var chip = badges.Badges[0];

        badges.Apply(Centre(("Outlook", 2)), Start + TimeSpan.FromSeconds(2));

        Assert.Same(chip, badges.Badges[0]);
        Assert.Equal("2", chip.CountText);
    }

    [Fact]
    public void IsNew_MarksAnArrivalAndThenLetsItSettle()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Array.Empty<AppNotifications>(), Start);

        badges.Apply(Centre(("Outlook", 1)), Start + TimeSpan.FromSeconds(2));
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
        badges.Apply(Centre(("Outlook", 4)), Start);
        badges.Tick(Start + BadgeCountViewModel.Highlight);

        badges.Apply(Centre(("Outlook", 2)), Start + TimeSpan.FromMinutes(1));

        Assert.False(badges.IsNew);
        Assert.Equal(2, badges.Count);
    }

    /// <summary>
    /// A reading can hand over the same app twice, and the first reconcile threw on it: a Move
    /// past the end of a collection with one item in it. The exception landed on the UI thread
    /// inside a handler that swallows them, so the count silently never updated at all -- exactly
    /// the failure mode this whole feature cannot have.
    /// </summary>
    [Fact]
    public void Apply_SurvivesTheSameAppAppearingTwice()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Centre(("Discord", 1)), Start);

        AppNotifications[] duplicated =
        [
            new("Appid.Discord", "Discord", 1, ""),
            new("Appid.Discord", "Discord", 1, "")
        ];

        var exception = Record.Exception(() => badges.Apply(duplicated, Start + TimeSpan.FromSeconds(2)));

        Assert.Null(exception);
        Assert.Single(badges.Badges);
        Assert.Equal(1, badges.Count);
    }

    [Fact]
    public void Clear_ForgetsBothSources()
    {
        var badges = new BadgeCountViewModel();
        badges.Apply(Centre(("Outlook", 2)), Start);
        badges.Apply(Attention("Discord"), Start + TimeSpan.FromSeconds(1));

        badges.Clear();

        Assert.Equal(0, badges.Count);
        Assert.False(badges.HasCount);
        Assert.Empty(badges.Badges);
        Assert.Equal("", badges.Summary);

        // And a later reading from one source does not resurrect the other's.
        badges.Apply(Centre(("Outlook", 2)), Start + TimeSpan.FromSeconds(2));
        Assert.Equal(2, badges.Count);
    }
}
