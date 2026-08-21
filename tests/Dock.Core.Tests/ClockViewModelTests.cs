using System.Globalization;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class ClockViewModelTests
{
    private static readonly DateTime Noon = new(2026, 8, 21, 12, 34, 56);

    /// <summary>
    /// The strings are the user's own, so the assertions have to be too -- hard-coding "12:34"
    /// would pass on a machine set to a 24-hour clock and fail on the next one along.
    /// </summary>
    private static string Expected(DateTime at) =>
        at.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture);

    [Fact]
    public void Tick_ShowsTheTimeAndTheDate()
    {
        var clock = new ClockViewModel();

        clock.Tick(Noon);

        Assert.Equal(Expected(Noon), clock.TimeText);
        Assert.Equal("Fri 21 Aug", clock.DateText);
    }

    [Fact]
    public void Tick_RaisesNothingUntilTheMinuteTurns()
    {
        var clock = new ClockViewModel();
        clock.Tick(Noon);

        var changes = 0;
        clock.PropertyChanged += (_, _) => changes++;

        // Four ticks a second for the rest of the minute, all showing the same time.
        clock.Tick(Noon.AddSeconds(1));
        clock.Tick(Noon.AddSeconds(2));
        Assert.Equal(0, changes);

        clock.Tick(Noon.AddMinutes(1));
        Assert.NotEqual(0, changes);
        Assert.Equal(Expected(Noon.AddMinutes(1)), clock.TimeText);
    }

    [Fact]
    public void Tick_CrossesMidnightOntoTheNextDate()
    {
        var clock = new ClockViewModel();

        clock.Tick(new DateTime(2026, 8, 21, 23, 59, 30));
        Assert.Equal("Fri 21 Aug", clock.DateText);

        clock.Tick(new DateTime(2026, 8, 22, 0, 0, 5));
        Assert.Equal("Sat 22 Aug", clock.DateText);
    }
}
