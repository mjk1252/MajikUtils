using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

/// <summary>
/// The media island's contents. Holds the latest <see cref="MediaSnapshot"/> and exposes it as the
/// handful of display-ready values the island binds to.
///
/// The island's resting activity: it claims the pill whenever something is playing, and yields it
/// to anything of higher rank without either side knowing about the other.
/// </summary>
public partial class MediaViewModel : ObservableObject, IIslandActivity
{
    /// <summary>
    /// How long a lost session keeps the pill. Losing one is routine and usually momentary --
    /// closing one tab of several, a player restarting its session between albums -- and without
    /// this the island would blink away and back between two tracks.
    /// </summary>
    private static readonly TimeSpan SessionLinger = TimeSpan.FromMilliseconds(1500);

    private readonly IMediaSessionSource _source;
    private MediaSnapshot? _snapshot;

    /// <summary>
    /// Whether there is a track worth drawing. Display state, not the slot claim: it stays true
    /// across the gap between two songs, so the pill keeps showing the track that just finished
    /// rather than blanking to "Nothing playing" for a second and a half. Cleared by
    /// <see cref="Retire"/>, once the island has actually let go of this activity.
    /// </summary>
    [ObservableProperty] private bool _hasSession;

    /// <summary>
    /// Whether a session exists *right now*. Unlike <see cref="HasSession"/> this drops the moment
    /// the player goes, which is what starts the host's linger window running.
    /// </summary>
    [ObservableProperty] private bool _isActive;

    public string Key => "media";

    /// <summary>Music is what the island shows when nothing more urgent is happening.</summary>
    public IslandPriority Priority => IslandPriority.Ambient;

    public TimeSpan Linger => SessionLinger;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private byte[]? _artwork;
    [ObservableProperty] private bool _isPlaying;

    [ObservableProperty] private bool _canSkipNext;
    [ObservableProperty] private bool _canSkipPrevious;

    /// <summary>False for live streams and anything else with no known length, which have no bar to draw.</summary>
    [ObservableProperty] private bool _hasTimeline;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _positionText = string.Empty;
    [ObservableProperty] private string _durationText = string.Empty;

    public MediaViewModel(IMediaSessionSource source)
    {
        _source = source;
    }

    public void Apply(MediaSnapshot? snapshot)
    {
        _snapshot = snapshot;
        IsActive = snapshot is not null;

        if (snapshot is null)
        {
            // Everything else is left standing. The session going is usually the gap between two
            // tracks, and the island holds the last one up across it -- see Retire, which is where
            // a session that stayed gone actually clears.
            //
            // Playback is the exception: it has demonstrably stopped, so the bars flatten now
            // rather than dancing to a track that ended.
            IsPlaying = false;
            return;
        }

        HasSession = true;
        Title = snapshot.Title;
        Artist = snapshot.Artist;
        IsPlaying = snapshot.IsPlaying;
        CanSkipNext = snapshot.CanSkipNext;
        CanSkipPrevious = snapshot.CanSkipPrevious;

        // Only reassigned when the bytes actually differ: every assignment makes the view decode a
        // fresh bitmap, and playback state changes several times a track while the art stays put.
        if (!SameArtwork(Artwork, snapshot.Artwork))
            Artwork = snapshot.Artwork;

        Tick();
    }

    /// <summary>
    /// Clears the now-playing row. Called by the island once a session that went stayed gone --
    /// never straight from <see cref="Apply"/>, which is the whole point of the grace period.
    /// </summary>
    public void Retire()
    {
        HasSession = false;
        Title = string.Empty;
        Artist = string.Empty;
        Artwork = null;
        IsPlaying = false;
        CanSkipNext = false;
        CanSkipPrevious = false;
        HasTimeline = false;
        Progress = 0;
        PositionText = string.Empty;
        DurationText = string.Empty;
    }

    /// <summary>
    /// Advances the progress readout between snapshots. The system publishes a position only when
    /// something changes it -- a play, a seek, a track change -- so a bar that moved only on those
    /// would sit still for whole songs. Extrapolating from the last snapshot's capture time is what
    /// makes it run.
    /// </summary>
    public void Tick()
    {
        if (_snapshot is not { } snapshot || snapshot.Duration <= TimeSpan.Zero)
        {
            HasTimeline = false;
            Progress = 0;
            return;
        }

        var position = snapshot.Position;
        if (snapshot.IsPlaying)
            position += DateTimeOffset.UtcNow - snapshot.CapturedAt;

        position = position < TimeSpan.Zero ? TimeSpan.Zero
            : position > snapshot.Duration ? snapshot.Duration
            : position;

        HasTimeline = true;
        Progress = position / snapshot.Duration;
        PositionText = Format(position);
        DurationText = Format(snapshot.Duration);
    }

    [RelayCommand]
    private void PlayPause() => _source.TogglePlayPause();

    [RelayCommand]
    private void SkipNext() => _source.SkipNext();

    [RelayCommand]
    private void SkipPrevious() => _source.SkipPrevious();

    private static string Format(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    private static bool SameArtwork(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        return left is not null && right is not null && left.AsSpan().SequenceEqual(right);
    }
}
