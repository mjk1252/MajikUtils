using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class MediaViewModelTests
{
    [Fact]
    public void Apply_Null_DropsTheClaimButHoldsTheTrackUp()
    {
        var viewModel = new MediaViewModel(new FakeSource());
        viewModel.Apply(Snapshot(isPlaying: true, artwork: [1, 2, 3]));

        viewModel.Apply(null);

        // The island is still showing this: losing the session is usually the gap between two
        // tracks, and blanking here would put "Nothing playing" on the pill for the whole of it.
        Assert.False(viewModel.IsActive);
        Assert.True(viewModel.HasSession);
        Assert.Equal("Title", viewModel.Title);
        Assert.NotNull(viewModel.Artwork);

        // Playback is the one thing that has demonstrably stopped, so the bars flatten.
        Assert.False(viewModel.IsPlaying);
    }

    [Fact]
    public void Retire_ClearsEverything()
    {
        var viewModel = new MediaViewModel(new FakeSource());
        viewModel.Apply(Snapshot(isPlaying: true, artwork: [1, 2, 3]));
        viewModel.Apply(null);

        viewModel.Retire();

        Assert.False(viewModel.HasSession);
        Assert.False(viewModel.IsActive);
        Assert.Equal(string.Empty, viewModel.Title);
        Assert.Null(viewModel.Artwork);
        Assert.False(viewModel.IsPlaying);
    }

    [Fact]
    public void Apply_AfterLosingTheSession_ClaimsAgainWithoutRetiring()
    {
        var viewModel = new MediaViewModel(new FakeSource());
        viewModel.Apply(Snapshot(isPlaying: true));
        viewModel.Apply(null);

        viewModel.Apply(Snapshot(isPlaying: true));

        Assert.True(viewModel.IsActive);
        Assert.True(viewModel.HasSession);
        Assert.True(viewModel.IsPlaying);
    }

    [Fact]
    public void Apply_SameArtworkBytes_DoesNotReassign()
    {
        var viewModel = new MediaViewModel(new FakeSource());
        viewModel.Apply(Snapshot(artwork: [1, 2, 3]));
        var first = viewModel.Artwork;

        // A fresh array with the same contents, which is what a refresh triggered by a mere
        // play/pause hands over. Reassigning would make the view decode the image again.
        viewModel.Apply(Snapshot(artwork: [1, 2, 3]));

        Assert.Same(first, viewModel.Artwork);
    }

    [Fact]
    public void Tick_WhilePlaying_AdvancesPastTheCapturedPosition()
    {
        var viewModel = new MediaViewModel(new FakeSource());

        viewModel.Apply(Snapshot(
            isPlaying: true,
            position: TimeSpan.FromSeconds(30),
            duration: TimeSpan.FromSeconds(100),
            capturedAt: DateTimeOffset.UtcNow - TimeSpan.FromSeconds(20)));

        // 30s in as of 20s ago, so half way through a 100s track by now.
        Assert.True(viewModel.HasTimeline);
        Assert.InRange(viewModel.Progress, 0.49, 0.52);
        Assert.Equal("0:50", viewModel.PositionText);
        Assert.Equal("1:40", viewModel.DurationText);
    }

    [Fact]
    public void Tick_WhilePaused_HoldsTheCapturedPosition()
    {
        var viewModel = new MediaViewModel(new FakeSource());

        viewModel.Apply(Snapshot(
            isPlaying: false,
            position: TimeSpan.FromSeconds(30),
            duration: TimeSpan.FromSeconds(100),
            capturedAt: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5)));

        Assert.Equal(0.3, viewModel.Progress, 3);
    }

    [Fact]
    public void Tick_PastTheEnd_ClampsToTheDuration()
    {
        var viewModel = new MediaViewModel(new FakeSource());

        // A track that finished while nothing published a newer snapshot.
        viewModel.Apply(Snapshot(
            isPlaying: true,
            position: TimeSpan.FromSeconds(90),
            duration: TimeSpan.FromSeconds(100),
            capturedAt: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2)));

        Assert.Equal(1.0, viewModel.Progress);
    }

    [Fact]
    public void Tick_WithoutADuration_ReportsNoTimeline()
    {
        var viewModel = new MediaViewModel(new FakeSource());

        // A live stream: playing, but with no length to draw a bar against.
        viewModel.Apply(Snapshot(isPlaying: true, duration: TimeSpan.Zero));

        Assert.True(viewModel.HasSession);
        Assert.False(viewModel.HasTimeline);
        Assert.Equal(0, viewModel.Progress);
    }

    [Fact]
    public void PlayPauseCommand_ReachesTheSource()
    {
        var source = new FakeSource();
        var viewModel = new MediaViewModel(source);

        viewModel.PlayPauseCommand.Execute(null);
        viewModel.SkipNextCommand.Execute(null);
        viewModel.SkipPreviousCommand.Execute(null);

        Assert.Equal(1, source.ToggleCount);
        Assert.Equal(1, source.NextCount);
        Assert.Equal(1, source.PreviousCount);
    }

    private static MediaSnapshot Snapshot(
        bool isPlaying = false,
        TimeSpan position = default,
        TimeSpan duration = default,
        DateTimeOffset? capturedAt = null,
        byte[]? artwork = null) =>
        new(
            Title: "Title",
            Artist: "Artist",
            IsPlaying: isPlaying,
            CanSkipNext: true,
            CanSkipPrevious: true,
            Position: position,
            Duration: duration,
            CapturedAt: capturedAt ?? DateTimeOffset.UtcNow,
            Artwork: artwork);

    private sealed class FakeSource : IMediaSessionSource
    {
        public int ToggleCount { get; private set; }
        public int NextCount { get; private set; }
        public int PreviousCount { get; private set; }

        public event EventHandler<MediaSnapshot?>? Changed;

        public void Start() => Changed?.Invoke(this, null);
        public void Stop() { }

        public void TogglePlayPause() => ToggleCount++;
        public void SkipNext() => NextCount++;
        public void SkipPrevious() => PreviousCount++;
    }
}
