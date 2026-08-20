using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dock.Core.ViewModels;

/// <summary>
/// A countdown on the island.
///
/// The only activity here that owes nothing to the operating system: no registry key, no COM
/// interface, no watcher. It also happens to be the one that makes the island feel like an island
/// rather than a now-playing widget, which is a fair summary of how much the platform had to do
/// with any of it.
///
/// The first activity with a *live* value, which is what makes it worth the progress ring in the
/// bubble: everything else so far is either static for its whole life or a dot.
/// </summary>
public sealed partial class TimerActivity : ObservableObject, IIslandActivity
{
    /// <summary>
    /// How long a finished timer stays on the island announcing itself before it goes. It has no
    /// sound and no toast, so this is the whole of the notification.
    /// </summary>
    private static readonly TimeSpan FinishedDwell = TimeSpan.FromSeconds(30);

    private DateTimeOffset _endsAt;
    private DateTimeOffset _finishedAt;

    [ObservableProperty] private bool _isActive;

    /// <summary>Whether the countdown has reached zero and is waiting to be acknowledged.</summary>
    [ObservableProperty] private bool _isFinished;

    [ObservableProperty] private string _remainingText = string.Empty;

    /// <summary>
    /// What the countdown is for, when it is for something. Empty for a plain timer, which is most
    /// of them -- twenty-five minutes is its own explanation.
    ///
    /// It exists because a reminder is a countdown with a name on it, and that is the whole of the
    /// difference: "call Tom at nine" needs no second activity, no second ring and no second set of
    /// templates, only a line of text the timer did not previously carry.
    /// </summary>
    [ObservableProperty] private string _label = string.Empty;

    /// <summary>Whether this countdown is for something named.</summary>
    public bool HasLabel => Label.Length > 0;

    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(HasLabel));

    /// <summary>0 at the start, 1 at the end. What the ring in the bubble draws.</summary>
    [ObservableProperty] private double _progress;

    private TimeSpan _duration;

    public string Key => "timer";

    /// <summary>
    /// Below music, for the same reason the camera indicator is: a ring says everything a timer has
    /// to say, and it says it for twenty-five minutes at a stretch. Evicting a track the user chose
    /// in order to spell out a number they can read off an arc is a bad trade, however deliberately
    /// the timer was started.
    ///
    /// Which is what makes this the activity the ring was built for -- it is the one that actually
    /// lives in the bubble. With nothing playing it takes the pill and shows the time in words,
    /// because then there is nothing to take it from.
    /// </summary>
    public IslandPriority Priority => IslandPriority.Background;

    /// <summary>Nothing here flaps: it is started and stopped by hand.</summary>
    public TimeSpan Linger => TimeSpan.Zero;

    /// <summary>Starts, or restarts, the countdown.</summary>
    public void Start(DateTimeOffset now, TimeSpan duration, string label = "")
    {
        if (duration <= TimeSpan.Zero)
            return;

        _duration = duration;
        _endsAt = now + duration;
        Label = label;
        IsFinished = false;
        IsActive = true;

        Tick(now);
    }

    /// <summary>
    /// Starts a countdown that ends at a given moment rather than after a given length. The same
    /// timer either way -- only the arithmetic to get there differs, and doing it here keeps every
    /// caller from having to.
    /// </summary>
    public void StartAt(DateTimeOffset now, DateTimeOffset when, string label = "") =>
        Start(now, when - now, label);

    [RelayCommand]
    public void Cancel()
    {
        IsActive = false;
        IsFinished = false;
    }

    /// <summary>
    /// Runs the clock forward. Driven by the App's activity tick rather than a timer of its own,
    /// for the same reason as everything else here: no WPF in this project, and a test that can
    /// jump thirty minutes without waiting for them.
    /// </summary>
    public void Tick(DateTimeOffset now)
    {
        if (!IsActive)
            return;

        var remaining = _endsAt - now;

        if (remaining <= TimeSpan.Zero)
        {
            if (!IsFinished)
            {
                IsFinished = true;
                _finishedAt = now;
            }

            Progress = 1;

            // A named countdown announces its name. "Time's up" is the right thing for a plain
            // timer and useless for a reminder, which was set precisely so that the thing would be
            // said back at the right moment.
            RemainingText = HasLabel ? Label : "Time's up";

            // Goes on its own eventually, so a timer that finished while nobody was looking does
            // not sit on the island until the next restart.
            if (now - _finishedAt >= FinishedDwell)
                Cancel();

            return;
        }

        Progress = _duration > TimeSpan.Zero
            ? Math.Clamp(1 - remaining / _duration, 0, 1)
            : 0;

        // Rounded up, so a timer started for one minute reads "1:00" rather than "0:59".
        RemainingText = Format(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)));
    }

    public void Retire()
    {
        RemainingText = string.Empty;
        Label = string.Empty;
        Progress = 0;
    }

    private static string Format(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
}
