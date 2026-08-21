using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class PomodoroActivityTests
{
    private static readonly DateTimeOffset Nine = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static PomodoroActivity Started()
    {
        var pom = new PomodoroActivity();
        pom.Start(Nine);
        return pom;
    }

    [Fact]
    public void Start_BeginsOnFocus()
    {
        var pom = Started();

        Assert.True(pom.IsActive);
        Assert.Equal(PomodoroPhase.Focus, pom.Phase);
        Assert.Equal("Focus", pom.PhaseLabel);
        Assert.Equal("25:00", pom.RemainingText);
        Assert.Equal(0, pom.CompletedRounds);
    }

    [Fact]
    public void Tick_CountsTheFocusDown()
    {
        var pom = Started();

        pom.Tick(Nine.AddMinutes(10));

        Assert.Equal(PomodoroPhase.Focus, pom.Phase);
        Assert.Equal("15:00", pom.RemainingText);
        Assert.Equal(0.4, pom.Progress, 3);
    }

    /// <summary>
    /// The break starts itself. Pressing a button first is a tax on having just concentrated for
    /// twenty-five minutes.
    /// </summary>
    [Fact]
    public void Focus_RollsStraightIntoTheBreak()
    {
        var pom = Started();

        pom.Tick(Nine.AddMinutes(25));

        Assert.Equal(PomodoroPhase.ShortBreak, pom.Phase);
        Assert.Equal("Break", pom.PhaseLabel);
        Assert.True(pom.IsBreak);
        Assert.Equal(1, pom.CompletedRounds);
        Assert.Equal("5:00", pom.RemainingText);
    }

    /// <summary>
    /// And so does the next focus round, which is the half that makes it a rhythm rather than a
    /// stack of timers you keep having to feed.
    /// </summary>
    [Fact]
    public void Break_RollsStraightBackIntoFocus()
    {
        var pom = Started();

        pom.Tick(Nine.AddMinutes(30));

        Assert.Equal(PomodoroPhase.Focus, pom.Phase);
        Assert.False(pom.IsBreak);
        Assert.Equal(1, pom.CompletedRounds);
    }

    [Fact]
    public void FourthRound_EarnsTheLongBreak()
    {
        var pom = Started();

        // 25 + 5, three times over, then the fourth focus round.
        pom.Tick(Nine.AddMinutes(30 * 3 + 25));

        Assert.Equal(PomodoroPhase.LongBreak, pom.Phase);
        Assert.Equal("Long break", pom.PhaseLabel);
        Assert.Equal("15:00", pom.RemainingText);
        Assert.Equal(4, pom.CompletedRounds);
        Assert.Equal(4, pom.TotalRounds);
    }

    /// <summary>The long break closes the set, so the dots start again but the tally does not.</summary>
    [Fact]
    public void AfterTheLongBreak_TheSetStartsOver()
    {
        var pom = Started();

        pom.Tick(Nine.AddMinutes(30 * 3 + 25 + 15));

        Assert.Equal(PomodoroPhase.Focus, pom.Phase);
        Assert.Equal(0, pom.CompletedRounds);
        Assert.Equal(4, pom.TotalRounds);
        Assert.Equal("○○○○", pom.RoundDots);
    }

    [Theory]
    [InlineData(0, "○○○○")]
    [InlineData(25, "●○○○")]
    [InlineData(55, "●●○○")]
    public void RoundDots_FillAsTheyAreEarned(int minutes, string expected)
    {
        var pom = Started();

        pom.Tick(Nine.AddMinutes(minutes));

        Assert.Equal(expected, pom.RoundDots);
    }

    /// <summary>
    /// A laptop that slept through three phases has to land where it should have, not three ticks
    /// later -- which is why Tick advances in a loop rather than one phase per call.
    /// </summary>
    [Fact]
    public void Tick_AfterSleepingThroughSeveralPhases_LandsInTheRightOne()
    {
        var pom = Started();

        // Straight from the first second to two hours later, in a single tick.
        pom.Tick(Nine.AddMinutes(120));

        // 25+5+25+5+25+5 = 90 puts the fourth focus at 90..115, then the long break at 115..130.
        Assert.Equal(PomodoroPhase.LongBreak, pom.Phase);
        Assert.Equal(4, pom.TotalRounds);
    }

    [Fact]
    public void Skip_MovesToTheNextPhase()
    {
        var pom = Started();

        pom.SkipCommand.Execute(null);

        Assert.Equal(PomodoroPhase.ShortBreak, pom.Phase);
        Assert.Equal(1, pom.CompletedRounds);
    }

    [Fact]
    public void Stop_TakesItOffTheIsland()
    {
        var pom = Started();

        pom.StopCommand.Execute(null);

        Assert.False(pom.IsActive);

        // And a stopped cycle stays stopped: the tick that would have advanced it does nothing.
        pom.Tick(Nine.AddMinutes(40));
        Assert.Equal(PomodoroPhase.Focus, pom.Phase);
    }

    [Fact]
    public void Start_Again_BeginsAFreshSet()
    {
        var pom = Started();
        pom.Tick(Nine.AddMinutes(55));

        pom.Start(Nine.AddHours(3));

        Assert.Equal(PomodoroPhase.Focus, pom.Phase);
        Assert.Equal(0, pom.CompletedRounds);
        Assert.Equal(0, pom.TotalRounds);
    }

    [Fact]
    public void Priority_SitsInTheBubbleRatherThanTakingThePill()
    {
        Assert.Equal(IslandPriority.Background, new PomodoroActivity().Priority);
    }

    // ---------------------------------------------------------------- the grammar

    [Theory]
    [InlineData("pom")]
    [InlineData("pomodoro")]
    [InlineData("Pomodoro")]
    [InlineData("  POM  ")]
    public void Parse_TheWord_StartsTheCycle(string draft)
    {
        Assert.Equal(CaptureKind.Pomodoro, CaptureViewModel.Parse(draft, Nine).Kind);
    }

    /// <summary>
    /// The one rule triggered by a word rather than a mark, so it has to be the narrowest possible
    /// match: anything with more in it is somebody writing a task.
    /// </summary>
    [Theory]
    [InlineData("pomodoro timer for the kitchen")]
    [InlineData("buy a pomodoro")]
    [InlineData("pompom")]
    [InlineData("pom pom")]
    public void Parse_AnythingElse_IsStillATask(string draft)
    {
        Assert.Equal(CaptureKind.Todo, CaptureViewModel.Parse(draft, Nine).Kind);
    }
}
