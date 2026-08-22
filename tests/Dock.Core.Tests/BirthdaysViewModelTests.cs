using Dock.Core.Models;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

/// <summary>
/// The countdown scope: the order it puts people in, and the words it puts on the wait.
/// </summary>
public class BirthdaysViewModelTests
{
    private static readonly DateOnly March14 = new(2026, 3, 14);

    /// <summary>
    /// The only order that answers the question the panel is opened to answer. A list running
    /// January to December puts whoever is next somewhere in the middle of it.
    /// </summary>
    [Fact]
    public void ItSortsByHowSoon_NotByDate()
    {
        var model = new BirthdaysViewModel();

        model.Apply(
        [
            new Birthday("January", 1, 5, null),
            new Birthday("Today", 3, 14, null),
            new Birthday("April", 4, 2, null)
        ], March14);

        Assert.Equal(["Today", "April", "January"], model.Upcoming.Select(b => b.Name));
        Assert.Equal("Today", model.Next?.Name);
    }

    /// <summary>Two people on one day come out the same way every time, not in file order.</summary>
    [Fact]
    public void SameDay_TiesBreakByName()
    {
        var model = new BirthdaysViewModel();

        model.Apply(
        [
            new Birthday("Zoe", 3, 20, null),
            new Birthday("Adam", 3, 20, null)
        ], March14);

        Assert.Equal(["Adam", "Zoe"], model.Upcoming.Select(b => b.Name));
    }

    [Fact]
    public void AnEmptyList_SaysSo()
    {
        var model = new BirthdaysViewModel();
        model.Apply([], March14);

        Assert.True(model.IsEmpty);
        Assert.Null(model.Next);
    }

    /// <summary>
    /// The countdown is in the units a person would use out loud. "In 287 days" is a number nobody
    /// converts into a feeling about how soon something is.
    /// </summary>
    [Theory]
    [InlineData(3, 14, "Today")]
    [InlineData(3, 15, "Tomorrow")]
    [InlineData(3, 20, "In 6 days")]
    [InlineData(4, 14, "In 4 weeks")]
    [InlineData(9, 14, "In 6 months")]
    public void TheCountdown_ReadsInSensibleUnits(int month, int day, string expected)
    {
        var item = new BirthdayItemViewModel(new Birthday("Ada", month, day, null), March14);

        Assert.Equal(expected, item.CountdownText);
    }

    [Fact]
    public void TheAgeLine_IsOnlyThereWithAYear()
    {
        var withYear = new BirthdayItemViewModel(new Birthday("Ada", 3, 14, 1990), March14);
        var without = new BirthdayItemViewModel(new Birthday("Mum", 3, 14, null), March14);

        Assert.True(withYear.HasAge);
        Assert.Equal("turns 36", withYear.AgeText);

        Assert.False(without.HasAge);
        Assert.Equal("", without.AgeText);
    }

    /// <summary>
    /// The tick is on a 250ms timer, so it has to be free when nothing has changed -- and has to
    /// actually rebuild when the day turns, or the morning of a birthday still reads "Tomorrow".
    /// </summary>
    [Fact]
    public void Tick_RebuildsOnlyWhenTheDayTurns()
    {
        var model = new BirthdaysViewModel();
        List<Birthday> birthdays = [new("Ada", 3, 15, null)];

        model.Apply(birthdays, March14);
        var before = model.Upcoming[0];

        model.Tick(birthdays, March14);
        Assert.Same(before, model.Upcoming[0]);

        model.Tick(birthdays, new DateOnly(2026, 3, 15));
        Assert.Equal("Today", model.Upcoming[0].CountdownText);
    }
}
