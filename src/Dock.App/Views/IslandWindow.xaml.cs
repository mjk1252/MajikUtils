using System.Collections.Specialized;
using System.Linq;
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
using Dock.Interop.Windowing;
using Microsoft.Win32;

// The island's measurements live in Dock.Core so they can be asserted without a window. Imported
// statically so the names read the same here as they always did.
using static Dock.Core.Models.IslandGeometry;

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
    Stacks,
    Mixer
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
    /// <summary>
    /// One width for every state the open island has, and the reason there is only one: the panel
    /// used to be 480 wide for the scratchpad and 660 for a section, so every scope click slid the
    /// whole island sideways underneath the pointer that clicked it. An overlay hanging off the top
    /// edge is allowed to grow downwards -- that is the gesture -- and is not allowed to move.
    ///
    /// 560 is the compromise the two old figures were arguing about: wide enough for a row of file
    /// names not to trim at the third word, narrow enough that a line of text is still one glance.
    /// </summary>
    private const double IslandWidth = 560;

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

    /// <summary>How small the bubble starts before it grows out of the pill.</summary>
    private const double BubbleSeedScale = 0.3;

    /// <summary>Horizontal padding between the pill's edge and the panel inside it, per side.</summary>
    private const double ContentInset = 20;

    private static readonly Duration ShowDuration = TimeSpan.FromMilliseconds(220);
    private static readonly Duration ShapeDuration = TimeSpan.FromMilliseconds(200);

    private readonly MediaViewModel _media;
    private readonly IslandActivityHost _activities;
    private readonly TimerActivity _timer;
    private readonly CaptureViewModel _capture;

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

    /// <summary>
    /// Handed to the equalizer inside the collapsed template, which has no other way to reach it --
    /// a DataTemplate's DataContext is the activity, not this window.
    /// </summary>
    public IAudioLevelSource AudioSource { get; }

    /// <summary>
    /// Whether the pill is on screen in its collapsed form. Half of what decides the equalizer
    /// runs; the bars supply the other half from the track itself.
    ///
    /// A dependency property because the only route to it is a RelativeSource binding out of the
    /// collapsed template, and that has to be notified when this changes.
    /// </summary>
    public static readonly DependencyProperty IsCollapsedShowingProperty =
        DependencyProperty.Register(nameof(IsCollapsedShowing), typeof(bool), typeof(IslandWindow),
            new PropertyMetadata(false));

    public bool IsCollapsedShowing
    {
        get => (bool)GetValue(IsCollapsedShowingProperty);
        private set => SetValue(IsCollapsedShowingProperty, value);
    }

    /// <summary>
    /// Whether the panel is on screen expanded, for the equalizer next to the media header's title.
    /// Same reasoning as <see cref="IsCollapsedShowing"/>, the other half of the pill's lifetime.
    /// </summary>
    public static readonly DependencyProperty IsExpandedShowingProperty =
        DependencyProperty.Register(nameof(IsExpandedShowing), typeof(bool), typeof(IslandWindow),
            new PropertyMetadata(false));

    public bool IsExpandedShowing
    {
        get => (bool)GetValue(IsExpandedShowingProperty);
        private set => SetValue(IsExpandedShowingProperty, value);
    }

    private IntPtr _hwnd;
    private WorkArea _work;
    private double _expandedHeight = FallbackExpandedHeight;
    private bool _shown;
    private bool _expanded;
    private bool _bubbleShown;

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

    /// <summary>Raised by the gear's "Restart to update" entry, shown once one is downloaded.</summary>
    public event Action? RestartForUpdateRequested;

    /// <summary>Raised by the gear's "Check for updates" entry.</summary>
    public event Action? CheckForUpdatesRequested;

    private bool _updateAvailable;

    /// <summary>Whether the gear menu should offer to restart into a downloaded update.</summary>
    public void SetUpdateAvailable(bool available) => _updateAvailable = available;

    public IslandWindow(
        MediaViewModel media,
        IslandActivityHost activities,
        PrivacyViewModel privacy,
        TimerActivity timer,
        CaptureViewModel capture,
        DockViewModel dock,
        IWingetService wingetService,
        IAudioLevelSource audio,
        VolumeMixerActivity mixer,
        AppSettings settings)
    {
        _media = media;
        _activities = activities;
        _timer = timer;
        _capture = capture;
        AudioSource = audio;

        InitializeComponent();

        // The expanded panel is the media panel and speaks to the media view model directly; only
        // the collapsed pill is shared ground, and it asks the host whose turn it is.
        DataContext = media;
        CollapsedLayer.DataContext = activities;
        Bubble.DataContext = activities;
        ActivityRows.DataContext = activities;
        QuickView.DataContext = capture;

        // The re-hosted panels and the stats readout all speak to the dock view model; the rest of
        // this window speaks to the media one. The mixer is the exception among the exceptions --
        // its own activity, not the dock, the same way the timer and notes panels read from theirs.
        foreach (var section in SectionViews)
            section.DataContext = dock;

        MixerView.DataContext = mixer;

        LauncherView.WingetService = wingetService;

        ApplyAppearance(settings);

        // KeyBinding.Command in XAML doesn't inherit DataContext -- InputBindings sit outside the
        // logical tree -- so Enter is wired here instead.
        CaptureInput.KeyDown += OnCaptureKeyDown;
        CaptureInput.TextChanged += (_, _) => UpdateCaptureHint();

        // Typing needs real Win32 focus, which this window only takes while it is pinned; clicking
        // into the box is also a statement that the user means to stay a while.
        CaptureInput.PreviewMouseLeftButtonDown += (_, _) => FocusInput(CaptureInput);

        UpdateCaptureHint();

        // Anything that can grow the open panel has to be caught up with, and that is every list
        // in it -- not just the ones edited by hand.
        //
        // Several of these fill in *after* the section opens: Recent enumerates the shell folder
        // off-thread, winget shells out, and the launcher refilters as you type. The pill measures
        // itself the moment the section is shown, so without this it sizes to an empty list and
        // then clips the rows that arrive a moment later -- taking the tab strip off the bottom of
        // the pill with them, where it cannot be clicked at all.
        foreach (var collection in new INotifyCollectionChanged[]
                 {
                     _capture.Items,
                     dock.ShelfItems, dock.RecentFiles, dock.ClipboardHistory,
                     dock.LauncherResults, dock.WingetResults, dock.Stacks,
                     privacy.Apps, mixer.Sessions
                 })
        {
            collection.CollectionChanged += (_, _) => ResizeForContentChange();
        }

        // Activity rows appear and disappear above everything else in the panel, so the island has
        // to catch up with them too.
        activities.Showing.CollectionChanged += (_, _) => ResizeForContentChange();

        // Lyrics are deliberately absent from this list. They used to arrive mid-song and grow the
        // open panel, because they were a band bolted under the transport row; they are a mode of
        // the now-playing stage now, and the stage is a fixed height in both of its modes.
        _media.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MediaViewModel.HasSession):
                    UpdateNowPlaying();
                    ResizeForContentChange();
                    break;

                // Every accented thing in the app reads from one brush, and this is the only place
                // that writes it: a new cover repaints the progress fill, the lit scope and the
                // focused input in one assignment.
                case nameof(MediaViewModel.Artwork):
                    UpdateAccent();
                    break;
            }
        };

        UpdateAccent();

        _hoverTimer.Tick += (_, _) => UpdateFromPointer();
        _progressTimer.Tick += (_, _) => _media.Tick();

        // A second activity arriving or leaving is the only thing that brings the bubble out or
        // puts it away while the pill itself is doing nothing.
        _activities.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IslandActivityHost.Secondary))
                UpdateBubble();
        };
    }

    private FrameworkElement[] SectionViews => [ShelfView, ClipboardView, LauncherView, RecentView, StacksView];

    private ToggleButton[] Tabs =>
        [QuickTab, ShelfTab, ClipboardTab, LauncherTab, RecentTab, StacksTab, MixerTab];

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

        PlaceBubble();

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

    /// <summary>
    /// Puts the bubble beside the pill, on whichever side there is room for it. The arithmetic is
    /// <see cref="IslandGeometry.BubbleOffset"/>'s; this only applies it to the elements.
    ///
    /// The alignment set here has to match the one the offset was measured against -- edge-to-edge
    /// at either end of the screen, centre-to-centre in the middle -- which is why both come out of
    /// the same switch on <see cref="_alignment"/>.
    /// </summary>
    private void PlaceBubble()
    {
        var detached = _shape == IslandShape.Pill;

        BubbleShape.Detached = detached;
        BubbleShape.TopGap = detached ? PillTopGap : 0;
        BubbleContentHost.Margin = new Thickness(0, detached ? PillTopGap : 0, 0, 0);

        Bubble.HorizontalAlignment = _alignment switch
        {
            IslandAlignment.Left => HorizontalAlignment.Left,
            IslandAlignment.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };

        BubbleSlide.X = BubbleOffset(_shape, _alignment);

        // Grows out of the edge facing the pill, so it reads as the island splitting rather than as
        // a notification appearing beside it.
        Bubble.RenderTransformOrigin =
            BubbleMirrored(_alignment) ? new Point(1, 0.5) : new Point(0, 0.5);
    }

    /// <summary>
    /// Shows the bubble whenever there is a second activity and the pill is collapsed.
    ///
    /// Collapsed-state only, and that falls out of the structure rather than being a rule anybody
    /// enforces: the expanded panel is not "the primary activity's panel", it is the now-playing
    /// row plus the section host plus the tab strip, and there is no second panel for a second
    /// activity to expand into. Everything it has to say is already on screen once the island is
    /// open.
    /// </summary>
    private void UpdateBubble()
    {
        var wanted = _shown && !_expanded && _activities.Secondary is not null;
        if (wanted == _bubbleShown)
            return;

        _bubbleShown = wanted;

        Animate(Bubble, OpacityProperty, wanted ? 1 : 0, ShapeDuration);
        Animate(BubbleScale, ScaleTransform.ScaleXProperty, wanted ? 1 : BubbleSeedScale, ShapeDuration);
        Animate(BubbleScale, ScaleTransform.ScaleYProperty, wanted ? 1 : BubbleSeedScale, ShapeDuration);
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
        SetShown(_activities.HasActivity || hovering);
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
        var rect = HitRect(_shape, _alignment, Screen,
            new IslandHitState(_shown, _expanded, _bubbleShown, ContentWidth, _expandedHeight));

        return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    /// <summary>The chosen monitor, in the plain shape the geometry takes.</summary>
    private IslandScreen Screen => new(_work.Left, _work.Top, _work.Width, _work.Scale);

    private void SetShown(bool shown)
    {
        if (_shown == shown)
            return;

        _shown = shown;

        if (!shown)
            SetExpanded(false);

        Animate(Pill, OpacityProperty, shown ? 1 : 0, ShowDuration);
        Animate(PillSlide, TranslateTransform.YProperty, shown ? 0 : -HiddenOffset, ShowDuration);

        UpdateCollapsedShowing();
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

        UpdateChrome();

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

        UpdateCollapsedShowing();
    }

    /// <summary>
    /// Publishes whether the collapsed pill is on screen, which is all the window has to say about
    /// the equalizer now. Whether the bars actually run is the bars' own business -- they know
    /// whether anything is playing, and the template they sit in exists only while media holds the
    /// pill, so an activity taking it away stops them without this having to know.
    /// </summary>
    private void UpdateCollapsedShowing()
    {
        IsCollapsedShowing = _shown && !_expanded;
        IsExpandedShowing = _shown && _expanded;

        NowPlaying.IsRunning = IsExpandedShowing;
        UpdateBubble();
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
            // SetExpanded returns early when the island is already open, which a pinning click
            // usually finds it -- so the two bands that pinning brings into existence have to be
            // shown, and the pill re-measured around them, from here.
            SetExpanded(true);
            UpdateChrome();

            if (_expanded)
                ResizePill(true);

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
        MixerView.Visibility = VisibilityFor(section, IslandSection.Mixer);

        // Sections size to their contents and scroll past a ceiling, rather than always opening at
        // full height: a shelf holding three files should not leave the island mostly empty, and a
        // clipboard history of two hundred entries must not grow it off the bottom of the screen.
        // Capture needs no ceiling of its own -- its feed is capped at CaptureViewModel.MaxItems.
        SectionHost.MaxHeight = section == IslandSection.Quick ? double.PositiveInfinity : SectionHeight;
        SectionHost.MinHeight = section == IslandSection.Quick ? 0 : SectionMinHeight;

        UpdateChrome();
        UpdateNowPlaying();

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
                Keyboard.Focus(CaptureInput);
                break;
        }
    }

    /// <summary>
    /// Decides which density the now-playing block gets, and whether its equalizer runs.
    ///
    /// The full form -- cover, timeline, transport -- belongs to Capture, which is the section that
    /// is *about* what is playing, and to a plain hover, which is about nothing else at all.
    /// Handing it to every section spends a third of the panel on furniture nobody opened the Shelf
    /// to use, so those get the strip: the same information and none of the controls.
    /// </summary>
    private void UpdateNowPlaying()
    {
        NowPlaying.Density = _section == IslandSection.Quick
            ? NowPlayingDensity.Full
            : NowPlayingDensity.Strip;

        NowPlaying.IsRunning = IsExpandedShowing;
    }

    /// <summary>
    /// Decides how much of the island exists. A hover is a glance: it answers "what is this" and
    /// stops there. Navigation and a section appear when the island is being *used* -- pinned, or
    /// holding a section open for a drag that is still in flight.
    ///
    /// This is the difference between the pill growing into something and the pill uncovering
    /// something that was already laid out behind it, and it is most of what the old panel got
    /// wrong: hovering the top edge of a screen handed you a workspace.
    /// </summary>
    private void UpdateChrome()
    {
        var open = _pinned || _section != IslandSection.Quick;

        NavBar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        SectionHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        // Which scope is lit depends on being pinned as much as on which section is open - Capture
        // is where an unpinned island already is, so lighting it before there is a strip to light
        // would be labelling a state the user has not chosen. Done here rather than in
        // SelectSection because pinning changes the answer without changing the section: clicking
        // an already-open panel is exactly that, and it used to leave the strip with nothing lit.
        SyncTabs(_section);
    }

    /// <summary>
    /// Repaints the one brush every accented thing in the app reads from, with the colour of the
    /// cover that is playing.
    ///
    /// The extraction is <see cref="ArtworkAccent"/>'s, which the equalizer bars have used since
    /// they were built -- this only spends it somewhere else as well. Mutated in place rather than
    /// swapped, because the resource is held by name all over the visual tree and Color is a
    /// dependency property: assigning it repaints every reference at once.
    /// </summary>
    private void UpdateAccent()
    {
        var (accent, _) = ArtworkAccent.PairFromPng(_media.Artwork);

        Recolour("IslandAccentBrush", accent);

        // The same hue at plate strength. Alpha rather than a darker shade, so it sits on the
        // island's near-black at the same weight whatever colour the cover turned out to be.
        Recolour("IslandAccentSoftBrush", Color.FromArgb(0x2E, accent.R, accent.G, accent.B));
    }

    /// <summary>
    /// Puts a new colour behind one of the accent keys.
    ///
    /// Replaced rather than recoloured, and that is not a preference. A ResourceDictionary seals
    /// every Freezable handed to it - on insertion, whatever the source - so a brush that lives in
    /// one is frozen by definition and can be neither assigned to nor animated. The first attempt
    /// here mutated the brush in place and was silently discarded on every track; the second
    /// installed a fresh brush at startup and was frozen the moment it went into the dictionary.
    ///
    /// So the entry is swapped instead, which is why every accent reference in the XAML is a
    /// DynamicResource: a StaticResource resolved once at load and would still be holding the
    /// white one it started with.
    ///
    /// The consequence is that a track change swaps the accent rather than easing into it. That is
    /// a fair trade at this scale - the artwork, the title and the timeline all change in the same
    /// frame, and nobody has ever seen the crossfade that transition was protecting.
    /// </summary>
    private static void Recolour(string key, Color colour)
    {
        if (Application.Current is { } app)
            app.Resources[key] = new SolidColorBrush(colour);
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
            IslandSection.Mixer => MixerTab,
            _ => _pinned ? QuickTab : null
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
            ReferenceEquals(tab, MixerTab) ? IslandSection.Mixer :
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

        if (_updateAvailable)
        {
            var update = new MenuItem { Header = "Restart to update MajikUtils" };
            update.Click += (_, _) => RestartForUpdateRequested?.Invoke();
            menu.Items.Add(update);
        }
        else
        {
            // Once one is ready, "Restart to update" replaces this -- re-checking at that point
            // has nothing left to find.
            var check = new MenuItem { Header = "Check for updates" };
            check.Click += (_, _) => CheckForUpdatesRequested?.Invoke();
            menu.Items.Add(check);
        }

        var exit = new MenuItem { Header = "Exit MajikUtils" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Commits whatever is in the capture box. The view model decides what the line meant and acts
    /// on it; a search is the one reading it cannot carry out itself, because the launcher is a
    /// place in this window and the view model knows nothing about windows.
    /// </summary>
    private void OnCaptureKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        // Pinned first: anything typed here means the user is working in the panel rather than
        // glancing at it, and starting a timer used to require reaching for a chip to say so.
        SetPinned(true);

        var intent = _capture.Submit(DateTimeOffset.UtcNow, _timer);

        if (intent.Kind == CaptureKind.Search)
        {
            SelectSection(IslandSection.Launcher);
            LauncherView.SetQuery(intent.Text);
        }

        UpdateCaptureHint();
        e.Handled = true;
    }

    /// <summary>
    /// Says back what Enter is about to do, before it does it. The grammar is four rules and the
    /// box is one line, so this is the whole of its documentation -- with nothing typed it reads as
    /// the legend, and from the first keystroke it reads as a prediction.
    /// </summary>
    private void UpdateCaptureHint()
    {
        var intent = CaptureViewModel.Parse(CaptureInput.Text);

        CaptureHint.Text = intent.Kind switch
        {
            CaptureKind.Timer => $"Enter starts a {Describe(intent.Duration)} timer",
            CaptureKind.Reminder => intent.Text.Length > 0
                ? $"Enter reminds you to {intent.Text} at {intent.When:h:mm tt}, in {Describe(intent.Duration)}"
                : $"Enter reminds you at {intent.When:h:mm tt}, in {Describe(intent.Duration)}",

            // The answer, not a description of one. A calculator that made you press Enter to find
            // out whether it agreed with you would be a worse calculator than the one in the
            // Start menu, and this line is already where the box says what it thinks.
            CaptureKind.Math => $"= {intent.Text}   ·   Enter copies it",

            CaptureKind.Search => "Enter searches your apps",
            CaptureKind.Note => "Enter files this as a note",
            CaptureKind.Todo => "Enter adds this as a task",
            _ => "25m timer     @9am reminder     2+2     .note     /search"
        };
    }

    /// <summary>
    /// A length in the roundest words that still say it. Reminders can be nearly a day out, so this
    /// has to cope with a good deal more than the timer chips it was written for.
    /// </summary>
    private static string Describe(TimeSpan duration)
    {
        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;

        if (hours > 0 && minutes > 0)
            return $"{hours}h {minutes}m";

        if (hours > 0)
            return $"{hours}h";

        // Anything under a minute would otherwise read as "0m", which looks broken rather than
        // imminent.
        return duration.TotalMinutes < 1 ? "under a minute" : $"{(int)duration.TotalMinutes}m";
    }

    /// <summary>
    /// A click anywhere on the open panel holds it there. This is the gesture that separates a
    /// glance from a session, and without it the two bands that only exist while pinned would have
    /// nothing to bring them out: the scope strip is one of them.
    /// </summary>
    private void OnPillClick(object sender, MouseButtonEventArgs e)
    {
        if (_expanded && !_pinned)
            SetPinned(true);
    }

    private void OnClearDoneClick(object sender, MouseButtonEventArgs e) =>
        _capture.ClearDoneCommand.Execute(null);

    private void OnRemoveTodoClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CaptureItemViewModel item })
            _capture.RemoveCommand.Execute(item);
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

    /// <summary>
    /// Width of the pill once it is open, which no longer depends on what is in it. Kept as a
    /// property rather than inlined because the hit region and the silhouette both have to be told,
    /// and they must never be told two different numbers.
    /// </summary>
    private static double ContentWidth => IslandWidth;

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

        // The equalizer unhooks and stops its own capture when its template is torn down, which
        // closing the window does.
        Close();
    }
}
