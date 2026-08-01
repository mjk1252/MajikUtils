using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Dock.Core.ViewModels;

namespace Dock.App.Views.Panels;

public partial class StacksPanel : UserControl
{
    // The Popup's HWND is sized to the fan Canvas, so the Canvas also bounds how far the fan can
    // reach before entries get clipped: the furthest entry sits at FanReachDistance +
    // (count-1) * FanRadialStep from the anchor, and the anchor itself sits 40px in from the
    // canvas edge. Keep these comfortably ahead of that or the last entries silently disappear.
    private const double FanCanvasHeight = 780;

    // Entry 0 sits FanReachDistance from the tile, aligned exactly on the tile's own axis. Each
    // later entry moves further out (radius grows by FanRadialStep every step) while also rotating
    // a few degrees, so entries keep climbing rather than curving back the way a constant-radius
    // arc would near its own apex.
    // Half-extents must track the fan entry's DataTemplate in StacksPanel.xaml, whose Grid is a
    // fixed 108 x 86. They are what re-centres a tile on its computed radius point.
    //
    // FanAngleStepDeg is only a few degrees, so consecutive entries are separated almost entirely
    // radially -- their centres end up ~FanRadialStep apart, near-vertically. A step smaller than
    // the full tile height therefore makes each entry's name overlap the next entry's icon, so
    // FanRadialStep must stay >= 2*FanItemHalfHeight, with the remainder as the visible gap.
    private const double FanReachDistance = 3;
    private const double FanRadialStep = 96;
    private const double FanAngleStepDeg = 5.0;
    private const double FanItemHalfWidth = 54;
    private const double FanItemHalfHeight = 43;

    private readonly FileDragGesture _drag = new();

    private Window? _hostWindow;
    private StackItemViewModel? _openStackItem;
    private StackItemViewModel? _recentlyClosedStackItem;
    private DateTime _recentlyClosedStackItemAt;

    public StacksPanel()
    {
        InitializeComponent();

        // Watch IsOpen itself rather than the Closed event -- see OnFanIsOpenChanged.
        DependencyPropertyDescriptor
            .FromProperty(Popup.IsOpenProperty, typeof(Popup))
            .AddValueChanged(StackFanFlyout, OnFanIsOpenChanged);
    }

    private DockViewModel ViewModel => (DockViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Hooked at the window rather than on this control because the open fan's Popup covers the
        // tile that opened it -- see OnHostPreviewMouseDown.
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null)
            _hostWindow.PreviewMouseLeftButtonDown += OnHostPreviewMouseDown;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow is not null)
            _hostWindow.PreviewMouseLeftButtonDown -= OnHostPreviewMouseDown;

        _hostWindow = null;
        StackFanFlyout.IsOpen = false;
    }

    /// <summary>Closes the fan when the panel is switched away from or the window is minimised.</summary>
    public void CloseFan() => StackFanFlyout.IsOpen = false;

    private void OnAddFolderClick(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Add a folder as a stack" };
        if (dialog.ShowDialog() == true)
            ViewModel.AddStack(dialog.FolderName);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        foreach (var path in paths)
            ViewModel.AddStack(path);
    }

    private void OnStackTileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StackItemViewModel item } element)
            return;

        // If this exact stack's fan was open, this same click's mouse-down already dismissed it
        // (either WPF's StaysOpen="False" auto-dismiss or OnHostPreviewMouseDown) and
        // OnFanIsOpenChanged recorded that. Treat this up as "close", not "reopen".
        if (ReferenceEquals(_recentlyClosedStackItem, item) &&
            (DateTime.UtcNow - _recentlyClosedStackItemAt) < TimeSpan.FromMilliseconds(400))
        {
            _recentlyClosedStackItem = null;
            return;
        }

        if (StackFanFlyout.IsOpen)
        {
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
    /// when its own tile is clicked again, instead of relying on the Popup's own StaysOpen="False"
    /// auto-dismiss. Two things independently break the naive per-tile approach: (1) that built-in
    /// dismissal races the tile's own MouseLeftButtonUp, so IsOpen can already read false by the
    /// time OnStackTileClick runs with no reliable way to tell "just closed by this click" from
    /// "was already closed"; (2) entry 0 sits only 3-5px above the tile (by design), so the fan's
    /// Popup rectangle geometrically overlaps the tile underneath it once open -- clicking the tile
    /// again then hit-tests to the popup itself (OriginalSource becomes "PopupRoot"), not the tile,
    /// so ancestor-based hit-testing can never identify it as the toggle tile at all. Comparing the
    /// raw screen point against the placement target's actual screen bounds sidesteps both: it
    /// doesn't care what OriginalSource WPF attributes the click to, and it runs here -- before the
    /// click reaches anything else -- so it can close the popup itself and let OnFanIsOpenChanged
    /// record the closure in time for OnStackTileClick to see.
    /// </summary>
    private void OnHostPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!StackFanFlyout.IsOpen || _openStackItem is null)
            return;

        if (StackFanFlyout.PlacementTarget is not FrameworkElement tile)
            return;

        // Both corners go through PointToScreen: ActualWidth/Height are DIPs, but PointToScreen
        // yields device pixels, so pairing a device-pixel origin with a DIP size would under-size
        // the hit rect on any display above 100% scaling and miss clicks on the tile's right/bottom.
        var tileTopLeft = tile.PointToScreen(new Point(0, 0));
        var tileBottomRight = tile.PointToScreen(new Point(tile.ActualWidth, tile.ActualHeight));
        var tileBounds = new Rect(tileTopLeft, tileBottomRight);
        var clickScreenPos = ((Visual)sender).PointToScreen(e.GetPosition((IInputElement)sender));

        if (tileBounds.Contains(clickScreenPos))
            StackFanFlyout.IsOpen = false;
    }

    private void OnStackTileRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StackItemViewModel item } element)
            return;

        e.Handled = true;

        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Remove stack" };
        remove.Click += (_, _) => ViewModel.RemoveStackCommand.Execute(item);
        menu.Items.Add(remove);

        element.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void OnFanEntryMouseDown(object sender, MouseButtonEventArgs e) => _drag.Begin(e);

    private void OnFanEntryClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StackEntryViewModel entry })
            return;

        entry.OpenCommand.Execute(null);
        StackFanFlyout.IsOpen = false;
    }

    private void OnFanEntryMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StackEntryViewModel entry } element)
            _drag.TryDrag(e, element, entry.Path);
    }

    /// <summary>
    /// Records "the fan just closed, and it belonged to this stack" so the mouse-UP half of the
    /// very same click can tell a dismiss apart from a request to open a fresh fan.
    ///
    /// This deliberately hangs off the IsOpen property rather than the Popup's Closed event.
    /// Because StackFanFlyout sets PopupAnimation="Fade", WPF defers both the HWND teardown and
    /// the Closed event until the fade-out finishes -- so on a dismissing click the ordering is
    /// mouse-down -> IsOpen flips false -> mouse-up -> (~200ms later) Closed. Bookkeeping done in
    /// Closed therefore lands *after* OnStackTileClick has already run and, seeing no record of a
    /// recent closure and IsOpen already false, reopened the fan. That is what made the fan
    /// impossible to dismiss by clicking its own tile: every click re-opened it.
    ///
    /// IsOpen, by contrast, is set synchronously the instant the popup dismisses -- whether from
    /// our own explicit IsOpen=false or WPF's StaysOpen="False" auto-dismiss -- so the record is
    /// in place before the mouse-up is dispatched.
    /// </summary>
    private void OnFanIsOpenChanged(object? sender, EventArgs e)
    {
        if (StackFanFlyout.IsOpen)
            return;

        _recentlyClosedStackItem = _openStackItem;
        _recentlyClosedStackItemAt = DateTime.UtcNow;
        _openStackItem = null;
    }

    /// <summary>
    /// Entry 0 sits FanReachDistance from the tile, aligned exactly above it -- radius_i =
    /// FanReachDistance + i*FanRadialStep, so each later entry is strictly further from the tile
    /// than the last. theta_i = i*FanAngleStepDeg adds a slight rotation per step purely so entries
    /// fan sideways instead of stacking directly on top of one another along the same line out.
    ///
    /// StackFanFlyout uses Placement="Top", which aligns the popup's bottom-left corner with the
    /// clicked tile's top-left corner (not its centre) -- so entry 0 landing directly above the
    /// tile needs the anchor's canvas X to be the tile's half-width, not an arbitrary constant.
    /// </summary>
    private void ComputeFanPositions(StackItemViewModel item, FrameworkElement anchorElement)
    {
        var count = item.Entries.Count;
        if (count == 0)
            return;

        // A tile is positioned by its centre and then pulled back by its half-extent, so anchoring
        // the fan exactly on the tile's own axis drives entry 0's left edge negative once the fan
        // tile is wider than the stack tile -- and the Popup's HWND is sized to the Canvas, so
        // anything negative is clipped away rather than merely overhanging. Push the anchor in from
        // that edge by the half-extent and slide the whole Popup back by the same amount: entry 0
        // still lands over the tile, but now entirely inside the canvas.
        var tileCentreX = anchorElement.ActualWidth / 2;
        var anchorX = Math.Max(tileCentreX, FanItemHalfWidth);
        var anchorY = FanCanvasHeight - 40;

        StackFanFlyout.HorizontalOffset = tileCentreX - anchorX;
        StackFanFlyout.VerticalOffset = 0;

        for (var i = 0; i < count; i++)
        {
            var radius = FanReachDistance + i * FanRadialStep;
            var theta = i * FanAngleStepDeg * Math.PI / 180.0;

            var x = anchorX + radius * Math.Sin(theta);
            var y = anchorY - radius * Math.Cos(theta);

            item.Entries[i].FanOffsetX = x - FanItemHalfWidth;
            item.Entries[i].FanOffsetY = y - FanItemHalfHeight;
        }
    }
}
