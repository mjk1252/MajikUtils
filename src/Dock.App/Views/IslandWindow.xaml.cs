using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dock.Core.ViewModels;
using Dock.Interop.Windowing;
using Microsoft.Win32;

namespace Dock.App.Views;

/// <summary>
/// The media island: a pill hanging from the top edge of the primary monitor, showing whatever is
/// playing and growing into transport controls when the pointer reaches it.
///
/// Deliberately not a <see cref="PanelWindow"/>. Those exist to own taskbar buttons and so can
/// never hide; this one owns nothing, takes no focus, and spends most of its life invisible.
/// </summary>
public partial class IslandWindow : Window
{
    // Both sizes are DIPs of the pill inside the window. The window itself is fixed at the larger
    // footprint and never resizes -- see the comment in the XAML.
    private const double CollapsedWidth = 260;
    private const double CollapsedHeight = 34;
    private const double ExpandedWidth = 480;

    /// <summary>
    /// Only used until the expanded panel has been measured once. Its real height is whatever its
    /// contents need, which is not a number worth hard-coding: it is the taller of the artwork and
    /// the column of text beside it, and that column's height comes from font metrics -- so a
    /// figure that fits at one DPI or UI font clips the transport buttons at another.
    /// </summary>
    private const double FallbackExpandedHeight = 132;

    // A collapsed pill is a lozenge -- its bottom corners are half its height. Expanded, that same
    // radius would look like a rounded window, so it grows only a little.
    private const double CollapsedRadius = CollapsedHeight / 2;
    private const double ExpandedRadius = 22;

    /// <summary>
    /// Height of the invisible strip along the top edge that summons the pill when nothing is
    /// playing. Thin on purpose: it is a place to throw the pointer at, not a region to avoid.
    /// </summary>
    private const double PeekHeight = 3;

    /// <summary>
    /// Slack around the pill once it is showing. Without it the pill sits exactly on the boundary
    /// that decides its own state, and a pixel of pointer jitter flickers it.
    /// </summary>
    private const double HoverSlack = 8;

    private static readonly Duration ShowDuration = TimeSpan.FromMilliseconds(220);
    private static readonly Duration ShapeDuration = TimeSpan.FromMilliseconds(200);
    private static readonly Duration AccentDuration = TimeSpan.FromMilliseconds(320);

    /// <summary>
    /// One bar of the now-playing equalizer: how tall it reaches, how long a full rise takes, and
    /// how far into that rise it starts. The periods are deliberately not multiples of each other
    /// and the phases are staggered -- four bars sharing a beat read as a single blinking block,
    /// where four drifting against each other read as sound.
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

    private readonly MediaViewModel _media;
    private readonly NotesViewModel _notes;

    /// <summary>
    /// Hover is polled rather than taken from MouseEnter/MouseLeave. The pill is click-through
    /// whenever it is not expanded, and a click-through window receives no mouse events at all --
    /// so the very state the pointer needs to break it out of is the one that cannot report the
    /// pointer. Polling GetCursorPos costs nothing and covers the idle top-edge strip too, where
    /// there is no window under the pointer to raise anything.
    /// </summary>
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <summary>Runs only while the expanded panel is on screen -- it is the only thing showing a clock.</summary>
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly Rectangle[] _bars;

    /// <summary>
    /// Shared by all four bars so the accent is one animatable colour rather than four that have to
    /// be kept in step.
    /// </summary>
    private readonly SolidColorBrush _barBrush = new(ArtworkAccent.Fallback);

    private IntPtr _hwnd;
    private WorkArea _work;
    private double _expandedHeight = FallbackExpandedHeight;
    private bool _shown;
    private bool _expanded;
    private bool _barsRunning;

    public IslandWindow(MediaViewModel media, NotesViewModel notes)
    {
        _media = media;
        _notes = notes;
        InitializeComponent();
        DataContext = media;
        NotesPanel.DataContext = notes;

        // KeyBinding.Command in XAML doesn't inherit DataContext -- InputBindings sit outside the
        // logical tree -- so Enter-to-add is wired here instead.
        NoteInput.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                _notes.AddNoteCommand.Execute(null);
        };

        // The overlay is WS_EX_NOACTIVATE so brushing it with the pointer never steals focus from
        // whatever the user is typing into elsewhere -- but that also means it can never hold Win32
        // keyboard focus itself, which typing into this box requires. Lifted only for as long as the
        // box is actually in use, and put back the moment the window stops being the foreground one.
        NoteInput.PreviewMouseLeftButtonDown += (_, _) =>
        {
            OverlayWindowStyles.SetActivatable(_hwnd, true);
            Activate();
            NoteInput.Focus();
        };
        Deactivated += (_, _) => OverlayWindowStyles.SetActivatable(_hwnd, false);

        // A new note grows the list inside a panel that may already be open -- the pill has to
        // catch up to that instead of clipping it until the next collapse/expand cycle.
        _notes.Notes.CollectionChanged += (_, _) => ResizeForContentChange();

        _bars = [Bar1, Bar2, Bar3, Bar4];
        foreach (var bar in _bars)
            bar.Fill = _barBrush;

        _hoverTimer.Tick += (_, _) => UpdateFromPointer();
        _progressTimer.Tick += (_, _) => _media.Tick();

        _media.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MediaViewModel.IsPlaying))
                UpdateEqualizer();
            else if (e.PropertyName is nameof(MediaViewModel.Artwork))
                UpdateBarColour();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        OverlayWindowStyles.MakePassiveOverlay(_hwnd);
        OverlayWindowStyles.SetClickThrough(_hwnd, true);

        Reposition();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _hoverTimer.Start();
    }

    /// <summary>
    /// Parks the window centred on the top edge of the primary monitor's work area. Work area
    /// rather than monitor bounds so a taskbar docked at the top pushes the island below it.
    /// </summary>
    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        _work = MonitorPlacement.FromPrimary();

        var width = (int)Math.Round(Width * _work.Scale);
        var height = (int)Math.Round(Height * _work.Scale);
        var left = _work.Left + (_work.Width - width) / 2;

        MonitorPlacement.SetPhysicalBounds(_hwnd, left, _work.Top, width, height);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(Reposition);

    /// <summary>
    /// The whole behaviour, decided once per poll: something playing keeps the pill on screen,
    /// the pointer reaching it opens the controls, and with nothing playing only the top-edge
    /// strip brings it out at all.
    /// </summary>
    private void UpdateFromPointer()
    {
        // A game or a full-screen video owns the whole primary monitor, and a topmost overlay
        // would be drawing straight across it.
        if (ForegroundWindow.IsFullScreenOnPrimary())
        {
            SetExpanded(false);
            SetShown(false);
            return;
        }

        var (x, y) = CursorInfo.GetPosition();
        var hovering = ActiveHitRect().Contains(x, y);

        // The pointer alone is enough to expand now: notes live in this panel too, and those have
        // to be reachable even with nothing playing.
        SetShown(_media.HasSession || hovering);
        SetExpanded(hovering);
    }

    /// <summary>
    /// The region of screen the pointer has to be in, in physical pixels. It grows with the pill:
    /// a thin strip while there is nothing on screen, the pill's own rectangle once there is, and
    /// the expanded panel's once that is open -- so reaching for a transport button never leaves
    /// the region that is keeping it open.
    /// </summary>
    private Rect ActiveHitRect()
    {
        var (width, height, slack) = (_expanded, _shown) switch
        {
            (true, _) => (ExpandedWidth, _expandedHeight, HoverSlack),
            (_, true) => (CollapsedWidth, CollapsedHeight, HoverSlack),
            _ => (CollapsedWidth, PeekHeight, 0d)
        };

        var scaledWidth = width * _work.Scale;
        var left = _work.Left + (_work.Width - scaledWidth) / 2;
        var scaledSlack = slack * _work.Scale;

        return new Rect(
            left - scaledSlack,
            _work.Top,
            scaledWidth + scaledSlack * 2,
            height * _work.Scale + scaledSlack);
    }

    private void SetShown(bool shown)
    {
        if (_shown == shown)
            return;

        _shown = shown;

        if (!shown)
            SetExpanded(false);

        Animate(Pill, OpacityProperty, shown ? 1 : 0, ShowDuration);
        Animate(PillSlide, TranslateTransform.YProperty, shown ? 0 : -(CollapsedHeight + 6), ShowDuration);

        UpdateEqualizer();
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
            return;

        _expanded = expanded;

        if (expanded)
            _expandedHeight = MeasureExpandedHeight();

        ResizePill(expanded);

        Animate(CollapsedLayer, OpacityProperty, expanded ? 0 : 1, ShapeDuration);
        Animate(ExpandedLayer, OpacityProperty, expanded ? 1 : 0, ShapeDuration);

        // Solid only while the controls are there to be pressed. Every other moment the island is
        // something to look at, and a click aimed past it should reach what it was aimed at.
        ExpandedLayer.IsHitTestVisible = expanded;
        OverlayWindowStyles.SetClickThrough(_hwnd, !expanded);

        if (expanded)
        {
            // The elapsed time would otherwise be as stale as the last snapshot, which for a track
            // playing untouched can be minutes old.
            _media.Tick();
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
        }

        UpdateEqualizer();
    }

    /// <summary>
    /// The silhouette and the content host are sized separately rather than nested, because the
    /// silhouette is wider than the pill it draws -- its top corners flare out past both sides.
    /// </summary>
    private void ResizePill(bool expanded)
    {
        var width = expanded ? ExpandedWidth : CollapsedWidth;
        var height = expanded ? _expandedHeight : CollapsedHeight;

        Animate(PillContent, WidthProperty, width, ShapeDuration);
        Animate(PillContent, HeightProperty, height, ShapeDuration);
        Animate(Notch, NotchShape.PillWidthProperty, width, ShapeDuration);
        Animate(Notch, NotchShape.PillHeightProperty, height, ShapeDuration);
        Animate(Notch, NotchShape.BottomRadiusProperty,
            expanded ? ExpandedRadius : CollapsedRadius, ShapeDuration);
    }

    /// <summary>
    /// Re-measures and re-grows the already-open panel. A plain SetExpanded(true) only fires on the
    /// collapsed-to-expanded transition, so it never runs again while a note is added to a panel
    /// that is already sitting open -- the pill would otherwise keep clipping the growing list at
    /// whatever height it happened to open at.
    /// </summary>
    private void ResizeForContentChange()
    {
        if (!_expanded)
            return;

        // The new note's container isn't generated by the ItemsControl until the next layout pass,
        // so measuring synchronously here would still see the list as it was before the change.
        // Loaded priority runs after that pass but before anything gets painted.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_expanded)
                return;

            _expandedHeight = MeasureExpandedHeight();
            ResizePill(true);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Asks the expanded panel how tall it needs to be, rather than growing the pill to a figure
    /// picked in advance. The content host clips, so a pill even slightly shorter than its panel
    /// shaves the bottom off the transport buttons.
    ///
    /// Measured on each expand instead of once, because the answer changes: a live stream has no
    /// timeline, so its progress row collapses and the panel comes out shorter.
    /// </summary>
    private double MeasureExpandedHeight()
    {
        ExpandedLayer.Measure(new Size(ExpandedWidth, double.PositiveInfinity));

        // DesiredSize covers the layer's own margins. Rounded up so a fractional line height
        // cannot leave the last row a hair short of fitting.
        var measured = Math.Ceiling(ExpandedLayer.DesiredSize.Height);
        return measured > 0 ? measured : FallbackExpandedHeight;
    }

    /// <summary>
    /// Runs the now-playing bars, but only while there is something to see: paused music flattens
    /// them, and a hidden or expanded pill stops them outright rather than leaving four looping
    /// animations ticking behind an invisible layer.
    /// </summary>
    private void UpdateEqualizer()
    {
        var running = _shown && !_expanded && _media.IsPlaying;
        if (running == _barsRunning)
            return;

        _barsRunning = running;

        for (var i = 0; i < _bars.Length; i++)
        {
            var bar = _bars[i];

            if (!running)
            {
                // Handing null to BeginAnimation is what releases the property back to its local
                // value; without it the last animated height sticks and the bar cannot be set.
                bar.BeginAnimation(HeightProperty, null);
                bar.Height = BarRestingHeight;
                continue;
            }

            var (peak, periodMs, phaseMs) = BarBeats[i];
            bar.BeginAnimation(HeightProperty, new DoubleAnimation(BarRestingHeight, peak,
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
    /// Tints the bars with the artwork's dominant colour. Crossfaded rather than swapped: the
    /// artwork itself cuts between tracks, but four coloured bars snapping to an unrelated hue in
    /// the corner of the eye reads as a glitch.
    /// </summary>
    private void UpdateBarColour() =>
        _barBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(ArtworkAccent.FromPng(_media.Artwork), AccentDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

    private static void Animate(IAnimatable target, DependencyProperty property, double to, Duration duration) =>
        target.BeginAnimation(property, new DoubleAnimation(to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

    public void CloseForExit()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _hoverTimer.Stop();
        _progressTimer.Stop();
        Close();
    }
}
