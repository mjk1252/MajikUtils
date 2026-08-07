using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class AnnouncementActivityTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Announce_ClaimsThePillWithWhatItWasGiven()
    {
        var announcement = new AnnouncementActivity();

        announcement.Announce(Start, "Copied", "", "3 lines");

        Assert.True(announcement.IsActive);
        Assert.Equal("Copied", announcement.Label);
        Assert.Equal("3 lines", announcement.Detail);
    }

    [Fact]
    public void Tick_RetiresItOnceItsMomentHasPassed()
    {
        var announcement = new AnnouncementActivity();
        announcement.Announce(Start, "Copied", "");

        announcement.Tick(Start + TimeSpan.FromSeconds(1));
        Assert.True(announcement.IsActive);

        announcement.Tick(Start + AnnouncementActivity.Duration);
        Assert.False(announcement.IsActive);
    }

    [Fact]
    public void Announce_Twice_ReplacesAndRestartsTheClock()
    {
        var announcement = new AnnouncementActivity();
        announcement.Announce(Start, "Copied", "");

        // Turning the volume knob twice is one announcement that lasted longer, not two overlapping.
        announcement.Announce(Start + TimeSpan.FromSeconds(2), "Screenshot captured", "");

        Assert.Equal("Screenshot captured", announcement.Label);

        announcement.Tick(Start + TimeSpan.FromSeconds(4));
        Assert.True(announcement.IsActive);

        announcement.Tick(Start + TimeSpan.FromSeconds(5));
        Assert.False(announcement.IsActive);
    }

    [Fact]
    public void Announce_DoesNotLinger()
    {
        // The activity is already the grace period: it is showing something that has finished
        // happening, so holding it for another second and a half afterwards would be double.
        Assert.Equal(TimeSpan.Zero, new AnnouncementActivity().Linger);
    }

    [Fact]
    public void Priority_OutranksMusicAndConditions()
    {
        Assert.True(new AnnouncementActivity().Priority > IslandPriority.Ambient);
        Assert.True(new AnnouncementActivity().Priority > IslandPriority.Background);
    }
}

public class TimerActivityTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_BeginsCountingDown()
    {
        var timer = new TimerActivity();

        timer.Start(Start, TimeSpan.FromMinutes(5));

        Assert.True(timer.IsActive);
        Assert.False(timer.IsFinished);
        Assert.Equal("5:00", timer.RemainingText);
        Assert.Equal(0, timer.Progress, 3);
    }

    [Fact]
    public void Tick_AdvancesProgressAndTheReadout()
    {
        var timer = new TimerActivity();
        timer.Start(Start, TimeSpan.FromMinutes(4));

        timer.Tick(Start + TimeSpan.FromMinutes(1));

        Assert.Equal(0.25, timer.Progress, 3);
        Assert.Equal("3:00", timer.RemainingText);
    }

    [Fact]
    public void Tick_RoundsUpSoAFreshTimerReadsItsFullDuration()
    {
        var timer = new TimerActivity();
        timer.Start(Start, TimeSpan.FromMinutes(1));

        // A hair past the start. Truncating would show "0:59" the instant it began.
        timer.Tick(Start + TimeSpan.FromMilliseconds(1));

        Assert.Equal("1:00", timer.RemainingText);
    }

    [Fact]
    public void Tick_AtZero_FinishesButStaysUp()
    {
        var timer = new TimerActivity();
        timer.Start(Start, TimeSpan.FromMinutes(1));

        timer.Tick(Start + TimeSpan.FromMinutes(1));

        // There is no sound and no toast, so staying on the island is the whole notification.
        Assert.True(timer.IsFinished);
        Assert.True(timer.IsActive);
        Assert.Equal(1, timer.Progress, 3);
        Assert.Equal("Time's up", timer.RemainingText);
    }

    [Fact]
    public void Tick_LongAfterFinishing_GivesUpTheIsland()
    {
        var timer = new TimerActivity();
        timer.Start(Start, TimeSpan.FromMinutes(1));
        timer.Tick(Start + TimeSpan.FromMinutes(1));

        // A timer that went off while nobody was looking must not hold the pill until the next
        // restart.
        timer.Tick(Start + TimeSpan.FromMinutes(2));

        Assert.False(timer.IsActive);
        Assert.False(timer.IsFinished);
    }

    [Fact]
    public void Cancel_TakesItOffTheIslandImmediately()
    {
        var timer = new TimerActivity();
        timer.Start(Start, TimeSpan.FromMinutes(5));

        timer.CancelCommand.Execute(null);

        Assert.False(timer.IsActive);
    }

    [Fact]
    public void Start_WithNoDuration_DoesNothing()
    {
        var timer = new TimerActivity();

        timer.Start(Start, TimeSpan.Zero);

        Assert.False(timer.IsActive);
    }

    [Fact]
    public void Tick_WhileInactive_IsIgnored()
    {
        var timer = new TimerActivity();

        timer.Tick(Start + TimeSpan.FromHours(1));

        Assert.False(timer.IsActive);
        Assert.Equal(string.Empty, timer.RemainingText);
    }

    [Fact]
    public void Priority_RanksBelowMusic()
    {
        // A ring says everything a timer has to say, and says it for half an hour at a stretch.
        // Evicting a track for that is a bad trade however deliberately the timer was started.
        Assert.True(new TimerActivity().Priority < IslandPriority.Ambient);
    }
}

public class ConditionActivityTests
{
    [Fact]
    public void Priority_RanksBelowMusic()
    {
        var condition = new ConditionActivity { Key = "dnd", Label = "Do not disturb", Glyph = "" };

        // A condition that is usually true must not take the pill off something that is happening.
        Assert.True(condition.Priority < IslandPriority.Ambient);
    }

    [Fact]
    public void Linger_IsShortEnoughToTrackAPolledSource()
    {
        var condition = new ConditionActivity { Key = "dnd", Label = "Do not disturb", Glyph = "" };

        Assert.InRange(condition.Linger, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2));
    }
}
