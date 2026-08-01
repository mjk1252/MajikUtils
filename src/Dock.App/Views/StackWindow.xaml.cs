using System.Windows;
using System.Windows.Input;
using Dock.App.Views.Panels;
using Dock.Core.ViewModels;
using Dock.Interop.Shell;

namespace Dock.App.Views;

/// <summary>
/// One folder stack, as its own taskbar button. The window has no chrome and no background: it
/// *is* the fan. Clicking the taskbar button restores it directly over that button, so a stack's
/// contents are one click from the taskbar rather than one click plus a tab plus a tile.
/// </summary>
public partial class StackWindow : PanelWindow
{
    // Entry 0 sits FanReachDistance above the anchor. Each later entry moves further out (radius
    // grows by FanRadialStep every step) while also rotating a few degrees, so entries keep
    // climbing rather than curving back the way a constant-radius arc would near its own apex.
    // Half-extents must track the entry DataTemplate in StackWindow.xaml, whose Grid is a fixed
    // 108 x 86. They are what re-centres a tile on its computed radius point.
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

    /// <summary>Matches StackItemViewModel's own cap; the window is sized for a full fan.</summary>
    private const int MaxFanEntries = 8;

    // The fan springs from a point near the window's lower-left: entries climb, and sweep only
    // rightwards, so that is the one corner the arc never reaches back into.
    private const double AnchorX = FanItemHalfWidth + 46;

    /// <summary>Room below the anchor for the lower half of entry 0, which sits almost on it.</summary>
    private const double AnchorFromBottom = FanItemHalfHeight + 6;

    // Everything below is derived from the geometry rather than picked by eye. The window *is*
    // the clipping rectangle, so anything the fan can reach past an edge is silently cut off --
    // which is exactly what a hand-tuned size got wrong for the furthest, top-right entry.
    private static readonly double MaxRadius = FanReachDistance + (MaxFanEntries - 1) * FanRadialStep;
    private static readonly double MaxTheta = (MaxFanEntries - 1) * FanAngleStepDeg * Math.PI / 180.0;

    /// <summary>Slack past the outermost tile so its drop shadow isn't shaved off by the edge.</summary>
    private const double EdgePad = 12;

    private static readonly double CanvasWidth =
        AnchorX + MaxRadius * Math.Sin(MaxTheta) + FanItemHalfWidth + EdgePad;

    private static readonly double CanvasHeight =
        AnchorFromBottom + MaxRadius * Math.Cos(MaxTheta) + FanItemHalfHeight + EdgePad;

    private static readonly double AnchorY = CanvasHeight - AnchorFromBottom;

    private readonly FileDragGesture _drag = new();
    private readonly StackItemViewModel _stack;
    private readonly string? _pinnedIcon;

    public StackWindow(StackItemViewModel stack)
    {
        InitializeComponent();

        // Sized here, not in the XAML, so the window can never drift out of step with the fan
        // geometry it has to contain.
        Width = CanvasWidth;
        Height = CanvasHeight;

        _stack = stack;
        DataContext = stack;

        // Keyed on the folder's name rather than its id, so the file to drop in is guessable --
        // a stack on C:\Users\me\Downloads is overridden by "stack-downloads.png".
        var folderIcon = PanelIcons.LoadCustom("stack-" + stack.Name.ToLowerInvariant())
                         ?? PanelIcons.FromPng(stack.IconPng);

        Icon = folderIcon;
        _pinnedIcon = folderIcon is null
            ? null
            : PanelIcons.EnsureIcoOnDisk("stack-" + stack.Folder.Id, folderIcon);

        Title = stack.Name;
    }

    protected override string AppId => "Dock.Stack." + _stack.Folder.Id;
    protected override string PanelArgument => "stack:" + _stack.Folder.Id;
    protected override string DisplayName => _stack.Name;
    protected override string? RelaunchIconResource => _pinnedIcon;

    // The fan is placed against its own taskbar button every time it opens, so there is no
    // user-chosen position to carry across sessions.
    protected override bool PersistsPlacement => false;

    /// <summary>
    /// The fan shows at most eight entries, so a stack's button needs a way through to the rest of
    /// the folder. This one runs Explorer directly rather than routing through Dock -- it has
    /// nothing to ask the running instance for, and so keeps working from a pinned button.
    /// </summary>
    protected override IReadOnlyList<JumpListTask> ExtraJumpListTasks =>
    [
        new("Open folder", "explorer.exe", $"\"{_stack.Path}\"")
    ];

    /// <summary>
    /// Parks the window's bottom edge on the top of the work area (i.e. just above the taskbar)
    /// and centres it on the cursor, which is sitting on the taskbar button that was just clicked.
    /// Clamped horizontally so a stack pinned near either end of the taskbar still fans fully
    /// on-screen instead of half off it.
    /// </summary>
    protected override void PositionOnShow()
    {
        var work = SystemParameters.WorkArea;
        var cursor = CursorPositionDips();

        // Line the anchor up with the cursor, so the arc reads as springing from the button.
        Left = Math.Clamp(cursor.X - AnchorX, work.Left, Math.Max(work.Left, work.Right - CanvasWidth));
        Top = Math.Max(work.Top, work.Bottom - CanvasHeight);
    }

    protected override void OnPanelShown()
    {
        // Rebuilt on every open: the folder may have changed while the fan was put away, and the
        // geometry has to be recomputed anyway once the entry count differs.
        ComputeFanPositions();

        // Force a fresh bind so each entry's ContentPresenter picks up its just-computed
        // Canvas.Left/Top rather than reusing containers positioned for a previous open.
        FanItems.ItemsSource = null;
        FanItems.ItemsSource = _stack.Entries;
    }

    private void ComputeFanPositions()
    {
        for (var i = 0; i < _stack.Entries.Count; i++)
        {
            var radius = FanReachDistance + i * FanRadialStep;
            var theta = i * FanAngleStepDeg * Math.PI / 180.0;

            var x = AnchorX + radius * Math.Sin(theta);
            var y = AnchorY - radius * Math.Cos(theta);

            _stack.Entries[i].FanOffsetX = x - FanItemHalfWidth;
            _stack.Entries[i].FanOffsetY = y - FanItemHalfHeight;
        }
    }

    /// <summary>
    /// A click that reached the canvas rather than a tile is a click on empty space. Entries carry
    /// a <see cref="StackEntryViewModel"/> as their DataContext; the canvas carries the stack
    /// itself, which is what tells the two apart no matter which visual inside a tile was hit.
    /// </summary>
    private void OnBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: StackEntryViewModel })
            return;

        WindowState = WindowState.Minimized;
    }

    private void OnEntryMouseDown(object sender, MouseButtonEventArgs e) => _drag.Begin(e);

    private void OnEntryClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StackEntryViewModel entry })
            return;

        entry.OpenCommand.Execute(null);
        WindowState = WindowState.Minimized;
    }

    private void OnEntryMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StackEntryViewModel entry } element)
            _drag.TryDrag(e, element, entry.Path);
    }
}
