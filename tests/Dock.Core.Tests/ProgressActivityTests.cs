using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class ProgressActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Report_StartsIt()
    {
        var progress = new ProgressActivity();

        Assert.False(progress.IsActive);

        progress.Report("Installing VS Code", null);

        Assert.True(progress.IsActive);
        Assert.Equal("Installing VS Code", progress.Label);
    }

    /// <summary>
    /// Work with no figure yet draws a quiet ring rather than claiming to be 0% done, which is the
    /// common case at the start of every job.
    /// </summary>
    [Fact]
    public void Report_WithoutAFraction_IsIndeterminate()
    {
        var progress = new ProgressActivity();
        progress.Report("Downloading", null);

        Assert.True(progress.IsIndeterminate);
        Assert.Equal("Working", progress.ProgressText);
    }

    [Fact]
    public void Report_WithAFraction_SaysHowFar()
    {
        var progress = new ProgressActivity();
        progress.Report("Copying", 0.42);

        Assert.False(progress.IsIndeterminate);
        Assert.Equal("42%", progress.ProgressText);
    }

    [Theory]
    [InlineData(-1.0, "0%")]
    [InlineData(2.0, "100%")]
    public void Report_ClampsNonsense(double fraction, string expected)
    {
        var progress = new ProgressActivity();
        progress.Report("Copying", fraction);

        Assert.Equal(expected, progress.ProgressText);
    }

    /// <summary>
    /// Finishing is something the island says, not something it silently stops saying -- so the
    /// job stays up for a few seconds after it is done.
    /// </summary>
    [Fact]
    public void Finish_StaysUpBriefly_ThenGoes()
    {
        var progress = new ProgressActivity();
        progress.Report("Installing VS Code", 0.9);

        progress.Finish(Now, "Installed VS Code");

        Assert.True(progress.IsActive);
        Assert.True(progress.IsFinished);
        Assert.Equal("100%", progress.ProgressText);

        progress.Tick(Now.AddSeconds(3));
        Assert.True(progress.IsActive);

        progress.Tick(Now.AddSeconds(9));
        Assert.False(progress.IsActive);
    }

    /// <summary>A job that never started has nothing to finish, and must not appear in order to.</summary>
    [Fact]
    public void Finish_WithoutAJob_DoesNothing()
    {
        var progress = new ProgressActivity();

        progress.Finish(Now, "Installed something");

        Assert.False(progress.IsActive);
    }

    [Fact]
    public void Cancel_TakesItOffAtOnce()
    {
        var progress = new ProgressActivity();
        progress.Report("Installing", 0.5);

        progress.Cancel();

        Assert.False(progress.IsActive);
    }

    /// <summary>
    /// Retire is the host saying the activity is genuinely gone. Clearing display state before then
    /// would blank the pill for the whole linger window.
    /// </summary>
    [Fact]
    public void Retire_ClearsWhatWasDrawn()
    {
        var progress = new ProgressActivity();
        progress.Report("Installing VS Code", 0.5);
        progress.Cancel();

        progress.Retire();

        Assert.Equal(string.Empty, progress.Label);
        Assert.Equal(0, progress.Progress);
        Assert.True(progress.IsIndeterminate);
    }

    /// <summary>
    /// Below music, like the timer and for the same reason: a ring says how far along a job is
    /// without words, so evicting a chosen track to spell out a number is a bad trade.
    /// </summary>
    [Fact]
    public void Priority_SitsInTheBubbleRatherThanTakingThePill()
    {
        Assert.Equal(IslandPriority.Background, new ProgressActivity().Priority);
    }
}
