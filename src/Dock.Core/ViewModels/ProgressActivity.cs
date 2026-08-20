using CommunityToolkit.Mvvm.ComponentModel;

namespace Dock.Core.ViewModels;

/// <summary>
/// Something long enough to be worth watching: an install, a copy, a download.
///
/// The island already had one activity with a live value in it -- the timer, whose ring in the
/// bubble says how far along it is without a single word. This is that shape made general, because
/// the timer was never the only thing with a number between nought and one.
///
/// It is a sink rather than a source. Nothing here polls or watches; whoever is doing the work
/// calls <see cref="Report"/>, which is what lets a winget install and a file copy share one
/// activity without this class knowing what either of them is.
///
/// Indeterminate work is supported and is the common case at the start of a job: the ring draws a
/// quiet full circle rather than a lie about being 0% done.
/// </summary>
public sealed partial class ProgressActivity : ObservableObject, IIslandActivity
{
    /// <summary>
    /// How long a finished job stays up. Long enough to be seen by somebody who looked away, short
    /// enough not to become furniture -- the same reasoning as the timer's own dwell, and the same
    /// figure, because both are announcing that a thing they were counting has stopped.
    /// </summary>
    private static readonly TimeSpan FinishedDwell = TimeSpan.FromSeconds(8);

    private DateTimeOffset _finishedAt;

    [ObservableProperty] private bool _isActive;

    /// <summary>What is being done, in as few words as fit a pill: "Installing VS Code".</summary>
    [ObservableProperty] private string _label = string.Empty;

    /// <summary>0 to 1, and ignored entirely while <see cref="IsIndeterminate"/>.</summary>
    [ObservableProperty] private double _progress;

    /// <summary>
    /// Whether there is a fraction worth drawing yet. True while a job is starting and has no
    /// number to give -- a download before the first content-length, an install before the package
    /// is resolved.
    /// </summary>
    [ObservableProperty] private bool _isIndeterminate = true;

    /// <summary>Whether the job has finished and is only being shown for a moment longer.</summary>
    [ObservableProperty] private bool _isFinished;

    /// <summary>The percentage as words, for the row that has room for them.</summary>
    public string ProgressText => IsIndeterminate
        ? "Working"
        : $"{(int)Math.Round(Math.Clamp(Progress, 0, 1) * 100)}%";

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(ProgressText));

    partial void OnIsIndeterminateChanged(bool value) => OnPropertyChanged(nameof(ProgressText));

    public string Key => "progress";

    /// <summary>
    /// Above music but below anything urgent, and in the bubble rather than the pill whenever
    /// something else wants it.
    ///
    /// The same judgement the timer made and for the same reason: a ring says how far along a job
    /// is without words, so evicting a track somebody chose in order to spell out a number they can
    /// read off an arc is a bad trade. With nothing playing it takes the pill and says the job's
    /// name, because then there is nothing to take it from.
    /// </summary>
    public IslandPriority Priority => IslandPriority.Background;

    /// <summary>Nothing here flaps: a job starts and finishes, it does not blink.</summary>
    public TimeSpan Linger => TimeSpan.Zero;

    /// <summary>
    /// Starts, or updates, the job on the island. One call for both, because a caller reporting
    /// progress should not also have to track whether it has started reporting yet.
    ///
    /// <paramref name="progress"/> is null for work with no fraction to give.
    /// </summary>
    public void Report(string label, double? progress)
    {
        Label = label;
        IsIndeterminate = progress is null;
        Progress = progress is { } value ? Math.Clamp(value, 0, 1) : 0;
        IsFinished = false;
        IsActive = true;
    }

    /// <summary>
    /// Marks the job done. It stays up for <see cref="FinishedDwell"/> so that finishing is
    /// something the island says rather than something it silently stops saying.
    /// </summary>
    public void Finish(DateTimeOffset now, string label)
    {
        if (!IsActive)
            return;

        Label = label;
        IsIndeterminate = false;
        Progress = 1;
        IsFinished = true;
        _finishedAt = now;
    }

    /// <summary>Takes it off the island at once, for a job that was cancelled rather than finished.</summary>
    public void Cancel()
    {
        IsActive = false;
        IsFinished = false;
    }

    /// <summary>
    /// Runs the dwell clock down. Driven by the App's activity tick rather than a timer of its own,
    /// for the same reason as everything else here: no WPF in this project, and a test that can
    /// jump ten seconds without waiting for them.
    /// </summary>
    public void Tick(DateTimeOffset now)
    {
        if (IsActive && IsFinished && now - _finishedAt >= FinishedDwell)
            Cancel();
    }

    public void Retire()
    {
        Label = string.Empty;
        Progress = 0;
        IsIndeterminate = true;
        IsFinished = false;
    }
}
