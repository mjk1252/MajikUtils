using Dock.Core.Models;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

/// <summary>
/// The activity's behaviour over a day: when it takes the island, what dismissing it means, and
/// what happens at midnight.
///
/// The last of those is the one worth having tests for. It is a rule that only fires once a day on
/// a machine that has been left running, which is to say it is a rule nobody would ever catch by
/// using the app.
/// </summary>
public class BirthdayActivityTests
{
    private static readonly DateOnly March14 = new(2026, 3, 14);
    private static readonly DateOnly March15 = new(2026, 3, 15);

    private static List<Birthday> List(params Birthday[] birthdays) => [.. birthdays];

    private static readonly Birthday Ada = new("Ada", 3, 14, 1990);
    private static readonly Birthday Mum = new("Mum", 3, 14, null);
    private static readonly Birthday Tom = new("Tom", 3, 15, null);

    [Fact]
    public void OnTheDay_ItClaimsTheIsland()
    {
        var activity = new BirthdayActivity();
        activity.Apply(List(Ada, Tom), March14);

        Assert.True(activity.IsActive);
        Assert.Equal("Ada is 36 today", activity.Headline);
        Assert.Single(activity.Today);
    }

    [Fact]
    public void OnAnyOtherDay_ItSaysNothing()
    {
        var activity = new BirthdayActivity();
        activity.Apply(List(Ada), new DateOnly(2026, 6, 1));

        Assert.False(activity.IsActive);
        Assert.Empty(activity.Today);
    }

    /// <summary>Without a year there is no age, and the sentence has to work anyway.</summary>
    [Fact]
    public void WithNoYear_TheHeadlineDropsTheAge()
    {
        var activity = new BirthdayActivity();
        activity.Apply(List(Mum), March14);

        Assert.Equal("It's Mum's birthday", activity.Headline);
    }

    [Fact]
    public void SeveralOnOneDay_AreCounted()
    {
        var activity = new BirthdayActivity();
        activity.Apply(List(Ada, Mum), March14);

        Assert.Equal("2 birthdays today", activity.Headline);
        Assert.True(activity.HasSeveral);
    }

    /// <summary>The top of the ladder, and the only thing in the app that sits there.</summary>
    [Fact]
    public void ItOutranksEverything()
    {
        Assert.Equal(IslandPriority.Alert, new BirthdayActivity().Priority);
    }

    /// <summary>
    /// It has no clock behind it: the whole point is that it stays until it is acknowledged. So
    /// nothing but Dismiss can take it off the island on the day.
    /// </summary>
    [Fact]
    public void ItStaysUntilDismissed()
    {
        var activity = new BirthdayActivity();
        activity.Apply(List(Ada), March14);

        // However long the day goes on, and however often anything else re-reads the date.
        activity.Tick(March14);
        activity.Tick(March14);
        Assert.True(activity.IsActive);

        activity.Dismiss();
        Assert.False(activity.IsActive);
    }

    [Fact]
    public void Dismissing_ReportsTheDateToBeSaved()
    {
        var activity = new BirthdayActivity();
        activity.Apply(List(Ada), March14);

        DateOnly? saved = null;
        activity.Dismissed += date => saved = date;

        activity.Dismiss();

        Assert.Equal(March14, saved);
    }

    /// <summary>
    /// A dismissal has to survive a restart, or the button is decoration: dismiss, restart for any
    /// reason at all, and the same birthday is back claiming the pill.
    /// </summary>
    [Fact]
    public void ARestoredDismissal_KeepsItOff()
    {
        var activity = new BirthdayActivity();
        activity.RestoreDismissal(March14);
        activity.Apply(List(Ada), March14);

        Assert.False(activity.IsActive);
    }

    /// <summary>
    /// And it has to expire, which is the other half of the same rule. Yesterday's dismissal
    /// swallowing this morning's birthday is the failure that would go unnoticed for a year.
    /// </summary>
    [Fact]
    public void ADismissalDoesNotCarryIntoTheNextDay()
    {
        var activity = new BirthdayActivity();
        activity.Apply(List(Ada, Tom), March14);
        activity.Dismiss();

        activity.Tick(March15);

        Assert.True(activity.IsActive);
        Assert.Equal("It's Tom's birthday", activity.Headline);
    }

    /// <summary>Midnight on a day with nobody on it takes the island back.</summary>
    [Fact]
    public void TheDayRollingOver_ClearsAFinishedBirthday()
    {
        var activity = new BirthdayActivity();
        activity.Apply(List(Ada), March14);

        activity.Tick(March15);

        Assert.False(activity.IsActive);
        Assert.Empty(activity.Today);
    }

    /// <summary>
    /// The confetti's cue. It fires on becoming active, and must not fire again for a day that has
    /// not changed -- the tick runs four times a second.
    /// </summary>
    [Fact]
    public void TheCelebration_IsRaisedOnceForTheDay()
    {
        var activity = new BirthdayActivity();

        var celebrations = 0;
        activity.Celebrated += () => celebrations++;

        activity.Apply(List(Ada), March14);
        activity.Tick(March14);
        activity.Tick(March14);

        Assert.Equal(1, celebrations);
    }

    /// <summary>
    /// Switched off in Settings is not the same as dismissed, and the difference is that this one
    /// does not expire at midnight. Folding the toggle into the dismissal date would have made
    /// turning it off silently turn itself back on the next morning.
    /// </summary>
    [Fact]
    public void Disabled_ItNeverClaimsTheIsland()
    {
        var activity = new BirthdayActivity { Enabled = false };
        activity.Apply(List(Ada), March14);

        Assert.False(activity.IsActive);

        activity.Tick(March15);
        activity.Apply(List(Tom), March15);
        Assert.False(activity.IsActive);

        // And switching it back on takes effect on the day already in progress.
        activity.Enabled = true;
        Assert.True(activity.IsActive);
    }
}
