using Dock.Core.Models;

namespace Dock.Core.Tests;

/// <summary>
/// Which screens the island ends up on, given three settings that can disagree.
///
/// All of the subtlety is in the fallbacks, which is why they are answered in one place rather than
/// at each of the three call sites. The rule that matters most: the answer is never empty. There is
/// no way to ask for no island at all, so unticking the last monitor leaves it on the primary
/// rather than losing it with no way back.
/// </summary>
public class IslandMonitorSelectionTests
{
    private static readonly string[] Attached = [@"\.\DISPLAY1", @"\.\DISPLAY2", @"\.\DISPLAY3"];

    [Fact]
    public void EffectiveMonitors_OnAllMonitors_TakesEveryAttachedScreen()
    {
        var settings = new AppSettings { IslandOnAllMonitors = true };

        Assert.Equal(Attached, settings.EffectiveMonitors(Attached));
    }

    /// <summary>
    /// All-monitors wins over the list rather than expanding into it, so a screen plugged in later
    /// is covered without anyone coming back to tick it.
    /// </summary>
    [Fact]
    public void EffectiveMonitors_OnAllMonitors_IgnoresTheChosenList()
    {
        var settings = new AppSettings
        {
            IslandOnAllMonitors = true,
            IslandMonitors = [@"\.\DISPLAY2"]
        };

        Assert.Equal(3, settings.EffectiveMonitors(Attached).Count);
    }

    [Fact]
    public void EffectiveMonitors_TakesTheChosenScreens()
    {
        var settings = new AppSettings
        {
            IslandMonitors = [@"\.\DISPLAY1", @"\.\DISPLAY3"]
        };

        Assert.Equal([@"\.\DISPLAY1", @"\.\DISPLAY3"], settings.EffectiveMonitors(Attached));
    }

    /// <summary>
    /// An unplugged screen is not an error and not a reason to fall back. The others are still
    /// perfectly good answers, and the choice is kept so that plugging it back in restores it.
    /// </summary>
    [Fact]
    public void EffectiveMonitors_SkipsAScreenThatIsNoLongerThere()
    {
        var settings = new AppSettings
        {
            IslandMonitors = [@"\.\DISPLAY1", @"\.\LAPTOP-DOCK"]
        };

        Assert.Equal([@"\.\DISPLAY1"], settings.EffectiveMonitors(Attached));
    }

    /// <summary>
    /// Every chosen screen gone means falling back rather than showing nothing, which is the case
    /// of a laptop unplugged from its dock.
    /// </summary>
    [Fact]
    public void EffectiveMonitors_FallsBackWhenEveryChosenScreenIsGone()
    {
        var settings = new AppSettings
        {
            IslandMonitors = [@"\.\LAPTOP-DOCK"],
            IslandMonitor = @"\.\DISPLAY2"
        };

        Assert.Equal([@"\.\DISPLAY2"], settings.EffectiveMonitors(Attached));
    }

    /// <summary>
    /// A settings file written before any of this existed. The single-monitor setting is the older
    /// way of saying the same thing, and has to keep working.
    /// </summary>
    [Fact]
    public void EffectiveMonitors_HonoursTheOlderSingleMonitorSetting()
    {
        var settings = new AppSettings { IslandMonitor = @"\.\DISPLAY3" };

        Assert.Equal([@"\.\DISPLAY3"], settings.EffectiveMonitors(Attached));
    }

    /// <summary>An empty device name is "follow the primary", not "no screen".</summary>
    [Fact]
    public void EffectiveMonitors_WithNothingSetAtAll_FollowsThePrimary()
    {
        var settings = new AppSettings();

        Assert.Equal([""], settings.EffectiveMonitors(Attached));
    }

    [Fact]
    public void EffectiveMonitors_NeverAnswersWithNothing()
    {
        AppSettings[] cases =
        [
            new(),
            new() { IslandOnAllMonitors = true },
            new() { IslandMonitors = [@"\.\GONE"] },
            new() { IslandMonitors = [] }
        ];

        foreach (var settings in cases)
        {
            Assert.NotEmpty(settings.EffectiveMonitors(Attached));
            Assert.NotEmpty(settings.EffectiveMonitors([]));
        }
    }

    /// <summary>The same screen ticked twice is still one island, not two stacked on each other.</summary>
    [Fact]
    public void EffectiveMonitors_DoesNotRepeatAScreen()
    {
        var settings = new AppSettings
        {
            IslandMonitors = [@"\.\DISPLAY1", @"\.\display1"]
        };

        Assert.Single(settings.EffectiveMonitors(Attached));
    }
}
