using System.ComponentModel;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class IslandActivityHostTests
{
    /// <summary>
    /// An arbitrary fixed point. The host's only clock is what Tick hands it, so the tests never
    /// touch the wall clock and the linger cases are exact rather than approximate.
    /// </summary>
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NothingActive_ShowsNothing()
    {
        var host = Started();
        host.Register(new FakeActivity("a"));

        Assert.Null(host.Primary);
        Assert.Null(host.Secondary);
        Assert.False(host.HasActivity);
    }

    [Fact]
    public void OneActive_TakesPrimaryAndLeavesSecondaryEmpty()
    {
        var host = Started();
        var activity = new FakeActivity("a") { IsActive = true };

        host.Register(activity);

        Assert.Same(activity, host.Primary);
        Assert.Null(host.Secondary);
        Assert.True(host.HasActivity);
    }

    [Fact]
    public void AlreadyActiveAtRegistration_IsPickedUp()
    {
        var host = Started();

        // Registration order is startup order, not activation order: the media session may well
        // have published a track before the host was built.
        var activity = new FakeActivity("a") { IsActive = true };
        host.Register(activity);

        Assert.Same(activity, host.Primary);
    }

    [Fact]
    public void HigherPriority_TakesPrimaryAndDemotesTheIncumbent()
    {
        var host = Started();
        var media = new FakeActivity("media") { IsActive = true };
        var status = new FakeActivity("status", IslandPriority.Status);
        host.Register(media);
        host.Register(status);

        status.IsActive = true;

        // The whole point of two slots: a call starting does not throw the music away, it moves it
        // over.
        Assert.Same(status, host.Primary);
        Assert.Same(media, host.Secondary);
    }

    [Fact]
    public void HigherPriorityLeaving_PromotesTheIncumbentBack()
    {
        var host = Started();
        var media = new FakeActivity("media") { IsActive = true };
        var status = new FakeActivity("status", IslandPriority.Status) { IsActive = true };
        host.Register(media);
        host.Register(status);

        status.IsActive = false;
        host.Tick(Start + status.Linger + TimeSpan.FromSeconds(1));

        Assert.Same(media, host.Primary);
        Assert.Null(host.Secondary);
    }

    [Fact]
    public void EqualPriority_MostRecentlyActivatedTakesPrimary()
    {
        var host = Started();
        var first = new FakeActivity("first") { IsActive = true };
        var second = new FakeActivity("second");
        host.Register(first);
        host.Register(second);

        second.IsActive = true;

        Assert.Same(second, host.Primary);
        Assert.Same(first, host.Secondary);
    }

    [Fact]
    public void RepeatedActivation_DoesNotRestampTheOrder()
    {
        var host = Started();
        var first = new FakeActivity("first") { IsActive = true };
        var second = new FakeActivity("second");
        host.Register(first);
        host.Register(second);

        second.IsActive = true;

        // A source that republishes "still active" must not climb over what already outranks it.
        first.RaiseChanged(nameof(IIslandActivity.IsActive));

        Assert.Same(second, host.Primary);
    }

    [Fact]
    public void ThreeActive_LeavesTheThirdWaiting()
    {
        var host = Started();
        var ambient = new FakeActivity("ambient") { IsActive = true };
        var status = new FakeActivity("status", IslandPriority.Status) { IsActive = true };
        var transient = new FakeActivity("transient", IslandPriority.Transient) { IsActive = true };
        host.Register(ambient);
        host.Register(status);
        host.Register(transient);

        Assert.Same(transient, host.Primary);
        Assert.Same(status, host.Secondary);

        transient.IsActive = false;
        host.Tick(Start + transient.Linger + TimeSpan.FromSeconds(1));

        Assert.Same(status, host.Primary);
        Assert.Same(ambient, host.Secondary);
    }

    [Fact]
    public void GoingInactive_HoldsTheSlotUntilTheLingerElapses()
    {
        var host = Started();
        var activity = new FakeActivity("a") { IsActive = true };
        host.Register(activity);

        activity.IsActive = false;

        // The gap between two tracks. Still showing, because nothing has expired it yet.
        Assert.Same(activity, host.Primary);

        host.Tick(Start + TimeSpan.FromMilliseconds(500));
        Assert.Same(activity, host.Primary);

        host.Tick(Start + TimeSpan.FromMilliseconds(1600));
        Assert.Null(host.Primary);
        Assert.False(host.HasActivity);
    }

    [Fact]
    public void ReactivatingInsideTheLingerWindow_CancelsTheExpiry()
    {
        var host = Started();
        var activity = new FakeActivity("a") { IsActive = true };
        host.Register(activity);

        activity.IsActive = false;
        host.Tick(Start + TimeSpan.FromMilliseconds(500));
        activity.IsActive = true;

        // Well past the original deadline: the flap should have left no trace of it.
        host.Tick(Start + TimeSpan.FromSeconds(30));

        Assert.Same(activity, host.Primary);
    }

    [Fact]
    public void ZeroLinger_GoesOnTheEdgeWithoutWaitingForATick()
    {
        var host = Started();
        var activity = new FakeActivity("a") { Linger = TimeSpan.Zero, IsActive = true };
        host.Register(activity);

        activity.IsActive = false;

        Assert.Null(host.Primary);
        Assert.Equal(1, activity.RetireCount);
    }

    [Fact]
    public void LingerElapsing_RetiresTheActivityExactlyOnce()
    {
        var host = Started();
        var activity = new FakeActivity("a") { IsActive = true };
        host.Register(activity);

        activity.IsActive = false;
        host.Tick(Start + TimeSpan.FromMilliseconds(500));

        // Still inside the window: clearing here is what would blank the pill between two tracks.
        Assert.Equal(0, activity.RetireCount);

        host.Tick(Start + TimeSpan.FromSeconds(3));
        host.Tick(Start + TimeSpan.FromSeconds(4));

        Assert.Equal(1, activity.RetireCount);
    }

    [Fact]
    public void ReactivatingInsideTheLingerWindow_NeverRetires()
    {
        var host = Started();
        var activity = new FakeActivity("a") { IsActive = true };
        host.Register(activity);

        activity.IsActive = false;
        activity.IsActive = true;
        host.Tick(Start + TimeSpan.FromSeconds(30));

        // The track never left the pill, so it must never have been told to clear itself.
        Assert.Equal(0, activity.RetireCount);
    }

    [Fact]
    public void LingeringPrimary_KeepsItsSlotAheadOfTheSecondary()
    {
        var host = Started();
        var media = new FakeActivity("media") { IsActive = true };
        var status = new FakeActivity("status", IslandPriority.Status) { IsActive = true };
        host.Register(media);
        host.Register(status);

        status.IsActive = false;

        // Without this the pair would swap the instant the higher one blinked and swap back a
        // moment later, which is the exact churn the linger window exists to prevent.
        Assert.Same(status, host.Primary);
        Assert.Same(media, host.Secondary);

        host.Tick(Start + TimeSpan.FromSeconds(3));

        Assert.Same(media, host.Primary);
        Assert.Null(host.Secondary);
    }

    [Fact]
    public void PriorityChanging_ReordersTheSlots()
    {
        var host = Started();
        var first = new FakeActivity("first") { IsActive = true };
        var second = new FakeActivity("second") { IsActive = true };
        host.Register(first);
        host.Register(second);

        Assert.Same(second, host.Primary);

        first.Priority = IslandPriority.Alert;

        Assert.Same(first, host.Primary);
        Assert.Same(second, host.Secondary);
    }

    [Fact]
    public void RegisteringTwice_IsIgnored()
    {
        var host = Started();
        var activity = new FakeActivity("a") { IsActive = true };

        host.Register(activity);
        host.Register(activity);

        // A second registration would otherwise fill both slots with the same activity.
        Assert.Same(activity, host.Primary);
        Assert.Null(host.Secondary);
    }

    /// <summary>A host whose clock has been set, which is what the App does at startup.</summary>
    private static IslandActivityHost Started()
    {
        var host = new IslandActivityHost();
        host.Tick(Start);
        return host;
    }

    private sealed class FakeActivity(string key, IslandPriority priority = IslandPriority.Ambient)
        : IIslandActivity
    {
        private bool _isActive;
        private IslandPriority _priority = priority;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get; } = key;

        public TimeSpan Linger { get; init; } = TimeSpan.FromMilliseconds(1500);

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;

                _isActive = value;
                RaiseChanged(nameof(IsActive));
            }
        }

        public IslandPriority Priority
        {
            get => _priority;
            set
            {
                if (_priority == value)
                    return;

                _priority = value;
                RaiseChanged(nameof(Priority));
            }
        }

        public int RetireCount { get; private set; }

        public void Retire() => RetireCount++;

        public void RaiseChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
