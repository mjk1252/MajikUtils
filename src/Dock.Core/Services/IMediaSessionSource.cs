using Dock.Core.Models;

namespace Dock.Core.Services;

/// <summary>
/// The system's current media session -- whatever the volume flyout would show -- plus the three
/// transport commands worth putting on a hover panel.
///
/// The commands return nothing and are not awaited: the result that matters is the snapshot the
/// player publishes afterwards, which arrives through <see cref="Changed"/> like any other change.
/// </summary>
public interface IMediaSessionSource
{
    /// <summary>Fires with null when nothing is playing at all, including when the last player quits.</summary>
    event EventHandler<MediaSnapshot?>? Changed;

    void Start();
    void Stop();

    void TogglePlayPause();
    void SkipNext();
    void SkipPrevious();
    void SeekTo(TimeSpan position);
}
