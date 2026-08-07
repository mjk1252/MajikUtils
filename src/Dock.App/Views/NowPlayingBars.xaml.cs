using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Dock.Core.Services;

namespace Dock.App.Views;

/// <summary>
/// The four bars on the collapsed pill, driven by what the speakers are actually playing.
///
/// Lifted out of <see cref="IslandWindow"/> when the collapsed pill became a ContentControl over
/// whichever activity holds it: <c>x:Name</c> does not resolve inside a DataTemplate, so the bars
/// could not stay where they were. Which is no loss -- none of this was ever about the window.
///
/// One thing falls out of the move for free. The control only exists while media holds the pill,
/// so an activity taking the pill away tears it down, and the audio capture stops without anybody
/// having written that rule.
/// </summary>
public partial class NowPlayingBars : UserControl
{
    /// <summary>
    /// The fallback equalizer, used only when this machine will not hand over its audio: how tall
    /// each bar reaches, how long a full rise takes, and how far into that rise it starts. The
    /// periods are deliberately not multiples of each other and the phases are staggered -- four
    /// bars sharing a beat read as a single blinking block, where four drifting against each other
    /// read as sound.
    /// </summary>
    private static readonly (double Peak, int PeriodMs, int PhaseMs)[] BarBeats =
    [
        (13, 480, 0),
        (9, 620, 160),
        (14, 540, 80),
        (10, 700, 240)
    ];

    /// <summary>Where the bars sit when the music is paused -- a flat line, not a frozen waveform.</summary>
    private const double BarRestingHeight = 3;

    /// <summary>How tall a band at full scale draws.</summary>
    private const double BarPeakHeight = 14;

    // Rises faster than it falls, which is what makes a bar read as hit-and-decay rather than as a
    // needle. Applied per published frame, of which there are roughly forty-five a second.
    private const double BarAttack = 0.55;
    private const double BarRelease = 0.16;

    private static readonly Duration AccentDuration = TimeSpan.FromMilliseconds(320);

    private readonly Rectangle[] _bars;

    /// <summary>
    /// One diagonal gradient per bar, together forming a single sweep across the whole equalizer:
    /// bar <c>i</c> carries the slice of the artwork's two-colour ramp that falls where it sits.
    /// One brush shared by all four would restart the gradient inside each 2.5px bar instead.
    /// </summary>
    private readonly LinearGradientBrush[] _barBrushes;

    /// <summary>Smoothed band levels, 0..1, so a bar eases between published frames.</summary>
    private readonly double[] _barLevels;

    /// <summary>The source currently subscribed to, which is not always the one on the property.</summary>
    private IAudioLevelSource? _hooked;

    /// <summary>
    /// Whether real audio is driving the bars. False on a machine whose loopback endpoint would not
    /// open, where the fixed animation takes over.
    /// </summary>
    private bool _audioDriven;

    private bool _running;

    /// <summary>
    /// Whether the pill is on screen and collapsed. The window's half of the answer -- this control
    /// supplies the other half from <see cref="IsPlaying"/>, and both have to be true.
    /// </summary>
    public static readonly DependencyProperty IsRunningProperty =
        DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(NowPlayingBars),
            new PropertyMetadata(false, OnRunningChanged));

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(NowPlayingBars),
            new PropertyMetadata(false, OnRunningChanged));

    /// <summary>
    /// The cover art, as PNG bytes. Bound rather than typed against a view model, so this control
    /// knows about no activity.
    ///
    /// Declared as object rather than byte[] because the XAML compiler refuses an array-typed
    /// dependency property inside a template section, which is exactly where this one is set.
    /// </summary>
    public static readonly DependencyProperty ArtworkProperty =
        DependencyProperty.Register(nameof(Artwork), typeof(object), typeof(NowPlayingBars),
            new PropertyMetadata(null, OnArtworkChanged));

    public static readonly DependencyProperty AudioSourceProperty =
        DependencyProperty.Register(nameof(AudioSource), typeof(IAudioLevelSource), typeof(NowPlayingBars),
            new PropertyMetadata(null, OnAudioSourceChanged));

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public object? Artwork
    {
        get => GetValue(ArtworkProperty);
        set => SetValue(ArtworkProperty, value);
    }

    public IAudioLevelSource? AudioSource
    {
        get => (IAudioLevelSource?)GetValue(AudioSourceProperty);
        set => SetValue(AudioSourceProperty, value);
    }

    public NowPlayingBars()
    {
        InitializeComponent();

        _bars = [Bar1, Bar2, Bar3, Bar4];
        _barLevels = new double[_bars.Length];
        _barBrushes = new LinearGradientBrush[_bars.Length];

        for (var i = 0; i < _bars.Length; i++)
        {
            _barBrushes[i] = BuildBarBrush(i, ArtworkAccent.Fallback, ArtworkAccent.FallbackSecondary);
            _bars[i].Fill = _barBrushes[i];
        }

        // Unloaded is what stops the capture when another activity takes the pill: the template is
        // torn down, and nothing else would tell us the bars are no longer on screen.
        Unloaded += (_, _) => Stop();
        Loaded += (_, _) => UpdateEqualizer();
    }

    private static void OnRunningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NowPlayingBars)d).UpdateEqualizer();

    private static void OnArtworkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NowPlayingBars)d).UpdateBarColour();

    private static void OnAudioSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bars = (NowPlayingBars)d;
        bars.Rehook(e.NewValue as IAudioLevelSource);
        bars.UpdateEqualizer();
    }

    private void Rehook(IAudioLevelSource? source)
    {
        if (ReferenceEquals(_hooked, source))
            return;

        if (_hooked is not null)
        {
            _hooked.LevelsChanged -= OnAudioLevels;
            _hooked.Stop();
        }

        _hooked = source;
        _audioDriven = false;
        _running = false;

        if (_hooked is not null)
            _hooked.LevelsChanged += OnAudioLevels;
    }

    private void Stop()
    {
        if (!_running)
            return;

        _running = false;
        _audioDriven = false;
        _hooked?.Stop();
        Array.Clear(_barLevels);
        Rest();
    }

    /// <summary>
    /// Runs the bars, but only while there is something to see: paused music flattens them, and a
    /// hidden or expanded pill stops them outright rather than leaving an audio capture and four
    /// animations running behind an invisible layer.
    /// </summary>
    private void UpdateEqualizer()
    {
        var running = IsRunning && IsPlaying && IsLoaded && _hooked is not null;
        if (running == _running)
            return;

        _running = running;

        if (!running)
        {
            _hooked?.Stop();
            _audioDriven = false;
            Array.Clear(_barLevels);
            Rest();
            return;
        }

        // Real levels if this machine will give them up, the old fixed animation if not. Started
        // here rather than at construction so nothing is captured while the bars are off screen.
        _audioDriven = _hooked!.Start();

        if (_audioDriven)
        {
            Rest();
            return;
        }

        for (var i = 0; i < _bars.Length; i++)
        {
            var (peak, periodMs, phaseMs) = BarBeats[i];
            _bars[i].BeginAnimation(HeightProperty, new DoubleAnimation(BarRestingHeight, peak,
                TimeSpan.FromMilliseconds(periodMs))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,

                // Negative, so each bar starts partway through its own rise instead of every bar
                // waiting at the floor for its turn.
                BeginTime = TimeSpan.FromMilliseconds(-phaseMs),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }
    }

    /// <summary>
    /// Flattens the bars and hands their heights back. Handing null to BeginAnimation is what
    /// releases the property to its local value; without it the last animated height sticks and the
    /// bar cannot be set at all.
    /// </summary>
    private void Rest()
    {
        foreach (var bar in _bars)
        {
            bar.BeginAnimation(HeightProperty, null);
            bar.Height = BarRestingHeight;
        }
    }

    /// <summary>
    /// A frame of band levels, straight off the capture thread. Smoothed on the way in: the
    /// analysis window is short enough that raw values jitter, and a bar that follows every one of
    /// them reads as noise rather than as the beat it is actually tracking.
    /// </summary>
    private void OnAudioLevels(object? sender, double[] levels) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!_running || !_audioDriven)
                return;

            for (var i = 0; i < _bars.Length && i < levels.Length; i++)
            {
                var target = levels[i];
                var rate = target > _barLevels[i] ? BarAttack : BarRelease;
                _barLevels[i] += (target - _barLevels[i]) * rate;

                _bars[i].Height = BarRestingHeight + _barLevels[i] * (BarPeakHeight - BarRestingHeight);
            }
        }, System.Windows.Threading.DispatcherPriority.Render);

    /// <summary>
    /// Repaints the bars in the artwork's two most prominent colours, as one diagonal gradient
    /// running across the group. Crossfaded rather than swapped: the artwork itself cuts between
    /// tracks, but coloured bars snapping to an unrelated hue in the corner of the eye reads as a
    /// glitch.
    /// </summary>
    private void UpdateBarColour()
    {
        var (primary, secondary) = ArtworkAccent.PairFromPng(Artwork as byte[]);

        for (var i = 0; i < _barBrushes.Length; i++)
        {
            Fade(_barBrushes[i].GradientStops[0], Blend(primary, secondary, StopOffset(i)));
            Fade(_barBrushes[i].GradientStops[1], Blend(primary, secondary, StopOffset(i + 1)));
        }
    }

    private static void Fade(GradientStop stop, Color to) =>
        stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(to, AccentDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

    /// <summary>
    /// The gradient for one bar: the slice of the primary-to-secondary ramp that belongs where this
    /// bar sits, drawn corner to corner so the sweep runs diagonally rather than straight down.
    /// </summary>
    private LinearGradientBrush BuildBarBrush(int index, Color primary, Color secondary) =>
        new(new GradientStopCollection
        {
            new(Blend(primary, secondary, StopOffset(index)), 0),
            new(Blend(primary, secondary, StopOffset(index + 1)), 1)
        })
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };

    /// <summary>Where a bar's edge falls along the group-wide ramp, 0 at the first bar's left edge.</summary>
    private double StopOffset(int barEdge) => (double)barEdge / _bars.Length;

    private static Color Blend(Color from, Color to, double amount) =>
        Color.FromArgb(
            (byte)Math.Round(from.A + (to.A - from.A) * amount),
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
}
