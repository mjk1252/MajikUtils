using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

/// <summary>
/// The media island's contents. Holds the latest <see cref="MediaSnapshot"/> and exposes it as the
/// handful of display-ready values the island binds to.
/// </summary>
public partial class MediaViewModel : ObservableObject
{
    private readonly IMediaSessionSource _source;
    private MediaSnapshot? _snapshot;

    /// <summary>False when nothing is playing -- which is what decides whether the island shows itself.</summary>
    [ObservableProperty] private bool _hasSession;

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
        HasSession = snapshot is not null;

        if (snapshot is null)
        {
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
            return;
        }

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
