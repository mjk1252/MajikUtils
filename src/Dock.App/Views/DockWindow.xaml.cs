using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Core.ViewModels;
using Dock.Interop.Shell;
using Dock.Interop.Windowing;

namespace Dock.App.Views;

public partial class DockWindow : Window
{
    private const int PanicHotkeyId = 1;
    private const int ClipboardHotkeyId = 2;

    public const double MinIconSize = 32;
    public const double MaxIconSize = 96;

    // Per-window (per-monitor), not on the shared DockViewModel -- every monitor's dock has its
    // own DockWindow instance but they all share one DockViewModel, so icon size has to live
    // here for dragging one monitor's dock to resize only that monitor's icons.
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(DockWindow), new PropertyMetadata(52.0));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, Math.Clamp(value, MinIconSize, MaxIconSize));
    }

    public const double MinDockPadding = 0;
    public const double MaxDockPadding = 28;
    public const double MinIconSpacing = 0;
    public const double MaxIconSpacing = 24;

    public static readonly DependencyProperty DockPaddingProperty = DependencyProperty.Register(
        nameof(DockPadding), typeof(double), typeof(DockWindow),
        new PropertyMetadata(6.0, OnSpacingChanged));

    /// <summary>Padding inside the glass panel, along the dock's long axis. See <see cref="ApplySpacing"/>.</summary>
    public double DockPadding
    {
        get => (double)GetValue(DockPaddingProperty);
        set => SetValue(DockPaddingProperty, Math.Clamp(value, MinDockPadding, MaxDockPadding));
    }

    public static readonly DependencyProperty IconSpacingProperty = DependencyProperty.Register(
        nameof(IconSpacing), typeof(double), typeof(DockWindow),
        new PropertyMetadata(4.0, OnSpacingChanged));

    /// <summary>Gap on each side of every dock icon.</summary>
    public double IconSpacing
    {
        get => (double)GetValue(IconSpacingProperty);
        set => SetValue(IconSpacingProperty, Math.Clamp(value, MinIconSpacing, MaxIconSpacing));
    }

    public const double MinDockMargin = 0;
    public const double MaxDockMargin = 64;

    public static readonly DependencyProperty DockMarginProperty = DependencyProperty.Register(
        nameof(DockMargin), typeof(double), typeof(DockWindow),
        new PropertyMetadata(12.0, OnDockMarginChanged));

    /// <summary>
    /// Gap between the dock and its screen edge. Unlike the other two spacing settings this one is
    /// not consumed by any binding -- it is applied by ApplyPillRegionAndPosition, which places the
    /// window and sizes the appbar reservation, so a change has to re-run that.
    /// </summary>
    public double DockMargin
    {
        get => (double)GetValue(DockMarginProperty);
        set => SetValue(DockMarginProperty, Math.Clamp(value, MinDockMargin, MaxDockMargin));
    }

    private static void OnDockMarginChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DockWindow)d).ApplyPillRegionAndPosition();

    public const double MinAppClearance = 0;
    public const double MaxAppClearance = 64;

    public static readonly DependencyProperty AppClearanceProperty = DependencyProperty.Register(
        nameof(AppClearance), typeof(double), typeof(DockWindow),
        new PropertyMetadata(12.0, OnDockMarginChanged));

    /// <summary>
    /// Reserved space between the pill's inner edge and where a maximized app window's usable area
    /// starts. Purely an appbar-thickness input -- it does not move the pill itself, only how much
    /// of the screen other windows are told to avoid on the far side of it.
    /// </summary>
    public double AppClearance
    {
        get => (double)GetValue(AppClearanceProperty);
        set => SetValue(AppClearanceProperty, Math.Clamp(value, MinAppClearance, MaxAppClearance));
    }

    // The two Thickness properties below are what the XAML actually binds to. They exist because
    // a Thickness has to be built from the scalar setting *and* the dock's orientation -- the long
    // axis is horizontal when docked to the bottom and vertical when docked to a side -- which is
    // not something a plain binding can express.
    public static readonly DependencyProperty GlassPaddingProperty = DependencyProperty.Register(
        nameof(GlassPadding), typeof(Thickness), typeof(DockWindow),
        new PropertyMetadata(new Thickness(6, 2, 6, 2)));

    public Thickness GlassPadding
    {
        get => (Thickness)GetValue(GlassPaddingProperty);
        private set => SetValue(GlassPaddingProperty, value);
    }

    public static readonly DependencyProperty ItemSpacingProperty = DependencyProperty.Register(
        nameof(ItemSpacing), typeof(Thickness), typeof(DockWindow),
        new PropertyMetadata(new Thickness(4, 0, 4, 0)));

    public Thickness ItemSpacing
    {
        get => (Thickness)GetValue(ItemSpacingProperty);
        private set => SetValue(ItemSpacingProperty, value);
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DockWindow)d).ApplySpacing();

    /// <summary>
    /// Projects the two scalar spacing settings onto the dock's current orientation. The glass
    /// panel's cross-axis padding is deliberately a third of the long-axis value rather than equal
    /// to it: the cross axis is what makes the bar look thick, and matching the ends would bloat
    /// it. A third keeps the default of 6 rendering as the original hard-coded 6,2.
    /// </summary>
    private void ApplySpacing()
    {
        var along = DockPadding;
        var across = Math.Round(along / 3);
        var gap = IconSpacing;

        if (_position == DockPosition.Bottom)
        {
            GlassPadding = new Thickness(along, across, along, across);
            ItemSpacing = new Thickness(gap, 0, gap, 0);
        }
        else
        {
            GlassPadding = new Thickness(across, along, across, along);
            ItemSpacing = new Thickness(0, gap, 0, gap);
        }
    }

    // Marks a drag as originating from inside the dock itself (Recent Files, Shelf) so OnDrop
    // can tell it apart from a real Explorer drag -- otherwise dragging a recent file and
    // releasing it anywhere over the dock body (easy to do by accident, since the flyouts open
    // right above it) would get interpreted as "pin this as an app."
    private const string InternalDragFormat = "Dock.InternalFileDrag";

    // How many poll ticks the real cursor position must stay over the icon before the
    // switcher opens, and how many it must stay away (from both icon and popup) before it
    // closes. Deciding this from the polled cursor position -- rather than from WPF's
    // MouseEnter/MouseLeave events -- matters because opening the switcher Popup creates a
    // second top-level window that can itself generate a spurious MouseLeave on the dock the
    // instant it appears, which previously made the switcher flicker open/closed in a loop.
    private const int WindowSwitcherOpenTicks = 3;
    private const int WindowSwitcherCloseTicks = 2;

    private readonly DockViewModel _viewModel;
    private readonly MonitorSnapshot _monitor;
    public IntPtr MonitorHandle => _monitor.Handle;
    private readonly DockPosition _position;
    private readonly bool _enableGlobalHooks;
    private readonly IWingetService? _wingetService;
    private readonly int _accentRgb;
    private readonly byte _tintAlpha;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _wingetDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _windowSwitcherPollTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private DockItemViewModel? _windowSwitcherItem;
    private readonly Dictionary<IntPtr, IntPtr> _rowThumbnails = [];
    private FrameworkElement? _hoverAnchor;
    private DockItemViewModel? _hoverItem;
    private int _hoverTicks;
    private int _awayTicks;
    private System.Windows.Point? _fileDragStart;
    private Popup? _pendingTogglePopup;
    private bool _pendingTogglePopupWasOpen;
    private StackItemViewModel? _openStackItem;
    private StackItemViewModel? _recentlyClosedStackItem;
    private DateTime _recentlyClosedStackItemAt;

    private const double MaxMagnifyScale = 1.4;

    // The Popup's HWND is sized to this Canvas, so it also bounds how far the fan can reach before
    // entries get clipped: the furthest entry sits at FanReachDistance + (MaxFanEntries-1) *
    // FanRadialStep from the anchor, and the anchor itself sits 40px in from the docked edge.
    // Keep these comfortably ahead of that or the last entries silently disappear.
    private const double FanCanvasWidth = 780;
    private const double FanCanvasHeight = 780;

    // Entry 0 sits FanReachDistance from the icon, aligned exactly on the icon's own axis (above
    // it for a bottom dock, level with it for a side dock). Each later entry moves further from
    // the icon (radius grows by FanRadialStep every step) while also rotating a few degrees, so
    // entries keep climbing/receding rather than curving back toward the dock the way a
    // constant-radius arc would near its own apex.
    // Half-extents must track the fan entry's DataTemplate in DockWindow.xaml, whose Grid is a
    // fixed 108 x 86. They are what re-centres a tile on its computed radius point.
    //
    // FanAngleStepDeg is only a few degrees, so consecutive entries are separated almost entirely
    // radially -- their centres end up ~FanRadialStep apart, near-vertically for a bottom dock.
    // A step smaller than the full tile height therefore makes each entry's name overlap the next
    // entry's icon, which is exactly what a step of 35 (and then 46) did. FanRadialStep must stay
    // >= 2*FanItemHalfHeight, with the remainder as the visible gap between tiles.
    private const double FanReachDistance = 3;
    private const double FanRadialStep = 96;
    private const double FanAngleStepDeg = 5.0;
    private const double FanItemHalfWidth = 54;
    private const double FanItemHalfHeight = 43;
    private uint _taskbarCreatedMessage;
    private bool _appBarRegistered;

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    public event Action? PanicHotkeyPressed;
    public event Action? ExplorerRestarted;
    public event Action<double>? IconSizeChanged;

    public DockWindow(DockViewModel viewModel, MonitorSnapshot monitor, DockPosition position = DockPosition.Bottom,
        bool enableGlobalHooks = false, IWingetService? wingetService = null,
        string accentColor = "#1E1E1E", int tintOpacityPercent = 9)
    {
        _viewModel = viewModel;
        _monitor = monitor;
        _position = position;
        _enableGlobalHooks = enableGlobalHooks;
        _wingetService = wingetService;
        _accentRgb = ParseRgb(accentColor);
        // Settings expose the tint as 0-60%; acrylic wants an 0-255 alpha.
        _tintAlpha = (byte)(Math.Clamp(tintOpacityPercent, 0, 100) * 255 / 100);
        DataContext = viewModel;

        // Land within this monitor's own bounds (converted using ITS OWN DPI, not the primary
        // monitor's) rather than some arbitrary off-screen point. WPF picks a window's initial
        // per-monitor DPI context from where it's created; landing outside every monitor risks
        // it defaulting to the primary monitor's scale, which is what made the dock render at
        // the wrong size on a secondary monitor with different DPI/resolution.
        Left = _monitor.Bounds.Left / _monitor.DpiScale;
        Top = _monitor.Bounds.Top / _monitor.DpiScale;

        InitializeComponent();
        ApplyOrientation();

        // The DP defaults are already correct for a bottom dock, but a side dock needs the
        // Thicknesses transposed before anything renders.
        ApplySpacing();

        var accentR = (byte)((_accentRgb >> 16) & 0xFF);
        var accentG = (byte)((_accentRgb >> 8) & 0xFF);
        var accentB = (byte)(_accentRgb & 0xFF);

        Resources["FlyoutBackgroundBrush"] = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(0xEE, accentR, accentG, accentB));

        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _wingetDebounceTimer.Tick += OnWingetDebounceElapsed;
        _windowSwitcherPollTimer.Tick += OnWindowSwitcherPollTick;

        // Watch IsOpen itself rather than the Closed event -- see OnStackFanIsOpenChanged.
        System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(Popup.IsOpenProperty, typeof(Popup))
            .AddValueChanged(StackFanFlyout, OnStackFanIsOpenChanged);

        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            _wingetDebounceTimer.Stop();
            _windowSwitcherPollTimer.Stop();
            ClearRowThumbnails();
        };
    }

    private static int ParseRgb(string hex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            return (color.R << 16) | (color.G << 8) | color.B;
        }
        catch
        {
            return 0x1E1E1E;
        }
    }

    private void ApplyOrientation()
    {
        if (_position == DockPosition.Bottom)
            return;

        ContentStack.Orientation = Orientation.Vertical;

        var verticalPanel = (ItemsPanelTemplate)FindResource("VerticalPanel");
        PinnedItemsControl.ItemsPanel = verticalPanel;

        foreach (var separator in new[] { SeparatorStats, Separator1, Separator2, Separator3 })
        {
            var width = separator.Width;
            separator.Width = double.NaN;
            separator.Height = width;
            separator.Margin = new Thickness(10, 6, 10, 6);
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockTimeText.Text = now.ToString("h:mm tt");
        ClockDateText.Text = now.ToString("ddd, MMM d");
        FlyoutTimeText.Text = now.ToString("h:mm tt");
        FlyoutDateText.Text = now.ToString("dddd, MMMM d, yyyy");
    }

    /// <summary>
    /// All the dock's toggle-on-click Popups use StaysOpen="False", so clicking anywhere outside
    /// a popup -- including another dock button, the dock body, or another app entirely --
    /// closes it on its own via WPF's mouse-capture-based dismissal. That's what gives us "click
    /// another menu closes this one" and "click elsewhere closes it" for free. The gotcha:
    /// clicking the SAME button that opened an already-open popup fires the popup's auto-dismiss
    /// on mouse-DOWN, before our own click handler runs on mouse-UP -- so by the time the click
    /// handler asks "is it open?", it may already have been auto-closed by this very click,
    /// making a naive `IsOpen = !IsOpen` immediately reopen it (or, depending on exactly when the
    /// dismissal lands relative to our handler, leave it open when it should've closed -- the
    /// ordering isn't reliably one way or the other). Deciding "was it open" needs to happen
    /// before the same click's dismissal can run at all: ArmToggle captures the true state on
    /// PreviewMouseLeftButtonDown (always the first thing to see this click), and CommitToggle
    /// inverts THAT captured state on the later MouseLeftButtonUp, ignoring whatever IsOpen
    /// happens to read by then.
    /// </summary>
    private void ArmToggle(Popup popup)
    {
        _pendingTogglePopup = popup;
        _pendingTogglePopupWasOpen = popup.IsOpen;
    }

    private bool CommitToggle(Popup popup)
    {
        var wasOpen = ReferenceEquals(_pendingTogglePopup, popup) ? _pendingTogglePopupWasOpen : popup.IsOpen;
        _pendingTogglePopup = null;
        popup.IsOpen = !wasOpen;
        return popup.IsOpen;
    }

    private void OnClockWidgetPreviewDown(object sender, MouseButtonEventArgs e) => ArmToggle(ClockFlyout);
    private void OnOverflowChevronPreviewDown(object sender, MouseButtonEventArgs e) => ArmToggle(OverflowFlyout);
    private void OnLauncherButtonPreviewDown(object sender, MouseButtonEventArgs e) => ArmToggle(LauncherFlyout);
    private void OnRecentButtonPreviewDown(object sender, MouseButtonEventArgs e) => ArmToggle(RecentFlyout);
    private void OnClipboardButtonPreviewDown(object sender, MouseButtonEventArgs e) => ArmToggle(ClipboardFlyout);
    private void OnShelfButtonPreviewDown(object sender, MouseButtonEventArgs e) => ArmToggle(ShelfFlyout);

    private void OnClockClick(object sender, MouseButtonEventArgs e)
    {
        // Prefer relaying to the real taskbar clock, which opens Windows' actual Notification
        // Center (calendar + notifications) -- falls back to our own simple flyout if tray
        // reading hasn't found it (e.g. this build's automation path failed).
        if (_viewModel.ClockTrayIcon is { } clockIcon)
            clockIcon.ClickCommand.Execute(null);
        else
            CommitToggle(ClockFlyout);
    }

    private void OnOverflowChevronClick(object sender, MouseButtonEventArgs e)
    {
        // Prefer relaying to the real "Show Hidden Icons" chevron, which opens Windows' actual
        // overflow flyout -- falls back to our own icon-grid popup if it wasn't found.
        if (_viewModel.ChevronTrayIcon is { } chevronIcon)
            chevronIcon.ClickCommand.Execute(null);
        else
            CommitToggle(OverflowFlyout);
    }

    private void OnGlassMouseMove(object sender, MouseEventArgs e)
    {
        if (ActualWidth <= 0)
            return;

        var position = e.GetPosition(GlassBorder);
        var ratio = Math.Clamp(position.X / ActualWidth, 0.05, 0.95);
        RimStopMid.Offset = ratio;

        UpdateDockMagnification(position);
    }

    private void OnGlassMouseLeave(object sender, MouseEventArgs e)
    {
        ResetDockMagnification();
    }

    /// <summary>
    /// macOS-style continuous magnification: every icon's scale is a Gaussian falloff of its
    /// distance from the cursor along the dock's main axis, recomputed on every MouseMove instead
    /// of the old binary "the one icon under the cursor jumps to 1.25x" trigger -- so icons next
    /// to the one under the cursor visibly grow too, tapering off with distance, and everything
    /// glides continuously as the cursor moves instead of snapping between two fixed scales.
    /// Purely a RenderTransform effect (no reflow), same as the click-bounce animation already
    /// relies on for this TransformGroup.
    /// </summary>
    private void UpdateDockMagnification(System.Windows.Point cursorPos)
    {
        var isVertical = _position != DockPosition.Bottom;
        var sigma = IconSize * 0.9;

        foreach (var icon in GetMagnifiableIcons())
        {
            var center = icon.TranslatePoint(new System.Windows.Point(icon.ActualWidth / 2, icon.ActualHeight / 2), GlassBorder);
            var distance = isVertical ? cursorPos.Y - center.Y : cursorPos.X - center.X;
            var scale = 1.0 + (MaxMagnifyScale - 1.0) * Math.Exp(-(distance * distance) / (2 * sigma * sigma));

            if (icon.RenderTransform is TransformGroup { Children.Count: >= 1 } group &&
                group.Children[0] is ScaleTransform scaleTransform)
            {
                // Direct assignment (no animation) so scale tracks the cursor with zero lag.
                // Any leftover animation clock (e.g. from a prior ResetDockMagnification) must be
                // cleared first -- WPF gives an active AnimationClock priority over local-value
                // SetValue calls even after it finishes (FillBehavior.HoldEnd), so without this
                // the assignments below would be silently ignored and the icon would appear stuck.
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scaleTransform.ScaleX = scale;
                scaleTransform.ScaleY = scale;
            }
        }
    }

    private void ResetDockMagnification()
    {
        foreach (var icon in GetMagnifiableIcons())
        {
            if (icon.RenderTransform is not TransformGroup { Children.Count: >= 1 } group ||
                group.Children[0] is not ScaleTransform scaleTransform)
                continue;

            // Plain assignment, not BeginAnimation -- an animated reset leaves an active clock
            // that would then block every future direct SetValue in UpdateDockMagnification (see
            // the comment there), permanently freezing magnification after the first mouse-leave.
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scaleTransform.ScaleX = 1.0;
            scaleTransform.ScaleY = 1.0;
        }
    }

    /// <summary>
    /// Yields the styled inner Border (the one whose RenderTransform actually holds the
    /// ScaleTransform) for every realized item in the pinned-apps and stacks rows -- the fixed-size
    /// outer Border used for hit-testing (see the comment on PinnedItemsControl's DataTemplate)
    /// deliberately never scales, so magnification only ever touches this inner one.
    /// </summary>
    private IEnumerable<Border> GetMagnifiableIcons()
    {
        foreach (var control in new ItemsControl[] { PinnedItemsControl, StacksItemsControl })
        {
            for (var i = 0; i < control.Items.Count; i++)
            {
                if (control.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter)
                    continue;

                if (VisualTreeHelper.GetChildrenCount(presenter) == 0)
                    continue;

                if (VisualTreeHelper.GetChild(presenter, 0) is Border { Child: Border inner })
                {
                    EnsureMutableIconTransform(inner);
                    yield return inner;
                }
            }
        }
    }

    /// <summary>
    /// WPF's BAML compiler freezes literal, binding-free Freezable object graphs as a load-time
    /// optimization -- and in practice that applies unpredictably even to inline DataTemplate
    /// content (AnimateBounce's TranslateTransform on this exact TransformGroup works fine; the
    /// ScaleTransform sitting right next to it in the same group throws "sealed or frozen" on both
    /// direct writes and BeginAnimation). Rather than rely on any particular XAML shape staying
    /// unfrozen, this replaces the icon's RenderTransform with a freshly code-constructed,
    /// guaranteed-mutable TransformGroup the first time it's touched.
    /// </summary>
    private static void EnsureMutableIconTransform(Border icon)
    {
        if (icon.RenderTransform is TransformGroup { Children.Count: >= 2 } group &&
            group.Children[0] is ScaleTransform { IsFrozen: false })
        {
            return;
        }

        icon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1, 1), new TranslateTransform(0, 0) }
        };
    }

    private void OnResizeHandleDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        // Drag handle sits at the dock's top edge -- dragging up (negative Y) grows it, dragging
        // down shrinks it, matching how you'd resize any panel from its top edge. IconSize's
        // setter clamps, so no need to clamp here too.
        IconSize -= e.VerticalChange;
    }

    private void OnResizeHandleDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        IconSizeChanged?.Invoke(IconSize);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowStyler.MakeNonActivatingToolWindow(hwnd);

        // Has to happen here, not in the constructor: there is no HWND to composite until the
        // window is sourced. Without this the window's Background="Transparent" pixels have an
        // alpha channel nothing honours, and DWM renders the whole dock flat black -- so the
        // fallback paints the accent colour opaque rather than leaving it black.
        if (!WindowStyler.EnableAcrylic(hwnd, _accentRgb, _tintAlpha))
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                (byte)((_accentRgb >> 16) & 0xFF), (byte)((_accentRgb >> 8) & 0xFF), (byte)(_accentRgb & 0xFF)));
        }

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        if (_enableGlobalHooks)
        {
            WindowStyler.RegisterPanicHotkey(hwnd, PanicHotkeyId);
            WindowStyler.RegisterClipboardHotkey(hwnd, ClipboardHotkeyId);
            WindowStyler.AddClipboardListener(hwnd);
            _taskbarCreatedMessage = WindowStyler.RegisterTaskbarCreatedMessage();
        }

        Closed += (_, _) =>
        {
            if (_enableGlobalHooks)
            {
                WindowStyler.UnregisterHotkey(hwnd, PanicHotkeyId);
                WindowStyler.UnregisterHotkey(hwnd, ClipboardHotkeyId);
                WindowStyler.RemoveClipboardListener(hwnd);
            }

            if (_appBarRegistered)
                AppBarService.Unregister(hwnd);
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;

        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        if (_enableGlobalHooks)
        {
            if (msg == WindowStyler.WM_HOTKEY && wParam.ToInt32() == PanicHotkeyId)
            {
                PanicHotkeyPressed?.Invoke();
            }
            else if (msg == WindowStyler.WM_HOTKEY && wParam.ToInt32() == ClipboardHotkeyId)
            {
                ClipboardFlyout.IsOpen = !ClipboardFlyout.IsOpen;
            }
            else if (msg == WindowStyler.WM_CLIPBOARDUPDATE)
            {
                CaptureClipboardText();
            }
            else if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
            {
                ExplorerRestarted?.Invoke();
            }
        }

        return IntPtr.Zero;
    }

    private void CaptureClipboardText()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                    _viewModel.AddClipboardEntry(text);
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard is transiently locked by whichever app just wrote to it -- that write is
            // exactly what triggered this notification, so nothing to capture is actually lost.
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyPillRegionAndPosition();
        SizeChanged += (_, _) => ApplyPillRegionAndPosition();

        // Popups have their own NameScope, so ElementName bindings inside one can't resolve a
        // sibling outside it -- assign the placement target directly instead.
        ClockFlyout.PlacementTarget = ClockWidget;
        OverflowFlyout.PlacementTarget = OverflowChevron;
        LauncherFlyout.PlacementTarget = LauncherButton;
        RecentFlyout.PlacementTarget = RecentButton;
        ClipboardFlyout.PlacementTarget = ClipboardButton;
        ShelfFlyout.PlacementTarget = ShelfButton;
    }

    private void OnLauncherButtonClick(object sender, MouseButtonEventArgs e)
    {
        if (!CommitToggle(LauncherFlyout))
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (PresentationSource.FromVisual(LauncherSearchBox) is HwndSource popupSource)
            {
                WindowStyler.MakeActivatable(popupSource.Handle);
                WindowStyler.ForceForeground(popupSource.Handle);
            }

            LauncherSearchBox.Focus();
            Keyboard.Focus(LauncherSearchBox);
        }), DispatcherPriority.Input);
    }

    private void OnLauncherSearchChanged(object sender, TextChangedEventArgs e)
    {
        var query = LauncherSearchBox.Text;
        _viewModel.FilterLauncherApps(query);

        _wingetDebounceTimer.Stop();

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            _viewModel.ClearWingetResults();
            return;
        }

        _wingetDebounceTimer.Start();
    }

    private void OnWingetDebounceElapsed(object? sender, EventArgs e)
    {
        _wingetDebounceTimer.Stop();

        var wingetService = _wingetService;
        var query = LauncherSearchBox.Text;
        if (wingetService is null || string.IsNullOrWhiteSpace(query))
            return;

        _viewModel.BeginWingetSearch();

        System.Threading.Tasks.Task.Run(() => wingetService.Search(query))
            .ContinueWith(t =>
            {
                var results = t.IsCompletedSuccessfully ? t.Result : [];
                Dispatcher.Invoke(() => _viewModel.SetWingetResults(results));
            });
    }

    private void OnLauncherItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppLauncherItemViewModel item })
            return;

        item.LaunchCommand.Execute(null);
        LauncherFlyout.IsOpen = false;
    }

    private void OnRecentButtonClick(object sender, MouseButtonEventArgs e)
    {
        if (!CommitToggle(RecentFlyout))
            return;

        System.Threading.Tasks.Task.Run(() =>
        {
            var files = new RecentFilesProvider().GetRecentFiles(30);
            return _viewModel.BuildRecentFileItems(files);
        }).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                Dispatcher.Invoke(() => _viewModel.SetRecentFiles(t.Result));
        });
    }

    private void OnRecentFileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentFileItemViewModel item })
            return;

        item.OpenCommand.Execute(null);
        RecentFlyout.IsOpen = false;
    }

    private void OnFileDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        _fileDragStart = e.GetPosition(null);
    }

    /// <summary>
    /// True once the cursor has moved past WPF's standard drag threshold from the button-down
    /// point -- without this, ANY mouse move while the button happens to be held (a few pixels
    /// of hand tremor between mouse-down and mouse-up is unavoidable on basically every click)
    /// would start a drag. That blocking DragDrop.DoDragDrop call disrupts the popup's own
    /// outside-click dismissal tracking, which is what made Recent Files (and Shelf) stop
    /// closing on click-away after just clicking an item normally.
    /// </summary>
    private bool HasExceededDragThreshold(MouseEventArgs e)
    {
        if (_fileDragStart is not { } start)
            return false;

        var current = e.GetPosition(null);
        return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void OnRecentFileMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !HasExceededDragThreshold(e))
            return;

        _fileDragStart = null;

        if (sender is not FrameworkElement { DataContext: RecentFileItemViewModel item } element)
            return;

        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { item.File.Path });
        data.SetData(InternalDragFormat, true);
        DragDrop.DoDragDrop(element, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
    }

    private void OnClipboardButtonClick(object sender, MouseButtonEventArgs e)
    {
        CommitToggle(ClipboardFlyout);
    }

    private void OnClipboardItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ClipboardEntryViewModel item })
            return;

        item.CopyCommand.Execute(null);
        ClipboardFlyout.IsOpen = false;
    }

    private void OnClipboardClearClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ClearClipboardHistoryCommand.Execute(null);
        e.Handled = true;
    }

    private void OnShelfButtonClick(object sender, MouseButtonEventArgs e)
    {
        CommitToggle(ShelfFlyout);
    }

    private void OnShelfDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
            e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        foreach (var path in paths)
            _viewModel.AddToShelf(path);
    }

    private void OnShelfItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShelfItemViewModel item })
            return;

        var launcher = new ProcessAppLauncher();
        launcher.Launch(item.Path);
    }

    private void OnShelfItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShelfItemViewModel item })
            return;

        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Remove from shelf" };
        remove.Click += (_, _) => _viewModel.RemoveFromShelfCommand.Execute(item);
        menu.Items.Add(remove);

        ((FrameworkElement)sender).ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnShelfItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !HasExceededDragThreshold(e))
            return;

        _fileDragStart = null;

        if (sender is not FrameworkElement { DataContext: ShelfItemViewModel item } element)
            return;

        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { item.Path });
        data.SetData(InternalDragFormat, true);
        DragDrop.DoDragDrop(element, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
    }

    private void OnStackItemPreviewDown(object sender, MouseButtonEventArgs e)
    {
        // Unlike the ArmToggle-based flyouts, clicking the icon that's currently the popup's own
        // PlacementTarget never reaches this handler at all when the popup is open -- StaysOpen=
        // "False" dismisses it via mouse-capture release before the down event tunnels this far
        // down, so PreviewMouseLeftButtonDown simply doesn't fire on that click. All the actual
        // "was this the same stack" bookkeeping therefore has to live in OnStackFanIsOpenChanged
        // (which DOES reliably fire the moment the popup dismisses) and OnStackItemClick (whose
        // MouseLeftButtonUp fires every time, dismiss-click or not). Nothing to do here anymore --
        // kept as a no-op handler since the XAML still wires it up.
    }

    private void OnStackItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StackItemViewModel item } element)
            return;

        // If this exact stack's fan was open, this same click's mouse-down already dismissed it
        // (either WPF's StaysOpen="False" auto-dismiss or OnWindowPreviewMouseDown) and
        // OnStackFanIsOpenChanged recorded that. Treat this up as "close", not "reopen".
        if (ReferenceEquals(_recentlyClosedStackItem, item) &&
            (DateTime.UtcNow - _recentlyClosedStackItemAt) < TimeSpan.FromMilliseconds(400))
        {
            _recentlyClosedStackItem = null;
            return;
        }

        if(StackFanFlyout.IsOpen == true){
            StackFanFlyout.IsOpen = false;
            StackFanItems.ItemsSource = null;
            return;
        }

        ComputeFanPositions(item, element);

        // Force a fresh bind so each entry's ContentPresenter picks up its just-computed
        // Canvas.Left/Top rather than reusing containers positioned for the previous stack.
        StackFanItems.ItemsSource = null;
        StackFanItems.ItemsSource = item.Entries;
        StackFanFlyout.PlacementTarget = element;
        _openStackItem = item;
        StackFanFlyout.IsOpen = true;
    }

    /// <summary>
    /// Window-level (tunnel-root) mouse-down handler so we can close the stack fan deterministically
    /// when its own icon is clicked again, instead of relying on the Popup's own StaysOpen="False"
    /// auto-dismiss. Two things independently break the naive per-icon approach: (1) that built-in
    /// dismissal races the icon's own PreviewMouseLeftButtonDown/MouseLeftButtonUp pair, so IsOpen
    /// can already read false by the time OnStackItemClick runs with no reliable way to tell "just
    /// closed by this click" from "was already closed"; (2) entry 0 sits only 3-5px above the icon
    /// (by design), so the fan's 500x500 Popup rectangle geometrically overlaps the icon underneath
    /// it once open -- clicking the icon again then hit-tests to the popup itself (OriginalSource
    /// becomes "PopupRoot"), not the icon, so ancestor-based hit-testing can never identify it as
    /// the toggle icon at all. Comparing the raw screen point against the placement target's actual
    /// screen bounds sidesteps both: it doesn't care what OriginalSource WPF attributes the click
    /// to, and it runs here -- before the click reaches anything else -- so it can close the popup
    /// itself and let OnStackFanFlyoutClosed record the closure in time for OnStackItemClick to see.
    /// </summary>
    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!StackFanFlyout.IsOpen || _openStackItem is null)
            return;

        if (StackFanFlyout.PlacementTarget is not FrameworkElement icon)
            return;

        // Both corners go through PointToScreen: ActualWidth/Height are DIPs, but PointToScreen
        // yields device pixels, so pairing a device-pixel origin with a DIP size would under-size
        // the hit rect on any display above 100% scaling and miss clicks on the icon's right/bottom.
        var iconTopLeft = icon.PointToScreen(new System.Windows.Point(0, 0));
        var iconBottomRight = icon.PointToScreen(new System.Windows.Point(icon.ActualWidth, icon.ActualHeight));
        var iconBounds = new Rect(iconTopLeft, iconBottomRight);
        var clickScreenPos = PointToScreen(e.GetPosition(this));

        if (iconBounds.Contains(clickScreenPos))
        {
            StackFanFlyout.IsOpen = false;
        }
    }

    private void OnStackItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StackItemViewModel item })
            return;

        e.Handled = true;

        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Remove stack from dock" };
        remove.Click += (_, _) => _viewModel.RemoveStackCommand.Execute(item);
        menu.Items.Add(remove);

        ((FrameworkElement)sender).ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void OnStackFanEntryClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StackEntryViewModel entry })
            return;

        entry.OpenCommand.Execute(null);
        StackFanFlyout.IsOpen = false;
    }

    private void OnStackFanEntryMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !HasExceededDragThreshold(e))
            return;

        _fileDragStart = null;

        if (sender is not FrameworkElement { DataContext: StackEntryViewModel entry } element)
            return;

        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { entry.Path });
        data.SetData(InternalDragFormat, true);
        DragDrop.DoDragDrop(element, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
    }

    /// <summary>
    /// Records "the fan just closed, and it belonged to this stack" so the mouse-UP half of the
    /// very same click can tell a dismiss apart from a request to open a fresh fan.
    ///
    /// This deliberately hangs off the IsOpen property rather than the Popup's Closed event.
    /// Because StackFanFlyout sets PopupAnimation="Fade", WPF defers both the HWND teardown and
    /// the Closed event until the fade-out finishes -- so on a dismissing click the ordering is
    /// mouse-down -> IsOpen flips false -> mouse-up -> (~200ms later) Closed. Bookkeeping done in
    /// Closed therefore lands *after* OnStackItemClick has already run and, seeing no record of a
    /// recent closure and IsOpen already false, reopened the fan. That is what made the fan
    /// impossible to dismiss by clicking its own icon: every click re-opened it.
    ///
    /// IsOpen, by contrast, is set synchronously the instant the popup dismisses -- whether from
    /// our own explicit IsOpen=false or WPF's StaysOpen="False" auto-dismiss -- so the record is
    /// in place before the mouse-up is dispatched.
    /// </summary>
    private void OnStackFanIsOpenChanged(object? sender, EventArgs e)
    {
        if (StackFanFlyout.IsOpen)
            return;

        _recentlyClosedStackItem = _openStackItem;
        _recentlyClosedStackItemAt = DateTime.UtcNow;
        _openStackItem = null;
    }

    /// <summary>
    /// Entry 0 sits FanReachDistance from the icon, aligned exactly above it (Bottom dock) or
    /// level with it (Left/Right dock) -- radius_i = FanReachDistance + i*FanRadialStep, so each
    /// later entry is strictly further from the icon than the last, climbing higher for a bottom
    /// dock or receding further from the dock's edge for a side dock. theta_i = i*FanAngleStepDeg
    /// adds a slight rotation per step purely so entries fan sideways instead of stacking directly
    /// on top of one another along the same line out from the icon.
    ///
    /// StackFanFlyout uses Placement="Top", which aligns the popup's top-left corner with the
    /// clicked icon's top-left corner (not its center) -- so entry 0 landing directly above/beside
    /// the icon needs the anchor's canvas coordinate along the icon's own axis to be the icon's
    /// half-width/half-height, not an arbitrary constant.
    /// </summary>
    private void ComputeFanPositions(StackItemViewModel item, FrameworkElement anchorElement)
    {
        var count = item.Entries.Count;
        if (count == 0)
            return;

        // A tile is positioned by its centre and then pulled back by its half-extent, so anchoring
        // the fan exactly on the icon's own axis drives entry 0's left/top edge negative once the
        // tile is wider/taller than the icon -- and the Popup's HWND is sized to the Canvas, so
        // anything negative is clipped away rather than merely overhanging. Push the anchor in from
        // that edge by the half-extent and slide the whole Popup back by the same amount: entry 0
        // still lands over the icon, but now entirely inside the canvas.
        var iconCentreX = anchorElement.ActualWidth / 2;
        var iconCentreY = anchorElement.ActualHeight / 2;

        double anchorX, anchorY;
        switch (_position)
        {
            case DockPosition.Left:
                anchorX = 40;
                anchorY = Math.Max(iconCentreY, FanItemHalfHeight);
                break;
            case DockPosition.Right:
                anchorX = FanCanvasWidth - 40;
                anchorY = Math.Max(iconCentreY, FanItemHalfHeight);
                break;
            default:
                anchorX = Math.Max(iconCentreX, FanItemHalfWidth);
                anchorY = FanCanvasHeight - 40;
                break;
        }

        if (_position == DockPosition.Bottom)
        {
            StackFanFlyout.HorizontalOffset = iconCentreX - anchorX;
            StackFanFlyout.VerticalOffset = 0;
        }
        else
        {
            StackFanFlyout.HorizontalOffset = 0;
            StackFanFlyout.VerticalOffset = iconCentreY - anchorY;
        }

        for (var i = 0; i < count; i++)
        {
            var radius = FanReachDistance + i * FanRadialStep;
            var theta = i * FanAngleStepDeg * Math.PI / 180.0;

            double x, y;
            switch (_position)
            {
                case DockPosition.Left:
                    x = anchorX + radius * Math.Cos(theta);
                    y = anchorY + radius * Math.Sin(theta);
                    break;
                case DockPosition.Right:
                    x = anchorX - radius * Math.Cos(theta);
                    y = anchorY + radius * Math.Sin(theta);
                    break;
                default:
                    x = anchorX + radius * Math.Sin(theta);
                    y = anchorY - radius * Math.Cos(theta);
                    break;
            }

            item.Entries[i].FanOffsetX = x - FanItemHalfWidth;
            item.Entries[i].FanOffsetY = y - FanItemHalfHeight;
        }
    }

    private void ApplyPillRegionAndPosition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var widthPx = (int)(ActualWidth * dpiScale);
        var heightPx = (int)(ActualHeight * dpiScale);

        if (widthPx <= 0 || heightPx <= 0)
            return;

        var cornerRadiusPx = (int)(22 * dpiScale);
        WindowStyler.ApplyRoundedRegion(hwnd, widthPx, heightPx, cornerRadiusPx);

        var bounds = _monitor.Bounds;
        var marginPx = (int)(DockMargin * dpiScale);
        var clearancePx = (int)(AppClearance * dpiScale);
        int x, y;
        AppBarEdge edge;
        int thicknessPx;

        switch (_position)
        {
            case DockPosition.Left:
                x = bounds.Left + marginPx;
                y = bounds.Top + (bounds.Height - heightPx) / 2;
                edge = AppBarEdge.Left;
                thicknessPx = widthPx + marginPx + clearancePx;
                break;
            case DockPosition.Right:
                x = bounds.Right - widthPx - marginPx;
                y = bounds.Top + (bounds.Height - heightPx) / 2;
                edge = AppBarEdge.Right;
                thicknessPx = widthPx + marginPx + clearancePx;
                break;
            default:
                x = bounds.Left + (bounds.Width - widthPx) / 2;
                y = bounds.Bottom - heightPx - marginPx;
                edge = AppBarEdge.Bottom;
                thicknessPx = heightPx + marginPx + clearancePx;
                break;
        }

        WindowStyler.SetWindowPosition(hwnd, x, y);

        if (!_appBarRegistered)
        {
            AppBarService.Register(hwnd);
            _appBarRegistered = true;
        }

        AppBarService.Reposition(hwnd, bounds, edge, thicknessPx);
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        // A drag that started inside the dock (Recent Files, Shelf) landing back on the dock
        // body isn't a request to pin it -- see InternalDragFormat.
        if (e.Data.GetDataPresent(InternalDragFormat))
            return;

        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) &&
            e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
            {
                if (System.IO.Directory.Exists(path))
                    _viewModel.AddStack(path);
                else
                    _viewModel.AddPinned(path);
            }
        }
    }

    private void OnGlassRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled)
            return;

        var menu = new ContextMenu();
        var pin = new MenuItem { Header = "Pin an application..." };
        pin.Click += (_, _) => PromptAndPinApplication();
        menu.Items.Add(pin);

        ((FrameworkElement)sender).ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void PromptAndPinApplication()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            Title = "Pin an application"
        };

        if (dialog.ShowDialog(this) == true)
            _viewModel.AddPinned(dialog.FileName);
    }

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItemViewModel item } element)
            return;

        if (item.IsRunning && item.Windows.Count > 1)
        {
            _hoverAnchor = element;
            _hoverItem = item;
            _hoverTicks = WindowSwitcherOpenTicks;
            _awayTicks = 0;
            _windowSwitcherPollTimer.Start();
            ShowWindowSwitcher(item, element);
        }
        else
        {
            item.LaunchCommand.Execute(null);
            ReassertDockTopmost();

            if (element is Border { Child: FrameworkElement iconVisual })
                AnimateBounce(iconVisual);
        }
    }

    private void OnItemMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItemViewModel item } element)
            return;

        if (!item.IsRunning || item.Windows.Count <= 1)
            return;

        if (WindowSwitcherFlyout.IsOpen && !ReferenceEquals(_hoverItem, item))
        {
            // Already showing a switcher for a different app -- follow the cursor immediately
            // rather than making the user wait through the open delay again.
            ShowWindowSwitcher(item, element);
            _hoverTicks = WindowSwitcherOpenTicks;
        }
        else
        {
            _hoverTicks = 0;
        }

        _hoverAnchor = element;
        _hoverItem = item;
        _awayTicks = 0;
        _windowSwitcherPollTimer.Start();
    }

    private void OnItemMouseLeave(object sender, MouseEventArgs e)
    {
        // Deliberately no-op: closing is decided entirely by OnWindowSwitcherPollTick against
        // the real cursor position (see the comment on WindowSwitcherOpenTicks for why).
    }

    private void OnWindowSwitcherPollTick(object? sender, EventArgs e)
    {
        if (_hoverAnchor is null || _hoverItem is null)
        {
            _windowSwitcherPollTimer.Stop();
            return;
        }

        if (!GetCursorPos(out var cursorPoint))
            return;

        var cursor = new System.Windows.Point(cursorPoint.X, cursorPoint.Y);
        var overAnchor = _hoverAnchor.IsVisible && GetScreenRect(_hoverAnchor).Contains(cursor);
        var overPopup = WindowSwitcherFlyout.IsOpen && WindowSwitcherContent.IsVisible &&
                         GetScreenRect(WindowSwitcherContent).Contains(cursor);

        if (overAnchor || overPopup)
        {
            _awayTicks = 0;

            if (!WindowSwitcherFlyout.IsOpen)
            {
                _hoverTicks++;
                if (_hoverTicks >= WindowSwitcherOpenTicks)
                    ShowWindowSwitcher(_hoverItem, _hoverAnchor);
            }

            return;
        }

        _awayTicks++;
        if (_awayTicks < WindowSwitcherCloseTicks)
            return;

        WindowSwitcherFlyout.IsOpen = false;
        _windowSwitcherPollTimer.Stop();
        _hoverAnchor = null;
        _hoverItem = null;
    }

    /// <summary>
    /// Live preview via DWM thumbnails composited directly onto the switcher popup's own
    /// surface, rather than the earlier "bring the real window to front" peek -- that approach
    /// fought z-order/topmost races with the dock's own always-on-top window constantly. A DWM
    /// thumbnail is just a picture DWM draws into a rect; it never touches any window's z-order,
    /// so there's nothing left to race. Every row shows its own thumbnail at once (no hover
    /// needed) -- registered as each row's ThumbnailHost loads, since that's when we finally
    /// know its on-screen rect.
    /// </summary>
    private void OnThumbnailHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RunningWindow window } host)
            return;

        if (PresentationSource.FromVisual(WindowSwitcherContent) is not HwndSource popupSource)
            return;

        var thumbnailId = WindowStyler.RegisterThumbnail(popupSource.Handle, window.Handle);
        if (thumbnailId == IntPtr.Zero)
            return;

        _rowThumbnails[window.Handle] = thumbnailId;

        var dpiScale = popupSource.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var topLeft = host.TransformToAncestor(WindowSwitcherContent).Transform(new System.Windows.Point(0, 0));
        var left = (int)(topLeft.X * dpiScale);
        var top = (int)(topLeft.Y * dpiScale);
        var right = left + (int)(host.ActualWidth * dpiScale);
        var bottom = top + (int)(host.ActualHeight * dpiScale);

        WindowStyler.ShowThumbnail(thumbnailId, left, top, right, bottom);
    }

    private void OnThumbnailHostUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RunningWindow window })
            return;

        if (_rowThumbnails.Remove(window.Handle, out var thumbnailId))
            WindowStyler.UnregisterThumbnail(thumbnailId);
    }

    private void ClearRowThumbnails()
    {
        foreach (var thumbnailId in _rowThumbnails.Values)
            WindowStyler.UnregisterThumbnail(thumbnailId);

        _rowThumbnails.Clear();
    }

    private static Rect GetScreenRect(FrameworkElement element)
    {
        var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
        var bottomRight = element.PointToScreen(new System.Windows.Point(element.ActualWidth, element.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    private void ShowWindowSwitcher(DockItemViewModel item, FrameworkElement anchor)
    {
        if (!ReferenceEquals(_windowSwitcherItem, item))
            ClearRowThumbnails();

        _windowSwitcherItem = item;
        WindowSwitcherItems.ItemsSource = item.Windows;
        WindowSwitcherFlyout.PlacementTarget = anchor;
        WindowSwitcherFlyout.IsOpen = true;
    }

    private void OnWindowSwitcherItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RunningWindow window })
            return;

        _windowSwitcherItem?.ActivateWindow(window.Handle);
        ReassertDockTopmost();
        WindowSwitcherFlyout.IsOpen = false;
    }

    private void OnWindowSwitcherClosed(object? sender, EventArgs e)
    {
        ClearRowThumbnails();
        _windowSwitcherItem = null;
    }

    private void ReassertDockTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            WindowStyler.ReassertTopmost(hwnd);
    }

    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItemViewModel item } element)
            return;

        e.Handled = true;

        var menu = new ContextMenu();

        if (item.IsPinned)
        {
            var unpin = new MenuItem { Header = "Unpin from dock" };
            unpin.Click += (_, _) => _viewModel.UnpinCommand.Execute(item);
            menu.Items.Add(unpin);
        }
        else if (item.IsRunning)
        {
            var pin = new MenuItem { Header = "Pin to dock" };
            pin.Click += (_, _) => _viewModel.AddPinned(item.ExecutablePath);
            menu.Items.Add(pin);
        }

        if (item.IsRunning)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());

            var endTask = new MenuItem { Header = "End task" };
            endTask.Click += (_, _) => item.EndTaskCommand.Execute(null);
            menu.Items.Add(endTask);
        }

        if (menu.Items.Count == 0)
            return;

        element.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void OnTrayIconClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Core.ViewModels.TrayIconViewModel icon })
            icon.ClickCommand.Execute(null);
    }

    private void OnTrayIconRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Core.ViewModels.TrayIconViewModel icon })
            icon.RightClickCommand.Execute(null);
        e.Handled = true;
    }

    private static void AnimateBounce(FrameworkElement element)
    {
        if (element.RenderTransform is not TransformGroup { Children.Count: >= 2 })
            return;

        var keyFrames = new DoubleAnimationUsingKeyFrames();
        keyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(-16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        keyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)))
        {
            EasingFunction = new BounceEase { Bounces = 2, Bounciness = 2, EasingMode = EasingMode.EaseOut }
        });

        var storyboard = new Storyboard();
        Storyboard.SetTarget(keyFrames, element);
        Storyboard.SetTargetProperty(keyFrames, new PropertyPath("RenderTransform.Children[1].Y"));
        storyboard.Children.Add(keyFrames);
        storyboard.Begin();
    }
}
