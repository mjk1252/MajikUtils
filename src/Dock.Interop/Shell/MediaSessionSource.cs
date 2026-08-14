using System.Runtime.InteropServices;
using Dock.Core.Models;
using Dock.Core.Services;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Dock.Interop.Shell;

/// <summary>
/// Reads the system media session -- the same one behind the volume flyout's transport controls --
/// through WinRT's GlobalSystemMediaTransportControls. Every player that shows up there
/// (Spotify, browsers, VLC, Media Player) is covered by this one API; nothing is per-application.
///
/// Everything is best-effort. The session belongs to another process which can exit mid-call, so
/// each hop into WinRT can fail with an RPC or COM error at any moment; the whole surface degrades
/// to "nothing is playing" rather than propagating, which for a passive HUD is the right failure.
/// </summary>
public sealed class MediaSessionSource : IMediaSessionSource, IDisposable
{
    /// <summary>
    /// Serialises refreshes. Several notifications land at once on a track change -- properties,
    /// playback info and timeline all fire -- and each one awaits, so without this they interleave
    /// and the last snapshot published is not necessarily the newest one read.
    /// </summary>
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _disposed;

    // Cached because refreshing them means an await and a thumbnail decode, while playback and
    // timeline changes -- which arrive far more often -- carry none of this.
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private byte[]? _artwork;

    public event EventHandler<MediaSnapshot?>? Changed;

    public void Start() => _ = StartAsync();

    private async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or TypeLoadException)
        {
            // No media session service to talk to (older or trimmed-down Windows, or a locked-down
            // container). The island simply never has anything to show.
            return;
        }

        if (_disposed)
            return;

        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        AttachCurrentSession();
    }

    public void Stop()
    {
        DetachSession();

        if (_manager is not null)
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;

        _manager = null;
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args)
        => AttachCurrentSession();

    /// <summary>
    /// Points at whichever session Windows currently considers foremost. Handlers move with it:
    /// they are per-session, so a switch from one player to another means unsubscribing from the
    /// old one or its events keep arriving for a session nothing is displaying.
    /// </summary>
    private void AttachCurrentSession()
    {
        DetachSession();

        if (_disposed)
            return;

        try
        {
            _session = _manager?.GetCurrentSession();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _session = null;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }

        _ = RefreshAsync(includeProperties: true);
    }

    private void DetachSession()
    {
        if (_session is null)
            return;

        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        _session = null;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        => _ = RefreshAsync(includeProperties: true);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        => _ = RefreshAsync(includeProperties: false);

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        => _ = RefreshAsync(includeProperties: false);

    private async Task RefreshAsync(bool includeProperties)
    {
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var (publish, snapshot) = await BuildSnapshotAsync(includeProperties).ConfigureAwait(false);
            if (publish)
                Changed?.Invoke(this, snapshot);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Reads the session, distinguishing "nothing is playing" from "this reading tells us nothing".
    ///
    /// The difference matters because a track change is not one event: the session is momentarily
    /// unreadable, its status passes through Changing, and with several players around the current
    /// session can be swapped out entirely mid-read. Publishing an empty snapshot for any of those
    /// would tear the island down and rebuild it between songs.
    /// </summary>
    private async Task<(bool Publish, MediaSnapshot? Snapshot)> BuildSnapshotAsync(bool includeProperties)
    {
        const bool Publish = true;
        const bool Ignore = false;

        var session = _session;
        if (session is null)
        {
            ClearProperties();
            return (Publish, null);
        }

        if (includeProperties)
        {
            try
            {
                var properties = await session.TryGetMediaPropertiesAsync();

                // The current session can be swapped out while that await is in flight, in which
                // case these properties describe a player nobody is looking at any more. The
                // refresh the swap kicked off is the one that will publish.
                if (!ReferenceEquals(session, _session))
                    return (Ignore, null);

                _title = properties?.Title ?? string.Empty;
                _artist = FirstNonEmpty(properties?.Artist, properties?.AlbumArtist, properties?.AlbumTitle);
                _artwork = await ReadThumbnailAsync(properties?.Thumbnail).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                // A player that has genuinely gone raises CurrentSessionChanged, and that is what
                // clears the island -- one unanswered call is not evidence of anything.
                return (Ignore, null);
            }
        }

        try
        {
            var playback = session.GetPlaybackInfo();
            var status = playback?.PlaybackStatus;

            // Changing is the gap between two tracks. Holding the last one there is what makes a
            // track change look like the title being replaced rather than the island blinking.
            if (status is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing or null)
                return (Ignore, null);

            // Anything else outside Playing/Paused is a player with nothing loaded -- closed,
            // stopped, or still opening -- so the island gets out of the way rather than hanging
            // on to a stale title.
            if (status is not (GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused))
            {
                return (Publish, null);
            }

            // A player with neither a title nor artwork has nothing worth showing, whatever it
            // claims about its playback state.
            if (string.IsNullOrWhiteSpace(_title) && _artwork is null)
                return (Publish, null);

            var timeline = session.GetTimelineProperties();
            var duration = timeline is null ? TimeSpan.Zero : timeline.EndTime - timeline.StartTime;
            var position = timeline is null ? TimeSpan.Zero : timeline.Position - timeline.StartTime;

            return (Publish, new MediaSnapshot(
                Title: _title,
                Artist: _artist,
                IsPlaying: status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanSkipNext: playback!.Controls.IsNextEnabled,
                CanSkipPrevious: playback.Controls.IsPreviousEnabled,
                Position: position,
                Duration: duration > TimeSpan.Zero ? duration : TimeSpan.Zero,

                // The player's own clock for this reading, not ours: the position was current as
                // of LastUpdatedTime, which may already be a moment ago.
                CapturedAt: timeline?.LastUpdatedTime ?? DateTimeOffset.UtcNow,
                Artwork: _artwork));
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            return (Ignore, null);
        }
    }

    private void ClearProperties()
    {
        _title = string.Empty;
        _artist = string.Empty;
        _artwork = null;
    }

    /// <summary>
    /// Pulls the album art out as raw bytes. Read through a DataReader rather than a .NET stream
    /// wrapper so nothing but the WinRT projection is involved; the bytes go straight to a
    /// BitmapImage, which sniffs the format itself (players supply PNG or JPEG interchangeably).
    /// </summary>
    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null)
            return null;

        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size is 0 or > MaxThumbnailBytes)
                return null;

            var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);

            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            return null;
        }
    }

    /// <summary>Album art is a few hundred KB at most; anything larger is a player misreporting.</summary>
    private const ulong MaxThumbnailBytes = 8 * 1024 * 1024;

    public void TogglePlayPause() => Invoke(s => s.TryTogglePlayPauseAsync());

    public void SkipNext() => Invoke(s => s.TrySkipNextAsync());

    public void SkipPrevious() => Invoke(s => s.TrySkipPreviousAsync());

    public void SeekTo(TimeSpan position) =>
        Invoke(s => s.TryChangePlaybackPositionAsync(position.Ticks));

    /// <summary>
    /// Fires a transport command at the current session and forgets it. Nothing is done with the
    /// result: whether the player honoured it shows up as the change notification it raises
    /// afterwards, which is what the island is already listening for.
    /// </summary>
    private void Invoke(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        var session = _session;
        if (session is null)
            return;

        try
        {
            _ = command(session);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            // The player exited between the click and the call.
        }
    }

    /// <summary>
    /// The failure modes of talking to another process's media session: it exited, it is not
    /// answering, or the object we hold is already dead. None are worth surfacing.
    /// </summary>
    private static bool IsTransient(Exception ex) =>
        ex is COMException or InvalidOperationException or ObjectDisposedException
            or UnauthorizedAccessException or TimeoutException;

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? string.Empty;

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _refreshGate.Dispose();
    }
}
