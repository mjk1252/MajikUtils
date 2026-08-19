using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.App.Views;

/// <summary>How much room the now-playing block is being given.</summary>
public enum NowPlayingDensity
{
    /// <summary>Cover, timeline and transport. The panel that is about the track.</summary>
    Full,

    /// <summary>One line above a section that is about something else.</summary>
    Strip
}

/// <summary>
/// What is playing, at whichever of two densities the island currently has room for.
///
/// One control rather than two blocks of markup. The island shows the same session in two places
/// and those used to be a <c>MediaHeader</c> and a <c>MediaStrip</c> declared side by side and
/// toggled by visibility: the same bindings written twice, which is two things that had to be kept
/// in agreement by hand for as long as the project lasted.
/// </summary>
public partial class NowPlayingView : UserControl
{
    public NowPlayingView()
    {
        InitializeComponent();

        // A track with no lyrics has nothing to switch to, and a mode left on from the last song
        // would show the stage empty. Cleared rather than left for the binding to hide, because the
        // button going away does not un-press it.
        DataContextChanged += (_, _) => Hook();
    }

    private MediaViewModel? Media => DataContext as MediaViewModel;

    /// <summary>
    /// Which of the two forms to draw. A dependency property so the host can bind or set it, and
    /// so the equalizer can be shut off in the density that does not draw one.
    /// </summary>
    public static readonly DependencyProperty DensityProperty =
        DependencyProperty.Register(nameof(Density), typeof(NowPlayingDensity), typeof(NowPlayingView),
            new PropertyMetadata(NowPlayingDensity.Full, OnVisualStateChanged));

    public NowPlayingDensity Density
    {
        get => (NowPlayingDensity)GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    /// <summary>
    /// Whether the host is on screen in a state that draws this block. Handed straight to the
    /// equalizer, ANDed with the density -- capturing audio to animate four bars nobody can see is
    /// the one thing this control can do that actually costs something.
    /// </summary>
    public static readonly DependencyProperty IsRunningProperty =
        DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(NowPlayingView),
            new PropertyMetadata(false, OnVisualStateChanged));

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    /// <summary>Passed through to the equalizer, which has no other route to it.</summary>
    public static readonly DependencyProperty AudioSourceProperty =
        DependencyProperty.Register(nameof(AudioSource), typeof(IAudioLevelSource), typeof(NowPlayingView),
            new PropertyMetadata(null, OnAudioSourceChanged));

    public IAudioLevelSource? AudioSource
    {
        get => (IAudioLevelSource?)GetValue(AudioSourceProperty);
        set => SetValue(AudioSourceProperty, value);
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NowPlayingView)d).ApplyVisualState();

    private static void OnAudioSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NowPlayingView)d).Equalizer.AudioSource = (IAudioLevelSource?)e.NewValue;

    private void Hook()
    {
        if (Media is null)
            return;

        Media.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MediaViewModel.HasLyrics) && !Media.HasLyrics)
                LyricsToggle.IsChecked = false;
        };
    }

    private void ApplyVisualState()
    {
        var full = Density == NowPlayingDensity.Full;

        FullDensity.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        StripDensity.Visibility = full ? Visibility.Collapsed : Visibility.Visible;

        // The strip has no bars, so the capture that feeds them stops with it.
        Equalizer.IsRunning = IsRunning && full;

        UpdateStage();
    }

    /// <summary>
    /// Swaps the cover for the lyrics, or back. Both fill the same fixed-height stage, so this
    /// never changes how tall the island is -- which is the entire reason lyrics are a mode here
    /// rather than the extra band under the transport row they used to be.
    /// </summary>
    private void UpdateStage()
    {
        var lyrics = LyricsToggle.IsChecked == true;

        CoverStage.Visibility = lyrics ? Visibility.Collapsed : Visibility.Visible;
        LyricStage.Visibility = lyrics ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLyricsToggled(object sender, RoutedEventArgs e) => UpdateStage();

    private void OnSeek(object sender, MouseButtonEventArgs e)
    {
        var fraction = Timeline.ActualWidth > 0
            ? e.GetPosition(Timeline).X / Timeline.ActualWidth
            : 0;

        Media?.SeekCommand.Execute(fraction);
    }
}
