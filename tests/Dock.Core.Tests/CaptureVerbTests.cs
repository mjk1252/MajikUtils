using Dock.Core.Models;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

/// <summary>
/// The two verbs added after the grammar settled. Kept apart from CaptureViewModelTests because the
/// thing they mostly have to prove is a negative: that they did not start eating input the older
/// rules used to hand through to a task.
/// </summary>
public class CaptureVerbTests
{
    private static readonly DateTimeOffset Monday8am =
        new(2026, 8, 24, 8, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- arithmetic

    [Theory]
    [InlineData("2+2", "4")]
    [InlineData("23 * 1.2", "27.6")]
    [InlineData("(3+4)*2", "14")]
    [InlineData("10/4", "2.5")]
    [InlineData("2^10", "1024")]
    [InlineData("-5 + 8", "3")]
    [InlineData("7 % 3", "1")]
    public void Math_IsWorkedOut(string draft, string expected)
    {
        var intent = CaptureViewModel.Parse(draft, Monday8am);

        Assert.Equal(CaptureKind.Math, intent.Kind);
        Assert.Equal(expected, intent.Text);
    }

    /// <summary>
    /// Precedence, because a calculator that got this wrong would be worse than no calculator.
    /// </summary>
    [Fact]
    public void Math_RespectsPrecedence()
    {
        Assert.Equal("14", CaptureViewModel.Parse("2 + 3 * 4", Monday8am).Text);
    }

    /// <summary>The floating-point tail is hidden, or every other sum would look broken.</summary>
    [Fact]
    public void Math_RoundsAwayTheFloatingPointTail()
    {
        Assert.Equal("0.3", CaptureViewModel.Parse("0.1 + 0.2", Monday8am).Text);
    }

    /// <summary>
    /// The important half of the calculator: everything it refuses. A task list where "buy 2 x 4
    /// timber" silently became a number would be worse than having no calculator at all.
    /// </summary>
    [Theory]
    [InlineData("buy 2 x 4 timber")]
    [InlineData("25")]
    [InlineData("-5")]
    [InlineData("email tom re: q1+q2 numbers")]
    [InlineData("2 +")]
    [InlineData("()")]
    [InlineData("1/0")]
    public void Math_AnythingItCannotFullyConsume_StaysATask(string draft)
    {
        Assert.NotEqual(CaptureKind.Math, CaptureViewModel.Parse(draft, Monday8am).Kind);
    }

    /// <summary>A duration was claimed by the timer long before the calculator existed.</summary>
    [Fact]
    public void Math_DoesNotStealADuration()
    {
        Assert.Equal(CaptureKind.Timer, CaptureViewModel.Parse("1h30", Monday8am).Kind);
    }

    // ---------------------------------------------------------------- reminders

    [Fact]
    public void Reminder_LaterToday_IsToday()
    {
        var intent = CaptureViewModel.Parse("@9am call Tom", Monday8am);

        Assert.Equal(CaptureKind.Reminder, intent.Kind);
        Assert.Equal("call Tom", intent.Text);
        Assert.Equal(TimeSpan.FromHours(1), intent.Duration);
        Assert.Equal(24, intent.When.Day);
    }

    /// <summary>
    /// A time that has already gone means tomorrow. Nobody types a reminder for the past, and
    /// refusing one would just make them work out that they meant the next day.
    /// </summary>
    [Fact]
    public void Reminder_ATimeAlreadyPast_RollsToTomorrow()
    {
        var intent = CaptureViewModel.Parse("@7am standup", Monday8am);

        Assert.Equal(CaptureKind.Reminder, intent.Kind);
        Assert.Equal(25, intent.When.Day);
        Assert.Equal(TimeSpan.FromHours(23), intent.Duration);
    }

    [Theory]
    [InlineData("@17:30 gym", 17, 30)]
    [InlineData("@5:30pm gym", 17, 30)]
    [InlineData("@12am midnight", 0, 0)]
    [InlineData("@12pm lunch", 12, 0)]
    [InlineData("@9 something", 9, 0)]
    public void Reminder_UnderstandsTheUsualClockForms(string draft, int hour, int minute)
    {
        var intent = CaptureViewModel.Parse(draft, Monday8am);

        Assert.Equal(CaptureKind.Reminder, intent.Kind);
        Assert.Equal(hour, intent.When.Hour);
        Assert.Equal(minute, intent.When.Minute);
    }

    [Fact]
    public void Reminder_WithNoLabel_IsStillAReminder()
    {
        var intent = CaptureViewModel.Parse("@9am", Monday8am);

        Assert.Equal(CaptureKind.Reminder, intent.Kind);
        Assert.Equal(string.Empty, intent.Text);
    }

    /// <summary>
    /// The grammar must never eat input. An at-sign that is not followed by a clock is somebody
    /// writing a task, not somebody getting the syntax wrong.
    /// </summary>
    [Theory]
    [InlineData("@home buy milk")]
    [InlineData("@bob about the invoice")]
    [InlineData("@25:00 nonsense")]
    [InlineData("@9:99 nonsense")]
    public void Reminder_WithoutATime_FallsThroughToATask(string draft)
    {
        Assert.Equal(CaptureKind.Todo, CaptureViewModel.Parse(draft, Monday8am).Kind);
    }

    // ---------------------------------------------------------------- the timer behind a reminder

    [Fact]
    public void Reminder_StartsALabelledCountdown()
    {
        var todos = new TodosViewModel(new Core.Services.TodosStore(Temp()));
        var notes = new NotesViewModel(new Core.Services.NotesStore(Temp()));
        var capture = new CaptureViewModel(todos, notes);
        var timer = new TimerActivity();

        capture.DraftText = "@9am call Tom";
        capture.Submit(Monday8am, timer);

        Assert.True(timer.IsActive);
        Assert.True(timer.HasLabel);
        Assert.Equal("call Tom", timer.Label);

        // A named countdown announces its name rather than "Time's up" -- being said back at the
        // right moment is the whole reason it was set.
        timer.Tick(Monday8am.AddHours(1));
        Assert.Equal("call Tom", timer.RemainingText);
    }

    [Fact]
    public void Timer_WithoutALabel_StillSaysTimesUp()
    {
        var timer = new TimerActivity();
        timer.Start(Monday8am, TimeSpan.FromMinutes(25));

        timer.Tick(Monday8am.AddMinutes(25));

        Assert.False(timer.HasLabel);
        Assert.Equal("Time's up", timer.RemainingText);
    }

    private static string Temp() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
}
