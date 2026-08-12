using Dock.Core.Models;

namespace Dock.Core.Services;

/// <summary>
/// Looks up time-synced lyrics for a track. Best-effort like everything else that reaches outside
/// the machine here: most tracks have none published anywhere, which is a normal answer and not a
/// failure worth surfacing.
/// </summary>
public interface ILyricsProvider
{
    Task<IReadOnlyList<LyricLine>?> GetLyricsAsync(
        string artist, string title, TimeSpan duration, CancellationToken cancellationToken);
}
