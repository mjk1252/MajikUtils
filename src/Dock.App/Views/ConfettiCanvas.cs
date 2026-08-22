using System.Windows;
using System.Windows.Media;

namespace Dock.App.Views;

/// <summary>
/// Confetti, falling through the back of the pill.
///
/// Drawn rather than composed: a hundred rotating rectangles as a hundred WPF elements is a hundred
/// measure passes a frame on a window that is already animating its own size, and the whole thing
/// is a single <see cref="OnRender"/> with no layout in it at all. The particles are plain structs
/// in an array and never allocate after the first burst.
///
/// It falls for as long as the birthday is up, which is until somebody dismisses it. The rendering
/// hook is attached only while that is true and detached the moment the last piece lands, so the
/// idle cost when there is no birthday is exactly nothing -- and the cost while there is one is a
/// few dozen rectangles a frame inside a pill.
///
/// It draws behind everything in the pill and is never hit-testable, so nothing here can come
/// between the pointer and a control.
/// </summary>
public sealed class ConfettiCanvas : FrameworkElement
{
    /// <summary>
    /// How many pieces are in the air at once. Low, deliberately: the pill is 34px tall when it is
    /// collapsed, and confetti dense enough to look right over a full-screen panel is a solid band
    /// of colour across a strip that size.
    /// </summary>
    private const int Capacity = 70;

    /// <summary>Pieces per second while a burst is emitting.</summary>
    private const double EmissionRate = 28;

    /// <summary>
    /// Party colours, and deliberately not the theme's.
    ///
    /// Everything else in the app takes its colour from the artwork or from settings, and this is
    /// the one thing that should not: confetti tinted to match a dark island is grey paper falling
    /// past. It is the only element here allowed to clash, because clashing is what it is for.
    /// </summary>
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xFF, 0x5E, 0x7A),
        Color.FromRgb(0xFF, 0xC8, 0x3D),
        Color.FromRgb(0x3E, 0xCF, 0x8E),
        Color.FromRgb(0x4C, 0xA6, 0xFF),
        Color.FromRgb(0xC4, 0x7A, 0xFF),
        Color.FromRgb(0xFF, 0xFF, 0xFF)
    ];

    /// <summary>Frozen up front: an unfrozen brush is re-realised by the renderer on every frame.</summary>
    private static readonly Brush[] Brushes = Palette.Select(Frozen).ToArray();

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    private readonly Random _random = new();
    private Piece[] _pieces = [];
    private int _live;

    private bool _emitting;
    private DateTime _lastFrame;
    private double _owed;
    private bool _hooked;

    public ConfettiCanvas()
    {
        // Never in the way of a click. The pill underneath is what the pointer is aiming at, and
        // this covers all of it.
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Starts it falling, and keeps it falling until <see cref="Stop"/>. Idempotent -- calling it
    /// again while it is already running changes nothing, which is what lets the island call it
    /// from every edge that might have turned the birthday on without tracking which of them did.
    /// </summary>
    public void Start()
    {
        if (_emitting)
            return;

        _emitting = true;
        Visibility = Visibility.Visible;
        Hook();
    }

    /// <summary>
    /// Stops at once, pieces and all. What dismissing the birthday calls: the confetti is part of
    /// the thing being dismissed, so leaving the last second of it falling would read as the button
    /// not having worked.
    /// </summary>
    public void Stop()
    {
        _emitting = false;
        _live = 0;
        _owed = 0;
        Unhook();
        Visibility = Visibility.Collapsed;
    }

    private void Hook()
    {
        if (_hooked)
            return;

        _lastFrame = DateTime.UtcNow;
        CompositionTarget.Rendering += OnFrame;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked)
            return;

        CompositionTarget.Rendering -= OnFrame;
        _hooked = false;
    }

    /// <summary>
    /// One frame: age everything, emit whatever the elapsed time is owed, and ask for a repaint.
    ///
    /// Timed off the wall clock rather than off a frame count, because the rendering event fires at
    /// whatever rate the compositor manages and confetti that falls at half speed when another
    /// window is busy looks broken rather than slow.
    /// </summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;

        // Clamped, so coming back from a machine that was asleep does not teleport every piece off
        // the bottom of the pill in a single step.
        var elapsed = Math.Min((now - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = now;

        var height = ActualHeight;
        var width = ActualWidth;

        if (width <= 0 || height <= 0)
            return;

        Advance(elapsed, width, height);

        if (_emitting)
            Emit(elapsed, width);

        // Nothing emitting and nothing left in the air. Unhooking here rather than on a timer is
        // what keeps this class free once it has stopped: no birthday means no rendering hook at
        // all, rather than a frame handler that returns early sixty times a second forever.
        else if (_live == 0)
        {
            Unhook();
            Visibility = Visibility.Collapsed;
            return;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Moves every live piece, compacting the array as it goes: a piece that has fallen out of the
    /// bottom is swapped with the last live one and the count drops. No allocation, no ordering to
    /// preserve, and the live pieces stay contiguous for the draw.
    /// </summary>
    private void Advance(double elapsed, double width, double height)
    {
        for (var i = 0; i < _live; i++)
        {
            ref var piece = ref _pieces[i];

            piece.Y += piece.FallSpeed * elapsed;
            piece.Angle += piece.Spin * elapsed;

            // A drift that reverses with the angle, so a piece appears to be turning over in the
            // air rather than sliding sideways while spinning independently of it.
            piece.X += Math.Sin(piece.Angle) * piece.Drift * elapsed;

            if (piece.Y - piece.Size <= height && piece.X > -piece.Size && piece.X < width + piece.Size)
                continue;

            _pieces[i] = _pieces[--_live];
            i--;
        }
    }

    /// <summary>
    /// Releases this frame's share of the emission rate, carrying the fraction over rather than
    /// rounding it away -- at 28 a second and 60 frames a second, rounding down emits nothing at all.
    /// </summary>
    private void Emit(double elapsed, double width)
    {
        _owed += EmissionRate * elapsed;

        while (_owed >= 1 && _live < Capacity)
        {
            _owed -= 1;

            if (_pieces.Length < Capacity)
                Array.Resize(ref _pieces, Capacity);

            _pieces[_live++] = new Piece
            {
                X = _random.NextDouble() * width,

                // Started above the top edge and staggered, so a burst arrives as a fall already in
                // progress rather than as a row of pieces appearing along the top in one frame.
                Y = -_random.NextDouble() * 40 - 4,

                Size = 3 + _random.NextDouble() * 3.5,
                FallSpeed = 45 + _random.NextDouble() * 70,
                Drift = 12 + _random.NextDouble() * 26,
                Angle = _random.NextDouble() * Math.PI * 2,
                Spin = (_random.NextDouble() - 0.5) * 7,
                Brush = _random.Next(Brushes.Length)
            };
        }
    }

    /// <summary>
    /// Draws the live pieces as rotated rectangles, taller than wide so the rotation reads as paper
    /// turning over rather than as a square wobbling.
    /// </summary>
    protected override void OnRender(DrawingContext context)
    {
        for (var i = 0; i < _live; i++)
        {
            ref var piece = ref _pieces[i];

            var degrees = piece.Angle * 180 / Math.PI;
            context.PushTransform(new RotateTransform(degrees, piece.X, piece.Y));

            context.DrawRectangle(
                Brushes[piece.Brush],
                null,
                new Rect(piece.X - piece.Size / 2, piece.Y - piece.Size, piece.Size, piece.Size * 2));

            context.Pop();
        }
    }

    private struct Piece
    {
        public double X;
        public double Y;
        public double Size;
        public double FallSpeed;
        public double Drift;
        public double Angle;
        public double Spin;
        public int Brush;
    }
}
