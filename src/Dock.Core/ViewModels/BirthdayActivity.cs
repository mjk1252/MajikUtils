using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;

namespace Dock.Core.ViewModels;

/// <summary>
/// Somebody's birthday, on the island, all day.
///
/// The first activity here that is neither a moment nor a reading: an announcement is two and a
/// half seconds, a condition is polled and a track changes on its own, and this is a fact about
/// today that stays true until it is acknowledged. That shape is why it is the only thing in the
/// app at <see cref="IslandPriority.Alert"/> -- and why it has a Dismiss button, since an activity
/// nothing retires needs a way to be told it has been seen.
///
/// Dismissal is per day rather than permanent. Next year's is a different birthday, and so is
/// tomorrow's for somebody else, so what is remembered is the single date this was last dismissed
/// on -- one line in settings, and no list of acknowledgements to garbage-collect.
/// </summary>
public sealed partial class BirthdayActivity : ObservableObject, IIslandActivity
{
    private IReadOnlyList<Birthday> _all = [];
    private DateOnly _today;

    /// <summary>Whose birthday it is today, in the order the file listed them.</summary>
    public ObservableCollection<BirthdayItemViewModel> Today { get; } = [];

    /// <summary>
    /// Whether today has more than one birthday on it, which is the only case where the expanded
    /// row names them individually -- with one, the headline has already said the name.
    ///
    /// A property here rather than a count threshold in the view, because a binding to
    /// <c>Today.Count</c> would need a converter that takes a number to compare against, and the
    /// question "is this the several case" belongs to the activity either way.
    /// </summary>
    public bool HasSeveral => Today.Count > 1;

    [ObservableProperty] private bool _isActive;

    /// <summary>
    /// The pill's line: a name if there is one, a count if there are several. Held as text rather
    /// than assembled in the template, because "Ada is 47 today" and "3 birthdays today" are not
    /// the same sentence with different bindings in it.
    /// </summary>
    [ObservableProperty] private string _headline = string.Empty;

    /// <summary>
    /// The date this was last dismissed on, or null. Round-tripped through settings so that
    /// dismissing a birthday and then restarting does not bring it straight back -- which would
    /// make the button read as decoration.
    /// </summary>
    public DateOnly? DismissedOn { get; private set; }

    /// <summary>
    /// Raised when the user dismisses, so the App can write the date out. An event rather than a
    /// store reference, for the same reason nothing else in this project has one: no file IO here.
    /// </summary>
    public event Action<DateOnly>? Dismissed;

    /// <summary>
    /// Raised on the edge where the island goes from having no birthday to having one -- the moment
    /// the confetti should start, and deliberately not the same thing as <see cref="IsActive"/>
    /// going true. Coming back on screen after a hover does not re-throw the confetti; a new day
    /// with a new birthday on it does.
    /// </summary>
    public event Action? Celebrated;

    /// <summary>The cake, which is the whole of the compact form and most of the point.</summary>
    public const string Cake = "\U0001F382";

    public string Key => "birthday";

    /// <summary>
    /// The only Alert in the app, and the only thing that outranks a playing track.
    ///
    /// Everything else here was built to stay out of the music's way -- a timer draws a ring rather
    /// than take the pill, a condition is a dot on principle. This one is the exception the ladder
    /// was built with a top rung for: it happens once a year, it is the reason somebody added the
    /// feature, and it is dismissed by hand rather than by a clock.
    /// </summary>
    public IslandPriority Priority => IslandPriority.Alert;

    /// <summary>
    /// Nothing to protect. This does not flap -- it changes at midnight and when a button is
    /// pressed, and both of those are meant to take effect immediately.
    /// </summary>
    public TimeSpan Linger => TimeSpan.Zero;

    /// <summary>Restores the dismissal recorded in settings, without raising the event that writes it.</summary>
    public void RestoreDismissal(DateOnly? dismissedOn) => DismissedOn = dismissedOn;

    private bool _enabled = true;

    /// <summary>
    /// Whether a birthday is allowed to claim the island at all. Set from Settings.
    ///
    /// Separate from dismissal, and that separation is the point: a dismissal is "seen it, thanks"
    /// and expires at midnight, while this is "never interrupt me" and does not. Folding the toggle
    /// into the dismissal date would have meant switching it off silently un-switching itself the
    /// following morning.
    ///
    /// The Birthdays scope is unaffected either way -- a countdown list is a place you go to, and
    /// this only governs whether one is allowed to come to you.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;

            _enabled = value;
            Refresh();
        }
    }

    /// <summary>
    /// Hands over the whole list and the date to read it against.
    ///
    /// Takes the entire list rather than today's matches so that the day rollover is this class's
    /// business: <see cref="Tick"/> can re-answer the question at midnight without anybody else
    /// having to notice the date changed.
    /// </summary>
    public void Apply(IReadOnlyList<Birthday> birthdays, DateOnly today)
    {
        _all = birthdays;
        _today = today;
        Refresh();
    }

    /// <summary>
    /// Re-reads the date. Driven by the App's activity clock like everything else here, so a
    /// machine left running overnight finds the birthday in the morning rather than at the next
    /// restart -- which, for a feature that is only ever right for one day, is the difference
    /// between working and not.
    /// </summary>
    public void Tick(DateOnly today)
    {
        if (today == _today)
            return;

        _today = today;
        Refresh();
    }

    private void Refresh()
    {
        var celebrants = _all.Where(b => b.IsOn(_today)).ToList();

        Today.Clear();
        foreach (var birthday in celebrants)
            Today.Add(new BirthdayItemViewModel(birthday, _today));

        OnPropertyChanged(nameof(HasSeveral));

        Headline = celebrants.Count switch
        {
            0 => string.Empty,
            1 => Today[0].TurningAge is { } age
                ? $"{Today[0].Name} is {age} today"
                : $"It's {Today[0].Name}'s birthday",
            var count => $"{count} birthdays today"
        };

        // A dismissal only covers the day it was made on. Left alone it would also swallow the
        // following morning's, since the flag would still be set when the date rolled over.
        if (DismissedOn is { } dismissed && dismissed != _today)
            DismissedOn = null;

        var wanted = _enabled && celebrants.Count > 0 && DismissedOn != _today;
        if (wanted == IsActive)
            return;

        IsActive = wanted;

        if (wanted)
            Celebrated?.Invoke();
    }

    /// <summary>
    /// Acknowledges today's birthdays, and takes the island back.
    ///
    /// The one activity here with an explicit off switch, because it is the one with no clock
    /// behind it. Everything else expires; this waits.
    /// </summary>
    [RelayCommand]
    public void Dismiss()
    {
        DismissedOn = _today;
        IsActive = false;
        Dismissed?.Invoke(_today);
    }

    /// <summary>
    /// Deliberately empty. The pill's text has to survive being retired -- the same reason
    /// <see cref="MediaViewModel"/> keeps the track through the gap between two songs -- and there
    /// is nothing else here that is not recomputed on the next <see cref="Refresh"/> anyway.
    /// </summary>
    public void Retire()
    {
    }
}
