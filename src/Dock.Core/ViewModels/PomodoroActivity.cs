using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dock.Core.ViewModels;

/// <summary>Which part of the cycle is running.</summary>
public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak
}

/// <summary>
/// The pomodoro cycle: focus, a short break, and a longer one every fourth round.
///
/// A separate activity rather than a mode on <see cref="TimerActivity"/>, and the distinction is
/// the whole reason it is worth writing. A timer counts one interval down and stops. A pomodoro is
/// a *rhythm* -- it knows what comes next, it knows how many rounds you have done, and its whole
/// value is that you never have to start the next one. Folding that into a class whose job is "one
/// countdown, then nothing" would have made both harder to read.
///
/// The lengths are not configurable, which is a decision rather than an omission. 25/5/15 is what
/// the technique *is*; a pomodoro you have to set up is a timer with extra steps, and the island
/// already has a very good timer.
/// </summary>
public sealed partial class PomodoroActivity : ObservableObject, IIslandActivity
{
    public static readonly TimeSpan FocusLength = TimeSpan.FromMinutes(25);
    public static readonly TimeSpan ShortBreakLength = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LongBreakLength = TimeSpan.FromMinutes(15);

    /// <summary>How many focus rounds earn the long break.</summary>
    public const int RoundsPerSet = 4;

    private DateTimeOffset _phaseEndsAt;
    private TimeSpan _phaseLength;

    [ObservableProperty] private bool _isActive;

    [ObservableProperty] private PomodoroPhase _phase = PomodoroPhase.Focus;

    /// <summary>0 at the start of the current phase, 1 at its end. What the ring draws.</summary>
    [ObservableProperty] private double _progress;

    [ObservableProperty] private string _remainingText = string.Empty;

    /// <summary>
    /// Focus rounds finished in this set, 0 to <see cref="RoundsPerSet"/>. Resets after the long
    /// break, because that is what makes it a set rather than a tally.
    /// </summary>
    [ObservableProperty] private int _completedRounds;

    /// <summary>Total focus rounds since it was started. The number worth being pleased about.</summary>
    [ObservableProperty] private int _totalRounds;

    /// <summary>
    /// The set as four marks, filled as they are earned. A number would be smaller and worse: the
    /// point of a set is that you can see how much of it is left without doing arithmetic, and four
    /// dots is the only readout on the island that needs no units.
    /// </summary>
    public string RoundDots => string.Concat(
        Enumerable.Range(0, RoundsPerSet).Select(i => i < CompletedRounds ? '●' : '○'));

    partial void OnCompletedRoundsChanged(int value) => OnPropertyChanged(nameof(RoundDots));

    public string PhaseLabel => Phase switch
    {
        PomodoroPhase.Focus => "Focus",
        PomodoroPhase.ShortBreak => "Break",
        _ => "Long break"
    };

    /// <summary>Whether the current phase is a break, which is all the templates need to recolour.</summary>
    public bool IsBreak => Phase != PomodoroPhase.Focus;

    partial void OnPhaseChanged(PomodoroPhase value)
    {
        OnPropertyChanged(nameof(PhaseLabel));
        OnPropertyChanged(nameof(IsBreak));
    }

    public string Key => "pomodoro";

    /// <summary>
    /// Below music, like every other activity with a ring. A pomodoro runs for two hours at a
    /// stretch, and evicting a track somebody chose for that whole time -- to show a number they
    /// can read off an arc -- would be the worst version of this feature.
    /// </summary>
    public IslandPriority Priority => IslandPriority.Background;

    /// <summary>Nothing here flaps: it is started and stopped by hand.</summary>
    public TimeSpan Linger => TimeSpan.Zero;

    /// <summary>Starts a fresh set at the first focus round.</summary>
    public void Start(DateTimeOffset now)
    {
        CompletedRounds = 0;
        TotalRounds = 0;
        Begin(PomodoroPhase.Focus, now);
        IsActive = true;
    }

    /// <summary>
    /// Moves to whatever comes next without waiting for the clock. The one control the cycle needs
    /// beyond stopping: a break you do not want is far more common than a focus round you want to
    /// cut short, and either way the alternative is watching a bar you have stopped caring about.
    /// </summary>
    [RelayCommand]
    public void Skip() => Advance(DateTimeOffset.UtcNow);

    [RelayCommand]
    public void Stop()
    {
        IsActive = false;
    }

    /// <summary>
    /// Runs the clock forward, rolling into the next phase when one ends. Driven by the App's
    /// activity tick rather than a timer of its own, for the same reason as everything else here:
    /// no WPF in this project, and a test that can jump two hours without waiting for them.
    /// </summary>
    public void Tick(DateTimeOffset now)
    {
        if (!IsActive)
            return;

        // A loop rather than one step, so a machine that slept through three phases lands where it
        // should have rather than three ticks later.
        while (now >= _phaseEndsAt)
            Advance(_phaseEndsAt);

        var remaining = _phaseEndsAt - now;

        Progress = _phaseLength > TimeSpan.Zero
            ? Math.Clamp(1 - remaining / _phaseLength, 0, 1)
            : 0;

        // Rounded up, so a phase with one second left reads 0:01 rather than 0:00 for a whole second.
        RemainingText = Format(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)));
    }

    /// <summary>
    /// The cycle itself: focus earns a break, and every fourth one earns the long break.
    ///
    /// Both directions advance on their own. Auto-starting the break is obvious -- you have just
    /// earned it and pressing a button first is a tax on having concentrated. Auto-starting the
    /// *next focus round* is the less obvious half, and it is the point of the technique: the
    /// rhythm is the thing that works, and a cycle that waits to be told to continue is a stack of
    /// timers you have to keep feeding.
    /// </summary>
    private void Advance(DateTimeOffset at)
    {
        if (Phase == PomodoroPhase.Focus)
        {
            TotalRounds++;
            CompletedRounds++;

            var earnedLongBreak = CompletedRounds >= RoundsPerSet;
            Begin(earnedLongBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak, at);
            return;
        }

        // The long break closes the set, so the dots start again.
        if (Phase == PomodoroPhase.LongBreak)
            CompletedRounds = 0;

        Begin(PomodoroPhase.Focus, at);
    }

    private void Begin(PomodoroPhase phase, DateTimeOffset at)
    {
        Phase = phase;

        _phaseLength = phase switch
        {
            PomodoroPhase.Focus => FocusLength,
            PomodoroPhase.ShortBreak => ShortBreakLength,
            _ => LongBreakLength
        };

        _phaseEndsAt = at + _phaseLength;
        Progress = 0;
        RemainingText = Format(_phaseLength);
    }

    public void Retire()
    {
        RemainingText = string.Empty;
        Progress = 0;
        Phase = PomodoroPhase.Focus;
        CompletedRounds = 0;
    }

    private static string Format(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
}
