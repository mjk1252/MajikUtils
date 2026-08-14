using System.Collections.ObjectModel;
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

    /// <summary>
    /// Time-synced lines for the current track, if any were found. Populated by the App layer,
    /// which owns the network call this needs -- this class stays synchronous like the rest of
    /// <c>Dock.Core</c>, the same reasoning that keeps the media session itself outside it.
    /// </summary>
    public ObservableCollection<LyricLineViewModel> Lyrics { get; } = [];

    [ObservableProperty] private bool _hasLyrics;

    /// <summary>-1 while nothing has started yet, or while there are no lyrics to point into.</summary>
    [ObservableProperty] private int _currentLyricIndex = -1;

    /// <summary>
    /// The line being sung now. The island shows exactly two rows -- this one and
    /// <see cref="NextLyricText"/> -- rather than a scrolling list: a window onto a list only ever
    /// lands where the scroll leaves it, which in a 64px viewport meant the current line sharing
    /// the space with whatever had already been sung. Two derived strings can only ever show the
    /// two lines that matter.
    /// </summary>
    public string CurrentLyricText => LyricTextAt(CurrentLyricIndex);

    /// <summary>
    /// The line coming up, or empty on the last line of a song -- the view leaves that row blank
    /// rather than filling it with anything, since there genuinely is nothing next.
    /// </summary>
    public string NextLyricText => LyricTextAt(CurrentLyricIndex + 1);

    private string LyricTextAt(int index) =>
        index >= 0 && index < Lyrics.Count ? Lyrics[index].Text : string.Empty;

    /// <summary>
    /// Both rows are derived from the index, so they are re-read together whenever it moves -- and
    /// whenever the lines underneath it are replaced, which can leave the index where it was while
    /// meaning something completely different.
    /// </summary>
    private void NotifyLyricTextChanged()
    {
        OnPropertyChanged(nameof(CurrentLyricText));
        OnPropertyChanged(nameof(NextLyricText));
    }

    partial void OnCurrentLyricIndexChanged(int value) => NotifyLyricTextChanged();

    private LyricLineViewModel? _currentLyric;
    private TimeSpan[] _lyricOffsets = [];

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
        ClearLyrics();
    }

    /// <summary>
    /// Replaces the lyrics for whatever track is current. Called by the App layer once its lookup
    /// for the current title/artist finishes -- by the time it does, playback may have moved on to
    /// a different track entirely, which is the caller's race to guard against, not this method's.
    /// </summary>
    public void SetLyrics(IReadOnlyList<LyricLine> lines)
    {
        Lyrics.Clear();
        _currentLyric = null;

        foreach (var line in lines)
            Lyrics.Add(new LyricLineViewModel(line.Text));

        _lyricOffsets = lines.Select(l => l.Offset).ToArray();
        HasLyrics = Lyrics.Count > 0;

        // Back to the start before working out where the new track is: index 3 of the old sheet and
        // index 3 of the new one are different lines, and UpdateLyricIndex does nothing when the
        // number it computes matches the one already there.
        CurrentLyricIndex = -1;
        UpdateLyricIndex();
    }

    /// <summary>Called when a track changes (there is nothing to scroll for the new one yet) or ends.</summary>
    public void ClearLyrics()
    {
        Lyrics.Clear();
        _lyricOffsets = [];
        _currentLyric = null;
        CurrentLyricIndex = -1;
        HasLyrics = false;
    }

    /// <summary>
    /// Finds the line that should be highlighted for a given playback position and flips
    /// <see cref="LyricLineViewModel.IsCurrent"/> only on the two rows that actually change --
    /// touching every row on every tick would be forty property-change notifications a second for
    /// nineteen of them that did nothing.
    /// </summary>
    private void UpdateLyricIndex(TimeSpan position = default)
    {
        if (_lyricOffsets.Length == 0)
        {
            CurrentLyricIndex = -1;
            return;
        }

        var index = Array.FindLastIndex(_lyricOffsets, offset => offset <= position);

        if (index == CurrentLyricIndex)
            return;

        if (_currentLyric is not null)
            _currentLyric.IsCurrent = false;

        CurrentLyricIndex = index;
        _currentLyric = index >= 0 ? Lyrics[index] : null;

        if (_currentLyric is not null)
            _currentLyric.IsCurrent = true;
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

        UpdateLyricIndex(position);
    }

    [RelayCommand]
    private void PlayPause() => _source.TogglePlayPause();

    [RelayCommand]
    private void SkipNext() => _source.SkipNext();

    [RelayCommand]
    private void SkipPrevious() => _source.SkipPrevious();

    /// <summary>
    /// Seeks to a fraction of the track, as clicked on the progress bar. Guarded the same as
    /// <see cref="Tick"/>: nothing to scale against without a snapshot and a known duration.
    /// </summary>
    [RelayCommand]
    private void Seek(double progress)
    {
        if (_snapshot is not { } snapshot || snapshot.Duration <= TimeSpan.Zero)
            return;

        _source.SeekTo(snapshot.Duration * Math.Clamp(progress, 0, 1));
    }

    private static string Format(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    private static bool SameArtwork(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        return left is not null && right is not null && left.AsSpan().SequenceEqual(right);
    }
}
