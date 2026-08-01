namespace Dock.Core.Models;

/// <summary>
/// What is playing right now, as one immutable reading. A snapshot rather than a live object
/// because it crosses a thread boundary: the system raises its media notifications on the thread
/// pool, and the UI reads them on the dispatcher.
/// </summary>
/// <param name="Position">Playback position at <paramref name="CapturedAt"/>, not at the moment
/// this is read -- the system only republishes a position when something changes it, so a moving
/// progress bar has to extrapolate from these two together.</param>
public sealed record MediaSnapshot(
    string Title,
    string Artist,
    bool IsPlaying,
    bool CanSkipNext,
    bool CanSkipPrevious,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset CapturedAt,
    byte[]? Artwork);
