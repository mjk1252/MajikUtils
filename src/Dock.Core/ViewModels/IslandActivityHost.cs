using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Dock.Core.ViewModels;

/// <summary>
/// Decides which activities the island is showing.
///
/// Two slots, not one: <see cref="Primary"/> takes the pill and <see cref="Secondary"/> the bubble
/// beside it. Anything past the second waits -- two is what the form can hold before a third is
/// simply smaller than legible.
///
/// Selection is the highest <see cref="IslandPriority"/> among those still showing, ties broken by
/// whichever most recently *became* active. That last rule is what makes handover behave like an
/// island rather than a priority queue: a new claim of equal rank takes the pill, and when it goes
/// the one it displaced comes back rather than staying displaced.
/// </summary>
public sealed partial class IslandActivityHost : ObservableObject
{
    private readonly List<Slot> _slots = [];

    /// <summary>
    /// Stamped on each false-to-true edge, and the whole of what "most recently activated" means.
    /// Counted rather than timed because it only ever has to order two edges against each other,
    /// and a counter cannot tie the way two reads of a coarse clock can.
    /// </summary>
    private long _sequence;

    /// <summary>
    /// The last time handed to <see cref="Tick"/>, which is the only clock this class has. Deadlines
    /// are set from it rather than from <c>UtcNow</c> so that a test walking the clock forward moves
    /// every part of the class together.
    /// </summary>
    private DateTimeOffset _now = DateTimeOffset.MinValue;

    /// <summary>What the collapsed pill is showing.</summary>
    [ObservableProperty] private IIslandActivity? _primary;

    /// <summary>The runner-up. Null whenever fewer than two activities are showing.</summary>
    [ObservableProperty] private IIslandActivity? _secondary;

    /// <summary>
    /// Whether anything at all wants the island on screen. What the pointer poll asks, in place of
    /// the media session's own <c>HasSession</c>.
    /// </summary>
    [ObservableProperty] private bool _hasActivity;

    /// <summary>
    /// Adds an activity for the life of the application. There is no matching removal: activities
    /// are created at startup and last as long as the island does.
    /// </summary>
    public void Register(IIslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (_slots.Any(s => ReferenceEquals(s.Activity, activity)))
            return;

        var slot = new Slot(activity);
        if (activity.IsActive)
            slot.Activate(++_sequence);

        _slots.Add(slot);
        activity.PropertyChanged += OnActivityPropertyChanged;

        Reevaluate();
    }

    /// <summary>
    /// Advances the clock and retires anything whose linger has run out.
    ///
    /// Driven by a timer in the App layer rather than by one held here, so this class stays free of
    /// WPF and a test can walk time forward without waiting on anything.
    /// </summary>
    public void Tick(DateTimeOffset now)
    {
        _now = now;

        // Collected before anything is retired: Retire raises property changes of its own, and
        // walking the list while those re-enter is how a straightforward loop turns into a bug.
        List<IIslandActivity>? retiring = null;

        foreach (var slot in _slots)
        {
            if (slot.ExpireIfElapsed(now))
                (retiring ??= []).Add(slot.Activity);
        }

        if (retiring is null)
            return;

        Reevaluate();

        foreach (var activity in retiring)
            activity.Retire();
    }

    private void OnActivityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A null name is the convention for "assume everything changed", so it has to fall through
        // to the same handling rather than being filtered out with the properties we don't watch.
        if (e.PropertyName is not (null or nameof(IIslandActivity.IsActive) or nameof(IIslandActivity.Priority)))
            return;

        if (sender is not IIslandActivity activity)
            return;

        var slot = _slots.FirstOrDefault(s => ReferenceEquals(s.Activity, activity));
        if (slot is null)
            return;

        // Only the edges matter. An activity that republishes IsActive=true every second must not
        // keep restamping its activation order, or it would climb over everything of equal rank.
        var retireNow = false;

        if (activity.IsActive)
        {
            if (!slot.IsClaiming)
                slot.Activate(++_sequence);
        }
        else if (slot.IsClaiming)
        {
            var linger = activity.Linger;
            slot.Deactivate(linger > TimeSpan.Zero ? _now + linger : null);

            // Nothing to expire later, so this is the moment it is off the island.
            retireNow = !slot.IsShowing;
        }

        Reevaluate();

        // After Reevaluate, so the slot is released before the activity blanks itself -- retiring
        // first would show the empty version of it for one layout pass.
        if (retireNow)
            activity.Retire();
    }

    private void Reevaluate()
    {
        var showing = _slots
            .Where(s => s.IsShowing)
            .OrderByDescending(s => s.Activity.Priority)
            .ThenByDescending(s => s.ActivatedAt)
            .ToList();

        Primary = showing.Count > 0 ? showing[0].Activity : null;
        Secondary = showing.Count > 1 ? showing[1].Activity : null;
        HasActivity = Primary is not null;
    }

    /// <summary>
    /// One activity's standing with the host: whether it is claiming a slot, when it last started
    /// doing so, and when its linger runs out.
    ///
    /// The claiming flag tracks what the host last *saw*, which is not the same as the activity's
    /// current <see cref="IIslandActivity.IsActive"/> -- telling an edge from a repeat needs both.
    /// </summary>
    private sealed class Slot(IIslandActivity activity)
    {
        private DateTimeOffset? _lingerUntil;

        public IIslandActivity Activity { get; } = activity;

        /// <summary>Whether the activity was active as of the last change the host saw.</summary>
        public bool IsClaiming { get; private set; }

        public long ActivatedAt { get; private set; }

        /// <summary>Active, or inside the linger window that follows going inactive.</summary>
        public bool IsShowing => IsClaiming || _lingerUntil is not null;

        public void Activate(long sequence)
        {
            IsClaiming = true;
            ActivatedAt = sequence;

            // Coming back inside its own linger window cancels the expiry outright: this is the
            // flap the window exists for, and it should leave no trace.
            _lingerUntil = null;
        }

        public void Deactivate(DateTimeOffset? lingerUntil)
        {
            IsClaiming = false;
            _lingerUntil = lingerUntil;
        }

        public bool ExpireIfElapsed(DateTimeOffset now)
        {
            if (_lingerUntil is not { } until || now < until)
                return false;

            _lingerUntil = null;
            return true;
        }
    }
}
