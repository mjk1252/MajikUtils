using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

/// <summary>
/// The activity that made the island flicker.
///
/// Core Audio marks a session Inactive whenever the application stops rendering, which the gaps
/// between two sounds do. Read literally, the island tore itself down in every gap and rebuilt
/// itself at the next sound -- a flicker every few seconds, with the pointer nowhere near it, on
/// any machine with a browser open. Firefox is the worst of them: it cycles its per-content-process
/// sessions constantly with nothing obviously playing.
/// </summary>
public class VolumeMixerActivityTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 21, 20, 0, 0, TimeSpan.Zero);

    private static VolumeMixerActivity Mixer() => new(new FakeIcons(), new FakeSource());

    private static AudioSessionInfo Session(string name, bool active, double volume = 0.5, int pid = 1) =>
        new(pid, $@"C:\Apps\{name}.exe", name, volume, IsMuted: false, IsActive: active);

    [Fact]
    public void Apply_ClaimsThePillWhileSomethingIsAudible()
    {
        var mixer = Mixer();

        mixer.Apply([Session("Firefox", active: true)], Start);

        Assert.True(mixer.IsActive);
        Assert.Contains("Firefox", mixer.Summary);
    }

    /// <summary>The whole fix: a gap between two sounds is not the application going quiet.</summary>
    [Fact]
    public void Apply_HoldsThroughTheGapBetweenTwoSounds()
    {
        var mixer = Mixer();
        mixer.Apply([Session("Firefox", active: true)], Start);

        // Firefox stops rendering for three seconds, which Core Audio reports as Inactive.
        mixer.Apply([Session("Firefox", active: false)], Start + TimeSpan.FromSeconds(3));

        Assert.True(mixer.IsActive);
    }

    [Fact]
    public void Apply_LetsGoOnceItIsGenuinelyQuiet()
    {
        var mixer = Mixer();
        mixer.Apply([Session("Firefox", active: true)], Start);

        mixer.Apply([Session("Firefox", active: false)], Start + TimeSpan.FromSeconds(20));

        Assert.False(mixer.IsActive);
    }

    /// <summary>
    /// The flicker itself, as a test. A session cycling active and inactive on the rhythm a
    /// browser actually produces must not toggle the activity even once.
    /// </summary>
    [Fact]
    public void Apply_DoesNotFlickerWhileASessionCycles()
    {
        var mixer = Mixer();
        var changes = 0;

        mixer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VolumeMixerActivity.IsActive))
                changes++;
        };

        var at = Start;

        // Three seconds audible, three seconds not, for a minute.
        for (var i = 0; i < 20; i++)
        {
            mixer.Apply([Session("Firefox", active: i % 2 == 0)], at);
            at += TimeSpan.FromSeconds(3);
        }

        // Once, on the way up, and never again.
        Assert.Equal(1, changes);
        Assert.True(mixer.IsActive);
    }

    /// <summary>
    /// An app inside the grace window still counts as playing, so two of them read as two -- the
    /// grace is about what is making sound, not about what happens to be rendering this instant.
    /// </summary>
    [Fact]
    public void Apply_CountsAnAppInsideTheGraceWindow()
    {
        var mixer = Mixer();

        mixer.Apply([Session("Firefox", active: true, pid: 1)], Start);

        mixer.Apply(
            [Session("Firefox", active: false, pid: 1), Session("Spotify", active: true, pid: 2)],
            Start + TimeSpan.FromSeconds(2));

        Assert.Equal("2 apps playing", mixer.Summary);
    }

    /// <summary>
    /// And once the other has genuinely gone quiet, the pill names the one that has not. The
    /// ordering matters beyond the text: it decides whose icon the pill wears.
    /// </summary>
    [Fact]
    public void Apply_NamesWhatIsStillAudibleOnceTheOtherHasGone()
    {
        var mixer = Mixer();

        mixer.Apply([Session("Firefox", active: true, pid: 1)], Start);

        mixer.Apply(
            [Session("Firefox", active: false, pid: 1), Session("Spotify", active: true, pid: 2)],
            Start + TimeSpan.FromSeconds(20));

        Assert.StartsWith("Spotify", mixer.Summary);
    }

    [Fact]
    public void Apply_IgnoresSilentAndMutedSessions()
    {
        var mixer = Mixer();

        mixer.Apply([new AudioSessionInfo(1, @"C:\a.exe", "Quiet", 0.001, false, true)], Start);
        Assert.False(mixer.IsActive);

        mixer.Apply([new AudioSessionInfo(2, @"C:\b.exe", "Muted", 0.9, true, true)], Start);
        Assert.False(mixer.IsActive);
    }

    /// <summary>Settings switching it off must win regardless of what is playing.</summary>
    [Fact]
    public void Apply_StaysOffThePillWhenNotAllowed()
    {
        var mixer = Mixer();
        mixer.AllowPillClaim = false;

        mixer.Apply([Session("Firefox", active: true)], Start);

        Assert.False(mixer.IsActive);
    }

    /// <summary>A process that goes away is forgotten rather than held for the grace window.</summary>
    [Fact]
    public void Apply_ForgetsAProcessThatHasGone()
    {
        var mixer = Mixer();
        mixer.Apply([Session("Firefox", active: true)], Start);

        mixer.Apply([], Start + TimeSpan.FromSeconds(1));

        Assert.False(mixer.IsActive);
    }

    private sealed class FakeIcons : IIconProvider
    {
        public byte[]? GetIconPng(string path, int size) => [1];
        public byte[]? GetAppIconPng(string appUserModelId, int size) => [1];
    }

    private sealed class FakeSource : IVolumeMixerSource
    {
        public event EventHandler<IReadOnlyList<AudioSessionInfo>>? Changed;
        public bool Start()
        {
            Changed?.Invoke(this, []);
            return true;
        }

        public void Stop() { }
        public void SetVolume(int processId, double level) { }
        public void SetMuted(int processId, bool muted) { }
    }
}
