using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Core.ViewModels;
using Dock.Interop.Windowing;
using Microsoft.Win32;

namespace Dock.App.Views;

/// <summary>Which pane the expanded island is showing.</summary>
public enum IslandSection
{
    /// <summary>The scratchpad -- todo list and notes. What a hover gets, and the default.</summary>
    Quick,
    Shelf,
    Clipboard,
    Launcher,
    Recent,
    Stacks
}

/// <summary>
/// The island: a pill hanging from the top edge of a monitor, showing whatever is playing and
/// growing into the app's whole surface when the pointer reaches it.
///
/// This is now where MajikUtils lives. It used to be one overlay beside a row of taskbar-button
/// windows -- a Drawer and a Shelf -- and those are gone: everything they held is a section of this
/// panel, reached from the tab strip along its bottom. Only folder stacks still own taskbar
/// buttons, since a stack is a thing the user pins deliberately.
///
/// Deliberately not a <see cref="PanelWindow"/>. Those exist to own taskbar buttons and so can
/// never hide; this one owns nothing, takes no focus until asked, and spends most of its life
/// invisible.
/// </summary>
public partial class IslandWindow : Window
{
    // Both sizes are DIPs of the pill inside the window. The window itself is fixed at the larger
    // footprint and never resizes -- see the comment in the XAML.
    private const double CollapsedWidth = 260;
    private const double CollapsedHeight = 34;

    /// <summary>Width of the hover panel: what is playing, plus the scratchpad.</summary>
    private const double QuickWidth = 480;

    /// <summary>
    /// Width of a section opened from the tab strip. Wider than the hover panel because these are
    /// lists of files and apps rather than a couple of lines of text.
    /// </summary>
    private const double SectionWidth = 660;

    /// <summary>
    /// Ceiling on an open section's height. These panels scroll, so without one a long clipboard
    /// history would grow the island straight off the bottom of the screen.
    /// </summary>
    private const double SectionHeight = 380;

    /// <summary>Floor for the same, so an empty shelf still opens as a panel rather than a sliver.</summary>
    private const double SectionMinHeight = 150;

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

    /// <summary>
    /// Drop of the pill form below the screen edge. Enough to read as detached at a glance -- any
    /// less and it looks like a notch that failed to reach the edge -- without opening a gap the
    /// pointer can fall through on the way down to it.
    /// </summary>
    private const double PillTopGap = 8;

    /// <summary>Inset from the screen's side when the island is parked at one end of the edge.</summary>
    private const double EdgeMargin = 16;

    /// <summary>
    /// Mirrors the notch silhouette's Fillet in the XAML. The flares sit outside the pill on both
    /// sides, so the notch form covers this much more screen than the pill it draws -- which the
    /// hover region has to account for.
    /// </summary>
    private const double FilletWidth = 14;

    /// <summary>Horizontal padding between the pill's edge and the panel inside it, per side.</summary>
    private const double ContentInset = 20;

    private static readonly Duration ShowDuration = TimeSpan.FromMilliseconds(220);
    private static readonly Duration ShapeDuration = TimeSpan.FromMilliseconds(200);
    private static readonly Duration AccentDuration = TimeSpan.FromMilliseconds(320);

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

    private readonly MediaViewModel _media;
    private readonly NotesViewModel _notes;
    private readonly TodosViewModel _todos;

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
    /// One diagonal gradient per bar, together forming a single sweep across the whole equalizer:
    /// bar <c>i</c> carries the slice of the artwork's two-colour ramp that falls where it sits.
    /// One brush shared by all four would restart the gradient inside each 2.5px bar instead.
    /// </summary>
    private readonly LinearGradientBrush[] _barBrushes;

    private readonly IAudioLevelSource _audio;

    /// <summary>Smoothed band levels, 0..1, so a bar eases between published frames.</summary>
    private readonly double[] _barLevels;

    /// <summary>
    /// Whether real audio is driving the bars. False on a machine whose loopback endpoint would not
    /// open, where the fixed animation takes over.
    /// </summary>
    private bool _audioDriven;

    private IntPtr _hwnd;
    private WorkArea _work;
    private double _expandedHeight = FallbackExpandedHeight;
    private bool _shown;
    private bool _expanded;
    private bool _barsRunning;

    /// <summary>
    /// Whether the island is being held open. A hover panel goes away when the pointer does, which
    /// is right for glancing at a track and wrong for everything else here: a search box the user
    /// is typing into cannot vanish because the mouse drifted off it.
    /// </summary>
    private bool _pinned;

    private IslandSection _section = IslandSection.Quick;

    private IslandShape _shape = IslandShape.Notch;
    private IslandAlignment _alignment = IslandAlignment.Center;
    private string _monitor = "";

    /// <summary>Raised by the gear. The settings window is the application's to own, not ours.</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised by the gear's Exit entry -- the app's only remaining quit affordance.</summary>
    public event Action? ExitRequested;

    public IslandWindow(
        MediaViewModel media,
        NotesViewModel notes,
        TodosViewModel todos,
        DockViewModel dock,
        IWingetService wingetService,
        IAudioLevelSource audio,
        AppSettings settings)
    {
        _media = media;
        _notes = notes;
        _todos = todos;
        _audio = audio;

        InitializeComponent();

        DataContext = media;
        NotesPanel.DataContext = notes;
        TodosPanel.DataContext = todos;

        // The re-hosted panels and the stats readout all speak to the dock view model; the rest of
        // this window speaks to the media one.
        foreach (var section in SectionViews)
            section.DataContext = dock;

        TabStrip.DataContext = dock;
        LauncherView.WingetService = wingetService;

        ApplyAppearance(settings);

        // KeyBinding.Command in XAML doesn't inherit DataContext -- InputBindings sit outside the
        // logical tree -- so Enter-to-add is wired here instead.
        NoteInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                _notes.AddNoteCommand.Execute(null);
        };

        TodoInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                _todos.AddTodoCommand.Execute(null);
        };

        // Typing needs real Win32 focus, which this window only takes while it is pinned; clicking
        // into a box is also a statement that the user means to stay a while.
        NoteInput.PreviewMouseLeftButtonDown += (_, _) => FocusInput(NoteInput);
        TodoInput.PreviewMouseLeftButtonDown += (_, _) => FocusInput(TodoInput);

        // Anything that grows the open panel -- a new note, a todo, a dropped file -- has to be
        // caught up with, or the pill keeps clipping the list at the height it opened at.
        _notes.Notes.CollectionChanged += (_, _) => ResizeForContentChange();
        _todos.Todos.CollectionChanged += (_, _) => ResizeForContentChange();
        dock.ShelfItems.CollectionChanged += (_, _) => ResizeForContentChange();

        _bars = [Bar1, Bar2, Bar3, Bar4];
        _barLevels = new double[_bars.Length];
        _barBrushes = new LinearGradientBrush[_bars.Length];

        for (var i = 0; i < _bars.Length; i++)
        {
            _barBrushes[i] = BuildBarBrush(i, ArtworkAccent.Fallback, ArtworkAccent.FallbackSecondary);
            _bars[i].Fill = _barBrushes[i];
        }

        _audio.LevelsChanged += OnAudioLevels;

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

    private FrameworkElement[] SectionViews => [ShelfView, ClipboardView, LauncherView, RecentView, StacksView];

    private ToggleButton[] Tabs => [ShelfTab, ClipboardTab, LauncherTab, RecentTab, StacksTab, NotesTab];

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
    /// Applies the shape, edge position and monitor the user picked. Called at construction and
    /// again whenever settings change, so the island rearranges itself while the settings window
    /// is still open rather than at the next start.
    /// </summary>
    public void ApplyAppearance(AppSettings settings)
    {
        _shape = settings.IslandShape;
        _alignment = settings.IslandAlignment;
        _monitor = settings.IslandMonitor ?? "";

        var detached = _shape == IslandShape.Pill;

        Notch.Detached = detached;
        Notch.TopGap = detached ? PillTopGap : 0;

        // The silhouette and the content host are siblings, not nested -- so dropping the pill clear
        // of the screen edge has to move both.
        PillContent.Margin = new Thickness(0, detached ? PillTopGap : 0, 0, 0);

        // The window spans the full expanded footprint and never moves; anchoring the pill inside it
        // is what puts the island at one end of the edge. Anchored rather than centred on a computed
        // point so that expanding grows it inwards, away from the screen's side, instead of off it.
        Pill.HorizontalAlignment = _alignment switch
        {
            IslandAlignment.Left => HorizontalAlignment.Left,
            IslandAlignment.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };

        Pill.Margin = _alignment switch
        {
            IslandAlignment.Left => new Thickness(EdgeMargin, 0, 0, 0),
            IslandAlignment.Right => new Thickness(0, 0, EdgeMargin, 0),
            _ => default
        };

        // Whatever is hidden has to be parked above the edge by its full height, gap included, or a
        // detached pill leaves its bottom rim showing.
        if (!_shown)
        {
            // Clearing the animation first: a property still held by one ignores whatever is
            // assigned to it, so the pill would keep the old form's parking spot until it next
            // showed itself.
            PillSlide.BeginAnimation(TranslateTransform.YProperty, null);
            PillSlide.Y = -HiddenOffset;
        }

        Reposition();
    }

    /// <summary>How far above its resting place the pill sits while hidden.</summary>
    private double HiddenOffset =>
        CollapsedHeight + 6 + (_shape == IslandShape.Pill ? PillTopGap : 0);

    /// <summary>
    /// Parks the window against the top edge of the chosen monitor's work area. Work area rather
    /// than monitor bounds so a taskbar docked at the top pushes the island below it.
    /// </summary>
    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        _work = MonitorPlacement.FromDeviceName(_monitor);

        var width = (int)Math.Round(Width * _work.Scale);
        var height = (int)Math.Round(Height * _work.Scale);

        var left = _alignment switch
        {
            IslandAlignment.Left => _work.Left,
            IslandAlignment.Right => _work.Right - width,
            _ => _work.Left + (_work.Width - width) / 2
        };

        MonitorPlacement.SetPhysicalBounds(_hwnd, left, _work.Top, width, height);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(Reposition);

    /// <summary>Shows the latest system stats in the tab strip, where the drawer's header used to.</summary>
    public void UpdateStats(double cpuPercent, double gpuPercent) =>
        StatsText.Text = $"CPU {cpuPercent:0}%   GPU {gpuPercent:0}%";

    /// <summary>
    /// Opens the island on a given section and holds it there. This is the path every outside
    /// request takes -- a relaunch from a pinned shortcut, the clipboard hotkey, a jump-list entry.
    /// </summary>
    public void ShowSection(IslandSection section)
    {
        SetShown(true);
        SelectSection(section);
        SetPinned(true);
    }

    /// <summary>
    /// The whole behaviour, decided once per poll: something playing keeps the pill on screen,
    /// the pointer reaching it opens the controls, and with nothing playing only the top-edge
    /// strip brings it out at all.
    /// </summary>
    private void UpdateFromPointer()
    {
        // Pinned means the user asked for it and is working in it; the pointer has no say until
        // they dismiss it.
        if (_pinned)
            return;

        // A game or a full-screen video owns the whole monitor, and a topmost overlay would be
        // drawing straight across it.
        if (ForegroundWindow.IsFullScreenOn(_monitor))
        {
            SetExpanded(false);
            SetShown(false);
            return;
        }

        var (x, y) = CursorInfo.GetPosition();
        var hovering = ActiveHitRect().Contains(x, y);

        // The pointer alone is enough to expand now: the scratchpad and the tab strip live in this
        // panel too, and those have to be reachable even with nothing playing.
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
            (true, _) => (ContentWidth, _expandedHeight, HoverSlack),
            (_, true) => (CollapsedWidth, CollapsedHeight, HoverSlack),
            _ => (CollapsedWidth, PeekHeight, 0d)
        };

        // The notch's flares hang off both sides of the pill they frame, so its footprint on screen
        // is wider than the pill itself; the pill form has no such overhang.
        var footprint = width + (_shape == IslandShape.Notch ? FilletWidth * 2 : 0);

        var scaledWidth = footprint * _work.Scale;
        var scaledMargin = EdgeMargin * _work.Scale;
        var scaledSlack = slack * _work.Scale;

        var left = _alignment switch
        {
            IslandAlignment.Left => _work.Left + scaledMargin,
            IslandAlignment.Right => _work.Right - scaledMargin - scaledWidth,
            _ => _work.Left + (_work.Width - scaledWidth) / 2
        };

        // Measured from the screen edge down regardless of the gap above a detached pill: that gap
        // is a strip the pointer has to cross to reach the pill, and treating it as outside the
        // region would put the island away halfway there.
        var reach = height + (_shape == IslandShape.Pill ? PillTopGap : 0);

        return new Rect(
            left - scaledSlack,
            _work.Top,
            scaledWidth + scaledSlack * 2,
            reach * _work.Scale + scaledSlack);
    }

    private void SetShown(bool shown)
    {
        if (_shown == shown)
            return;

        _shown = shown;

        if (!shown)
            SetExpanded(false);

        Animate(Pill, OpacityProperty, shown ? 1 : 0, ShowDuration);
        Animate(PillSlide, TranslateTransform.YProperty, shown ? 0 : -HiddenOffset, ShowDuration);

        UpdateEqualizer();
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
            return;

        _expanded = expanded;

        // A hover that ends takes the island back to the scratchpad: a section is something the
        // user opened deliberately, and leaving it showing behind a collapsed pill means the next
        // glance at what is playing lands on last week's clipboard instead.
        if (!expanded && !_pinned)
            SelectSection(IslandSection.Quick);

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
    /// Holds the island open, or lets it go. Pinning also lifts WS_EX_NOACTIVATE: a window that can
    /// never be activated can never hold keyboard focus either, and every section here has
    /// something to type into.
    /// </summary>
    private void SetPinned(bool pinned)
    {
        if (_pinned == pinned)
            return;

        _pinned = pinned;

        OverlayWindowStyles.SetActivatable(_hwnd, pinned);

        if (pinned)
        {
            SetExpanded(true);
            Activate();
            FocusSectionInput();
            return;
        }

        // Back to being looked at rather than used. The pointer poll takes over from here and will
        // collapse it on the next tick if the pointer has already moved away.
        foreach (var tab in Tabs)
            tab.IsChecked = false;

        SelectSection(IslandSection.Quick);
        SetExpanded(false);
    }

    /// <summary>
    /// Swaps the visible pane and resizes the pill to it. Sections are hidden and shown rather than
    /// created and destroyed, so a search box keeps its text and a list its scroll position.
    /// </summary>
    private void SelectSection(IslandSection section)
    {
        _section = section;

        QuickView.Visibility = VisibilityFor(section, IslandSection.Quick);
        ShelfView.Visibility = VisibilityFor(section, IslandSection.Shelf);
        ClipboardView.Visibility = VisibilityFor(section, IslandSection.Clipboard);
        LauncherView.Visibility = VisibilityFor(section, IslandSection.Launcher);
        RecentView.Visibility = VisibilityFor(section, IslandSection.Recent);
        StacksView.Visibility = VisibilityFor(section, IslandSection.Stacks);

        // Sections size to their contents and scroll past a ceiling, rather than always opening at
        // full height: a shelf holding three files should not leave the island mostly empty, and a
        // clipboard history of two hundred entries must not grow it off the bottom of the screen.
        SectionHost.MaxHeight = section == IslandSection.Quick ? double.PositiveInfinity : SectionHeight;
        SectionHost.MinHeight = section == IslandSection.Quick ? 0 : SectionMinHeight;

        SyncTabs(section);

        if (section == IslandSection.Recent)
            RecentView.Refresh();

        if (_expanded)
            ResizePill(true);

        if (_pinned)
            FocusSectionInput();
    }

    /// <summary>
    /// Puts the caret wherever this section expects typing to go. Held-open sections are opened to
    /// be used, and a search box or a task line that needs a click first is a click most people
    /// never make -- they type, and the keystrokes go to whatever is behind the island.
    /// </summary>
    private void FocusSectionInput()
    {
        switch (_section)
        {
            case IslandSection.Launcher:
                LauncherView.FocusSearch();
                break;

            case IslandSection.Quick:
                Keyboard.Focus(TodoInput);
                break;
        }
    }

    /// <summary>Lights the tab for the open section, and only that one.</summary>
    private void SyncTabs(IslandSection section)
    {
        var active = section switch
        {
            IslandSection.Shelf => ShelfTab,
            IslandSection.Clipboard => ClipboardTab,
            IslandSection.Launcher => LauncherTab,
            IslandSection.Recent => RecentTab,
            IslandSection.Stacks => StacksTab,
            _ => _pinned ? NotesTab : null
        };

        foreach (var tab in Tabs)
        {
            var wanted = ReferenceEquals(tab, active);
            if (tab.IsChecked != wanted)
            {
                // Set through the field the handlers guard on, or this sync would re-enter them.
                _syncingTabs = true;
                tab.IsChecked = wanted;
                _syncingTabs = false;
            }
        }
    }

    private bool _syncingTabs;

    private static Visibility VisibilityFor(IslandSection active, IslandSection section) =>
        active == section ? Visibility.Visible : Visibility.Collapsed;

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingTabs)
            return;

        var tab = (ToggleButton)sender;

        // Pinned first: reaching for a tab at all means the user wants to stay, and the tab strip
        // lights the Notes tab only for a scratchpad that is being held open rather than hovered.
        SetPinned(true);

        SelectSection(
            ReferenceEquals(tab, ShelfTab) ? IslandSection.Shelf :
            ReferenceEquals(tab, ClipboardTab) ? IslandSection.Clipboard :
            ReferenceEquals(tab, LauncherTab) ? IslandSection.Launcher :
            ReferenceEquals(tab, RecentTab) ? IslandSection.Recent :
            ReferenceEquals(tab, StacksTab) ? IslandSection.Stacks :
            IslandSection.Quick);
    }

    /// <summary>Clicking the lit tab again puts the island away, the way its taskbar button used to.</summary>
    private void OnTabUnchecked(object sender, RoutedEventArgs e)
    {
        if (_syncingTabs)
            return;

        SetPinned(false);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        // Held open across the menu: a ContextMenu takes the foreground, which would otherwise read
        // as the user clicking away from the island.
        SetPinned(true);

        var menu = new ContextMenu();

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Items.Add(settings);

        var exit = new MenuItem { Header = "Exit MajikUtils" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void OnClearDoneClick(object sender, MouseButtonEventArgs e) =>
        _todos.ClearDoneCommand.Execute(null);

    private void OnRemoveTodoClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoItemViewModel item })
            _todos.RemoveTodoCommand.Execute(item);
    }

    /// <summary>
    /// A drag reaching the island is the gesture that used to be "hover the Shelf taskbar button and
    /// wait": it opens the shelf, ready to drop into. The pointer poll has already expanded the
    /// panel by the time this fires -- that is what made the window a drop target at all, since a
    /// click-through window receives nothing.
    /// </summary>
    private void OnDragEnter(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (!hasFiles)
            return;

        if (_section != IslandSection.Shelf)
        {
            SetShown(true);
            SelectSection(IslandSection.Shelf);
            SetExpanded(true);
        }

        // Deliberately not pinned: pinning takes the foreground, and stealing it mid-drag drops the
        // gesture. The island stays open because the pointer is on it, which during a drag it is.
        ShelfView.SetDropHint(true);
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ShelfView.SetDropHint(false);

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!ShelfView.AcceptDrop(e.Data))
            return;

        // The files are on the shelf now, so hold it open to show they landed rather than letting
        // the panel collapse the instant the pointer moves off.
        SetPinned(true);
        SelectSection(IslandSection.Shelf);
    }

    /// <summary>Esc is the keyboard's way of saying "put it away", and the only one this window has.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            SetPinned(false);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Losing the foreground dismisses a pinned island, matching how the panels behaved.
    ///
    /// Deferred to the next dispatcher pass because Deactivated also fires for things that are
    /// still *us* -- the gear's menu, a row's context menu, an in-flight drag -- and the new
    /// foreground window is not settled at the point the event is raised.
    /// </summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        Dispatcher.BeginInvoke(() =>
        {
            if (_pinned && !ForegroundWindow.IsOwnedByThisProcess())
                SetPinned(false);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Puts the caret in a box that was clicked. Needs the window activatable first: Win32 keyboard
    /// focus is not something a WS_EX_NOACTIVATE window can hold, so without pinning the caret would
    /// appear and every keystroke would still go to whatever was behind.
    /// </summary>
    private void FocusInput(IInputElement input)
    {
        SetPinned(true);
        Keyboard.Focus(input);
    }

    /// <summary>Width of the pill for whatever is currently showing in it.</summary>
    private double ContentWidth => _section == IslandSection.Quick ? QuickWidth : SectionWidth;

    /// <summary>
    /// The silhouette and the content host are sized separately rather than nested, because the
    /// silhouette is wider than the pill it draws -- its top corners flare out past both sides.
    /// </summary>
    private void ResizePill(bool expanded)
    {
        var width = expanded ? ContentWidth : CollapsedWidth;

        if (expanded)
        {
            // Laid out at the target width before being measured, or the height comes back for the
            // width the panel happened to have last time.
            ExpandedLayer.Width = width - ContentInset * 2;
            _expandedHeight = MeasureExpandedHeight();
        }

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

        // The new row's container isn't generated by the ItemsControl until the next layout pass,
        // so measuring synchronously here would still see the list as it was before the change.
        // Loaded priority runs after that pass but before anything gets painted.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_expanded)
                ResizePill(true);
        }), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Asks the expanded panel how tall it needs to be, rather than growing the pill to a figure
    /// picked in advance. The content host clips, so a pill even slightly shorter than its panel
    /// shaves the bottom off the transport buttons.
    ///
    /// Measured on each expand instead of once, because the answer changes: a live stream has no
    /// timeline, so its progress row collapses and the panel comes out shorter; and every section
    /// is a different height again.
    /// </summary>
    private double MeasureExpandedHeight()
    {
        ExpandedLayer.Measure(new Size(ContentWidth, double.PositiveInfinity));

        // DesiredSize covers the layer's own margins. Rounded up so a fractional line height
        // cannot leave the last row a hair short of fitting.
        var measured = Math.Ceiling(ExpandedLayer.DesiredSize.Height);
        return measured > 0 ? measured : FallbackExpandedHeight;
    }

    /// <summary>
    /// Runs the now-playing bars, but only while there is something to see: paused music flattens
    /// them, and a hidden or expanded pill stops them outright rather than leaving an audio capture
    /// and four animations running behind an invisible layer.
    /// </summary>
    private void UpdateEqualizer()
    {
        var running = _shown && !_expanded && _media.IsPlaying;
        if (running == _barsRunning)
            return;

        _barsRunning = running;

        if (!running)
        {
            _audio.Stop();
            _audioDriven = false;
            Array.Clear(_barLevels);

            foreach (var bar in _bars)
            {
                // Handing null to BeginAnimation is what releases the property back to its local
                // value; without it the last animated height sticks and the bar cannot be set.
                bar.BeginAnimation(HeightProperty, null);
                bar.Height = BarRestingHeight;
            }

            return;
        }

        // Real levels if this machine will give them up, the old fixed animation if not. Started
        // here rather than at launch so nothing is captured while the bars are off screen.
        _audioDriven = _audio.Start();

        if (_audioDriven)
        {
            foreach (var bar in _bars)
            {
                bar.BeginAnimation(HeightProperty, null);
                bar.Height = BarRestingHeight;
            }

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
    /// A frame of band levels, straight off the capture thread. Smoothed on the way in: the
    /// analysis window is short enough that raw values jitter, and a bar that follows every one of
    /// them reads as noise rather than as the beat it is actually tracking.
    /// </summary>
    private void OnAudioLevels(object? sender, double[] levels) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!_barsRunning || !_audioDriven)
                return;

            for (var i = 0; i < _bars.Length && i < levels.Length; i++)
            {
                var target = levels[i];
                var rate = target > _barLevels[i] ? BarAttack : BarRelease;
                _barLevels[i] += (target - _barLevels[i]) * rate;

                _bars[i].Height = BarRestingHeight + _barLevels[i] * (BarPeakHeight - BarRestingHeight);
            }
        }, DispatcherPriority.Render);

    /// <summary>
    /// Repaints the bars in the artwork's two most prominent colours, as one diagonal gradient
    /// running across the group. Crossfaded rather than swapped: the artwork itself cuts between
    /// tracks, but coloured bars snapping to an unrelated hue in the corner of the eye reads as a
    /// glitch.
    /// </summary>
    private void UpdateBarColour()
    {
        var (primary, secondary) = ArtworkAccent.PairFromPng(_media.Artwork);

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

        _audio.LevelsChanged -= OnAudioLevels;
        _audio.Stop();

        Close();
    }
}
