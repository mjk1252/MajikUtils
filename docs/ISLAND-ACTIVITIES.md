# Island Activities

A spec for generalising the island's collapsed pill from "the now-playing row" into a host for
several competing activities, and for the first activity that is not media: an indicator showing
which application is holding the microphone or the camera.

## Why

`IslandWindow` currently hard-wires `MediaViewModel` as *the* collapsed content. `HasSession`
decides whether the pill is on screen at all (`UpdateFromPointer`), the collapsed grid binds
straight to `Title`/`Artist`/`Artwork`, and `App.ApplyMediaSnapshot` owns a grace period so a
player restarting its session between albums does not blink the island away.

Every one of those is a general problem wearing media's clothes. A timer, a file transfer, a
charging state and the privacy indicator below all want the same three things: to claim the pill,
to be arbitrated against whatever else wants it, and not to flicker. Adding each one by bolting
another `Visibility` binding onto the collapsed grid ends with a grid that is six mutually
exclusive layouts and a window that knows about all of them.

So: the collapsed pill becomes a `ContentControl` over one *current* activity, chosen by a small
arbiter that lives in `Dock.Core` and is unit-testable. Media becomes the first implementation
rather than the special case.

## The contract

`Dock.Core/ViewModels/IIslandActivity.cs`:

```csharp
/// <summary>
/// Something that can claim the collapsed pill. Deliberately tiny: an activity says whether it
/// wants the pill and how badly, and everything about how it *looks* is a DataTemplate keyed on
/// the concrete view model. An activity that had to describe itself as a title and a glyph could
/// not express the now-playing bars, and inventing a shape general enough for both would leave
/// each one worse than the bespoke visual it replaced.
/// </summary>
public interface IIslandActivity : INotifyPropertyChanged
{
    /// <summary>Stable identity, for diagnostics and for keeping one registration per source.</summary>
    string Key { get; }

    /// <summary>Whether this wants the pill right now.</summary>
    bool IsActive { get; }

    IslandPriority Priority { get; }

    /// <summary>
    /// How long this stays current after going inactive. Sources at this level flap: a player
    /// restarts its session between albums, and a conferencing app releases and reacquires the
    /// microphone several times a call. Lingering is what stops either from tearing the island
    /// down and rebuilding it a moment later.
    /// </summary>
    TimeSpan Linger { get; }

    /// <summary>
    /// Called by the host once the linger window has closed. Where an activity clears its display
    /// state -- emphatically not when IsActive goes false, which is the subtlety below.
    /// </summary>
    void Retire();
}

/// <summary>
/// Explicit values rather than an ordinal, so a new activity can be slotted between two existing
/// ones without renumbering the file.
/// </summary>
public enum IslandPriority
{
    /// <summary>The resting state -- what the pill shows when nothing else is happening.</summary>
    Ambient = 0,

    /// <summary>A condition worth interrupting the ambient state for. The privacy indicator.</summary>
    Status = 100,

    /// <summary>Something with an end: a timer, a transfer.</summary>
    Transient = 200,

    Alert = 300
}
```

Note what is *not* on it. No accent: `ArtworkAccent` needs WPF imaging and so cannot live in
`Dock.Core`, and pushing a packed-ARGB struct through the interface just to hand it back to the
view buys nothing — colour is a template's business, and the privacy template's is a constant.
No width either; see [Deferred](#deferred).

### Claiming a slot is not the same as having something to show

`Retire` is the piece that makes `Linger` work rather than half-work, and it was missed on the
first pass of this spec. `MediaViewModel.HasSession` was doing double duty: it meant both *a
session exists* and *there is a track worth drawing*, and seven visibility bindings in the island
depend on the second meaning.

Point `IsActive` at it and the grace period inverts into a bug. The session drops between two
tracks, the host starts lingering — and the view model has already blanked itself, so the pill
spends the whole grace period showing "Nothing playing". The linger would be holding up an empty
version of the thing it was meant to preserve.

So the two meanings are separate properties:

- `IsActive` drops the moment the session goes. It is the claim, and it starts the linger.
- `HasSession` is display state and survives the gap. Only `Retire` clears it.

Every activity has this split in some form, which is why `Retire` belongs on the interface rather
than inside `MediaViewModel`. The privacy indicator needs exactly the same thing — which app was
holding the microphone has to stay on the pill for its two-second linger.

## The arbiter

`Dock.Core/ViewModels/IslandActivityHost.cs`. Holds the registered activities, subscribes to each
one's `PropertyChanged`, and exposes `Current` and `HasActivity`.

Selection is: highest `Priority` among those effectively active, ties broken by whichever most
recently *became* active. That last rule is what makes it behave like the Dynamic Island rather
than like a priority queue — a new claim of equal rank takes the pill, and when it goes the
previous one comes back rather than staying displaced.

Activation order is stamped by the host on the false→true edge, not read off the activity. An
activity that had to report its own activation time would be one more thing each implementation
can get wrong, and the host already sees every transition.

### Two slots

The same ordering feeds two slots, not one. `Primary` is the head of the list and takes the pill;
`Secondary` is the runner-up and takes the bubble described below. Anything past the second waits
— Apple shows at most two, and a third form would have nowhere to go that is not simply smaller
than legible.

```csharp
public sealed partial class IslandActivityHost : ObservableObject
{
    [ObservableProperty] private IIslandActivity? _primary;

    /// <summary>The runner-up, shown in the bubble. Null whenever fewer than two are active.</summary>
    [ObservableProperty] private IIslandActivity? _secondary;

    [ObservableProperty] private bool _hasActivity;

    public void Register(IIslandActivity activity);

    /// <summary>
    /// Expires lingering activities. Driven by a timer in the App layer rather than by one held
    /// here, so the whole class stays free of WPF and a test can walk the clock forward without
    /// waiting on anything.
    /// </summary>
    public void Tick(DateTimeOffset now);
}
```

Both slots come out of one ordered sequence, so `Secondary` is three lines once `Primary` exists
and lands in Phase A with its tests. Only the rendering waits for Phase C — splitting them would
mean writing the ordering twice.

The consequence worth stating plainly: a higher-priority activity arriving no longer *displaces*
the music, it demotes it to the bubble. Music playing plus a call in progress is now both, which
is the entire point of the split and is a better answer than the single slot gave.

`Tick` takes the time rather than reading it, which is the entire testability seam — no scheduler
abstraction, no fake clock interface, and `Tick(start + 2s)` in a test is the same call the app
makes 4 times a second.

### What this removes

`App.ApplyMediaSnapshot`, `OnMediaClearElapsed` and `_mediaClearTimer` all go. Their comment about
holding back the snapshot that empties the island moves onto `Linger`, where it now covers two
sources instead of one. `ApplyMediaSnapshot` collapses to `_mediaViewModel.Apply(snapshot)` and
folds back into its caller.

### Tests

`tests/Dock.Core.Tests/IslandActivityHostTests.cs` — this is the piece with actual logic in it, so
it gets real coverage:

- No active activity → `Primary` and `Secondary` null, `HasActivity` false.
- One active → it is primary, `Secondary` stays null.
- Higher priority activating takes primary and pushes the incumbent to secondary; deactivating it
  promotes the incumbent back.
- Equal priority: the most recently activated is primary.
- Three active → the third appears in neither slot, and is promoted to secondary when one of the
  two above it goes.
- Deactivating does not clear the slot before `Linger` elapses; `Tick` past it does.
- Reactivating inside the linger window cancels the expiry (the flapping case).
- A lingering primary still holds its slot: the secondary is not promoted until the linger expires,
  or the pair would swap and swap back over a gap between two tracks.

## The view

Three changes in `Dock.App`, of which one is real work.

**`CollapsedLayer` becomes a `ContentControl`.** Its `DataContext` is the host (set in the
constructor, alongside the existing `NotesPanel.DataContext = notes` lines), its `Content` is
`{Binding Primary}`, and the grid that is in there today moves verbatim into a
`DataTemplate DataType="{x:Type vm:MediaViewModel}"` in `Window.Resources`. Every binding inside
it survives unchanged, because the template's DataContext is the `MediaViewModel` the bindings
already assumed.

`ExpandedLayer` keeps `DataContext = media`. It is the media panel; it was never the general case.

**"Nothing playing" moves out of the template.** It was the last row of the collapsed media grid,
shown when `HasSession` was false. But with no activity there is no template being drawn to put it
in, so the idle top-edge peek would have come up as an empty pill. It belongs to the island rather
than to media anyway: it sits beside the `ContentControl`, keyed on `Primary` being null.

**The equalizer has to move.** `Bar1`..`Bar4` are reached by `x:Name` from code-behind, and
`x:Name` does not resolve into a `DataTemplate`. This is the bulk of the refactor and the reason
to do Phase A on its own.

Extract `Dock.App/Views/NowPlayingBars.xaml{,.cs}`, taking `UpdateEqualizer`, `OnAudioLevels`,
`UpdateBarColour`, `BuildBarBrush`, `StopOffset`, `Fade`, `Blend`, `BarBeats`, `_barLevels`,
`_barBrushes`, `_audioDriven` and the four `Bar*` constants out of `IslandWindow` — roughly 120
lines that were always about media and never about the window. It takes the `IAudioLevelSource`
and an `IsRunning` dependency property, and reads `IsPlaying`/`Artwork` off its own DataContext.

Two things fall out of this for free. The window's `running = _shown && !_expanded &&
_media.IsPlaying` loses its media term — the control only exists while media is *current*, so
"another activity has the pill" stops the capture without anybody writing that rule. And the
control must `_audio.Stop()` on `Unloaded`, which is the same guarantee `UpdateEqualizer` was
making by hand. Ownership of `AudioLoopbackSource` stays in `App`.

A template has no route back to the window that hosts it, so the two things the control needs from
the window are `RelativeSource` bindings against new members on `IslandWindow`: `AudioSource` (a
plain property — it never changes) and `IsCollapsedShowing` (a `DependencyProperty`, because it
does). What the window publishes is only *"the pill is on screen and collapsed"*; whether the bars
run is the control's own business.

One compiler constraint worth knowing before writing the DP: **an array-typed dependency property
cannot be set inside a template section.** `Artwork` has to be declared `typeof(object)` and cast
on use, or the XAML compiler fails with `MC4102: Tags of type 'PropertyArrayStart' are not
supported in template sections`.

**`UpdateFromPointer`** swaps `_media.HasSession` for `_host.HasActivity`. One line.

## The bubble

With two slots filled, the runner-up splits off into a small round form beside the pill — the
Dynamic Island's own answer to two live activities, and the reason the two-slot host is worth
having at all.

### Two templates per activity

An activity now has two appearances: what it looks like with the whole 260px pill, and what it
looks like in a 34px circle. Those are different enough that one template cannot serve both —
compacted, media is album art and nothing else, and the privacy indicator is a bare coloured dot.

Implicit `DataTemplate DataType="..."` can only express one of them, so the compact set gets
explicit keys — `Compact.MediaViewModel`, `Compact.PrivacyViewModel` — and a ~15-line
`DataTemplateSelector` in `Dock.App/Views/CompactActivityTemplateSelector.cs` looks up
`$"Compact.{item.GetType().Name}"`.

Missing key falls back to a generic template: the activity's icon, or a neutral dot. That fallback
is what keeps `IIslandActivity` from growing a `CanCompact` flag — a new activity that has not
thought about its compact form still renders something honest, rather than failing to compile or
rendering nothing.

### Shape

`NotchShape` is reused rather than reimplemented: the bubble is the same silhouette at
`PillWidth = PillHeight = 34`, `BottomRadius = 17`, and it inherits `Detached`/`TopGap` from the
current appearance setting so the two forms always agree about whether they hang off the edge or
float below it.

The one number that does not carry over is `Fillet`. At 14 either side of a 34px shape the flares
are most of the bubble; `BubbleFillet = 8` keeps the family resemblance without the shape
disappearing into its own corners.

### Placement

The bubble is a third child of the `Pill` grid, so `PillSlide` and the `Pill` opacity animation
carry it in and out with everything else — no second slide animation, and no chance of the two
forms parking at different heights. It is a sibling of the *silhouette*, not a child of
`PillContent`, because that host clips and the bubble is outside the pill by definition.

Horizontal position is a `TranslateTransform` set from code in `PlaceBubble()`, not a `Margin`. A
margin on a centre-aligned element shifts it by half its value, which is exactly the sort of thing
that reads correctly and is wrong by 50%.

Work in whole footprints rather than in pill widths plus fillets — the flares hang off both shapes
and any gap that ignores them lets the two silhouettes collide:

```
CollapsedFootprint = CollapsedWidth + (notch ? FilletWidth * 2 : 0)
BubbleFootprint    = BubbleSize     + (notch ? BubbleFillet * 2 : 0)
```

**The bubble is anchored to the same edge as the island, not always centred.** This is the part
that is easy to get wrong: the pill's *width* animates from 260 to 660 on expand, so a bubble
centred inside the `Pill` grid rides that animation and drifts outward as it fades. Centre
alignment happens to be safe (the pill's centre is the window's centre and does not move), but at
either end of the edge the drift is plainly visible. So:

| Alignment | `Bubble.HorizontalAlignment` | `BubbleSlide.X` |
| --- | --- | --- |
| Center | `Center` | `(CollapsedFootprint + BubbleFootprint) / 2 + BubbleGap` |
| Left | `Left` | `CollapsedFootprint + BubbleGap` |
| Right | `Right` | `-(CollapsedFootprint + BubbleGap)` |

Anchoring to the pinned edge measures from something that stays still.

**The bubble mirrors to the *left* of the pill when `IslandAlignment.Right`.** A right-anchored
pill sits `EdgeMargin` from the side of the screen and there is simply no room. Left and Center
both put it on the right, as Apple does.

### It is a collapsed-state form only

The bubble fades out with `CollapsedLayer` when the island expands, and does not reappear until it
collapses. This falls out of the existing structure rather than being a rule anyone has to
enforce: `ExpandedLayer` is not "the primary activity's panel", it is the now-playing row plus the
section host plus the tab strip. There is no second panel for a second activity to expand into,
and the expanded island already shows everything — including the privacy row from Phase B.

Clicking the bubble therefore does what clicking the pill does: pin and expand.

That also means the animating pill width never has to be reasoned about. The bubble's offset is
computed from `CollapsedWidth`, which is constant for every frame in which the bubble is visible.

### Appearing and leaving

Scale plus opacity from the pill's edge — `ScaleTransform` from 0.3 with
`RenderTransformOrigin` on the side facing the pill, over `ShapeDuration` (200ms) so it moves in
step with the shape animations already running. A bubble that faded in place would read as a
notification; one that grows out of the pill reads as the island splitting, which is what is
actually happening.

A change of *identity* in the secondary slot would want a crossfade of the content with the shape
left alone. **Not built, deliberately**: with media at `Ambient` and the camera at `Background`,
`Secondary` can only ever be the camera or null — the two-non-null transition the crossfade exists
for cannot happen. It is worth adding the day a third activity makes it reachable, and not before.

### The arithmetic lives in Dock.Core

None of the above is UI. Footprints, offsets and the hit region are pure functions of the shape,
the alignment and the state, and they were the one part of this with no automated coverage at all —
the drift bug above was caught by launching the app, flipping a setting and squinting at a
screenshot, which is not a thing anyone will do again.

So `Dock.Core/Models/IslandGeometry.cs` holds the constants and the maths, with `IslandRect` and
`IslandScreen` standing in for `System.Windows.Rect` (WPF, unreferencable from here). `IslandWindow`
imports it with `using static`, so the names read the same at the call sites as they always did, and
converts to a real `Rect` at the boundary.

`IslandGeometryTests` then asserts every shape × alignment combination on two monitors — one at
100% starting at the origin, one offset and at 150%, so nothing can quietly assume a DIP is a pixel.
Three of those tests are worth calling out:

- **The bubble clears the pill by exactly `BubbleGap`.** Both silhouettes carry their own flares,
  and a gap measured without them lets the shapes touch in notch form.
- **`BubbleOffset` and `BubbleRect` agree.** The view uses the translate; every other test proves
  things about the rectangle. Without a test tying the two together they could drift apart in
  silence and the whole suite would stay green.
- **The expanded hit region still covers where the bubble was.** See below — this is the invariant
  that cannot be checked by looking at it.

Verified by mutation, not by assumption: reintroducing the original centring bug fails 5 tests,
dropping the bubble's fillets or widening the hit region symmetrically fails 13.

### Hit testing

`ActiveHitRect` has to cover the pill, the gap, and the bubble as one rectangle. The gap is a
strip the pointer crosses on the way to the bubble, and a region that excluded it would put the
island away halfway across — the same reasoning the existing code applies to `PillTopGap`
vertically.

The trap: the combined footprint is **no longer symmetric about the pill**, because the bubble
hangs off one side only. The `Center` case centres `footprint` in the work area, and centring the
*combined* width would shift the pill itself half a bubble off-centre. The bubble's extent has to
widen the rect on one side without moving the pill's own placement.

Vertically nothing changes: same top edge, same height, so `reach` is untouched.

**The invariant that cannot be eyeballed.** Resting the pointer on the bubble expands the island,
and expanding hides the bubble. If the expanded region did not still cover where the bubble had
been, the pointer would fall out of it, the island would collapse, the bubble would reappear under
the pointer, and the whole thing would strobe. It happens to hold for every current combination —
the expanded panel is far wider than the collapsed pill plus a bubble — but "happens to hold" is
not a guarantee, and any future change to `SectionWidth`, `EdgeMargin` or the bubble's size could
quietly break it at one alignment only. It is asserted rather than reasoned about.

## The camera indicator

macOS puts a dot in the notch when the camera is on. Windows has the data and surfaces it as an
anonymous tray glyph — and the island is already sitting where Apple puts the dot.

### Where the data is

`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam` —
still called that in the registry long after the shell stopped saying it anywhere a person can see.
Direct subkeys are packaged apps, keyed by package family name; Win32 apps are under a
`NonPackaged` subkey, keyed by full exe path with `#` substituted for `\`.

Each carries `LastUsedTimeStart` and `LastUsedTimeStop` as QWORD FILETIMEs. **In use right now is
`LastUsedTimeStop == 0`** with a non-zero start. That is the whole detection.

Watched with `RegNotifyChangeKeyValue` (`fWatchSubtree: true`, `REG_NOTIFY_THREAD_AGNOSTIC`) on a
background thread, falling back to a 2s poll if the call fails — the house rule that a broken
interop path degrades a feature rather than crashing the app. Polling alone would be defensible
here; the keys are a few dozen values.

Two honest limits, worth putting in the code as comments:

- This reports what the capability manager sees. An application reaching the device through a
  legacy or virtual driver path may never appear.
- The `Stop` write is not instantaneous, and plenty of applications hold the device open past the
  point their UI suggests they released it. The indicator is truthful about the device, not about
  the application's intent.

### Pieces

| File | What |
| --- | --- |
| `Dock.Core/Models/DeviceUsage.cs` | `record DeviceUsage(string AppPath, string DisplayName)` |
| `Dock.Core/Services/IDeviceUsageSource.cs` | `event EventHandler<IReadOnlyList<DeviceUsage>>? Changed` + `Start`/`Stop`, matching `ISystemStatsSource` |
| `Dock.Interop/Shell/DeviceUsageMonitor.cs` | the registry read and watch |
| `Dock.Core/ViewModels/PrivacyViewModel.cs` | the activity |

`PrivacyViewModel` is `Priority => Background`, `Linger => 2s`, `IsActive => CameraInUse`, plus
`Apps` for the expanded list and a `Summary` string.

### It ranks below music, and compacts to a dot

Running it settled this. At `Status` the indicator took the whole pill: a playing track was
replaced outright by "WindowsCamera · camera". That trades something the user chose for something
they did not ask for, and "the camera is on" is worth knowing at a glance without being worth
reading a sentence about.

So there is a rank below `Ambient`:

```csharp
Background = -100,   // worth showing, never worth the whole pill
```

Music keeps the pill and the camera compacts to a green dot beside it — which is what macOS does
with the same information. With nothing playing the indicator takes the pill on its own and names
the application, because then the room is free.

This pulled `CompactActivityTemplateSelector` and the `Compact.*` templates forward out of Phase C,
since a second slot that renders nowhere is no better than no second slot. The compact form
currently draws *inside* the collapsed pill, in a second column beside the primary's content; Phase
C moves that same `ContentControl` out into the detached bubble and is left with only the geometry.

### The camera only — the microphone is deliberately not read

The spec originally covered both. Reading the real key on a real machine settled it against the
microphone, and the evidence is worth keeping.

A sample of one desktop had **two applications holding the microphone at rest**: an audio routing
service that takes it at boot and never lets go, and a chat client that holds it for as long as it
is running rather than for the length of a call. The webcam key on the same machine had **nothing
live at all** — every entry was a browser that had touched it weeks earlier.

That is the whole argument. `LastUsedTimeStop == 0` honestly means *"this application has the
device open"*. For the microphone that is true nearly always, and a light that never goes off
carries no information — it would have sat permanently on the island, and (at `Status`) taken the
pill from the music for good. For the camera it is true exactly when someone is on video, which is
a real event and worth interrupting for.

So the microphone comes out entirely: no mic key, no amber dot, no endpoint mute. `DeviceUsage`
loses its `DeviceKind` — with one device, a discriminator discriminates nothing — and the monitor
drops from two watched keys to one, which collapses its per-key watch list into a single thread and
a single event.

Cut along with it: `IMicrophoneControl`, `MicrophoneControl`, `IAudioEndpointVolume` and the
capture-endpoint constants in `AudioInterop`, and the `IslandPriority.Background` rank that existed
only to keep a permanently-lit indicator away from the pill.

**What is given up:** there is now no signal at all that something is listening. On Windows that
signal was never really available — only the fact that an application had the device open, which
is not the same question.

Display names: `FileVersionInfo.FileDescription` off the exe path, falling back to the filename.
Icons reuse `ShellIconProvider.GetIconPng(path, 32)`. Packaged apps have only a family name to
work with — prettify the segment before the `_` and accept it is rough; they are the minority and
mostly recognisable.

### How it looks

Collapsed: a `DataTemplate` for `PrivacyViewModel` — a green dot (`#30D158`), the app's icon, and
its name. Deliberately the same green macOS uses; the convention is worth more than originality
here.

Expanded: one more `Auto` row at the top of `ExpandedLayer`, collapsed when the camera is idle,
listing each app using it. No tab — the tab strip is for sections the user navigates to, and this
is a condition, not a place.

Adding the row means `ExpandedLayer` gains a fourth `RowDefinition` and everything below it shifts
down one: `MediaHeader` to row 1, `SectionHost` to 2, `TabStrip` to 3. The row also has to be wired
into `ResizeForContentChange` via `Apps.CollectionChanged`, or the island keeps the height it
opened at and clips the tab strip the moment a camera comes on.

### Setting

`AppSettings.ShowPrivacyIndicator`, default `true` — an absent property keeps the initialiser, the
same way `ShowMediaIsland` handles settings files written before it existed. Checkbox in
`SettingsWindow`, wired like `MediaIslandToggled` to start and stop the monitor.

## Order of work

**Phase A — the host. No visible change.**

1. `IIslandActivity.cs`, `IslandActivityHost.cs`
2. `IslandActivityHostTests.cs`
3. `MediaViewModel : IIslandActivity` — `IsActive => HasSession`, and the generated
   `OnHasSessionChanged` partial raises `PropertyChanged` for `IsActive`
4. Extract `NowPlayingBars`
5. `IslandWindow`: `ContentControl` + `DataTemplate`, `HasActivity` in `UpdateFromPointer`
6. `App.xaml.cs`: build the host, register media, 250ms `Tick` timer, delete the clear timer

Stop here and confirm the island behaves exactly as before, including the between-tracks gap and
the full-screen hide. Phase A is a refactor; if anything looks different, it is a bug.

**Phase B — the indicator.**

7. `DeviceUsage`, `IDeviceUsageSource`, `DeviceUsageMonitor`
8. `PrivacyViewModel` + its template and the expanded row
9. Setting, and registration in `App`

At the end of B there are two activities and `Secondary` is populated, but nothing renders it: a
camera call takes the pill and the music waits its turn. That is a coherent place to stop, and it
is what the bubble in Phase C fixes.

**Phase C — the bubble.** `CompactActivityTemplateSelector` and the `Compact.*` templates already
landed in B, drawing inside the pill's second column. What is left is moving them out:

10. `BubbleShape` + `BubbleContentHost` in the `Pill` grid, `PlaceBubble()`, the mirror for
    right-alignment
11. Scale-in animation (`BubbleSeedScale` → 1, origin on the edge facing the pill)
12. `ActiveHitRect` — the asymmetric footprint

Phase C is worth doing last and not sooner: it is the only part that cannot be tested without two
activities to arbitrate between, and its whole risk surface is geometry that the hit rect and the
silhouette have to agree on. Verify all four combinations on screen — notch and pill form, centre
and end alignment — because each one changes the arithmetic.

Then `docs/ARCHITECTURE.md` gains an "Island activities" section under *The island*, and
`docs/PHASES.md` a Phase 10.

## Deferred

**Per-activity collapsed width.** `CollapsedWidth` is a constant read by `ResizePill`,
`ActiveHitRect` and now `PlaceBubble`, and all three have to agree or the pill is a different size
from the region keeping it on screen. Making it per-activity means animating the collapsed width
on every handover and re-deriving the other two from the current activity — worth doing, not worth
entangling with this. The privacy indicator sits in the 260px pill for now, and the bubble takes
some of the pressure off: a cramped second activity has somewhere to go that is not the pill.

**A third activity.** Two slots is Apple's cap and a good one. If a third ever has to be visible,
the answer is almost certainly a count on the bubble rather than a second bubble.

**Notification mirroring.** Still the highest-value thing not here, and still blocked on
`UserNotificationListener` needing package identity. A sparse package keeps the Inno installer and
unlocks it, but it is a day of packaging rather than an afternoon of C#.

**Battery and charging.** Written off for now: the machine this was built on is a desktop, so it
would have been dead code with nowhere to test it.

## The activities

Phase D added eight more, and the shape of the work is the point: only three new view models were
needed, because most activities are the same thing wearing different labels.

| View model | Rank | What uses it |
| --- | --- | --- |
| `MediaViewModel` | Ambient | the system media session |
| `PrivacyViewModel` | Background | camera in use |
| `TimerActivity` | Background | the countdown |
| `ConditionActivity` | Background | do-not-disturb, restart pending |
| `AnnouncementActivity` | Transient | clipboard, volume, downloads, screenshots, drives, network, Bluetooth |

`AnnouncementActivity` is the one that collapses the list. Seven sources push into a *single*
shared instance rather than each being an activity of its own, because seven activities at the same
rank would spend their lives queueing for two slots — and because the honest behaviour of an
on-screen display is that the newest thing replaces whatever was there. It also has
`Linger => Zero`: the activity *is* the grace period, so holding it for another second and a half
after it expires would be counting the same thing twice.

`ConditionActivity` is one class with two instances, since all that separates "Do not disturb" from
"Restart pending" is a label and a glyph.

### Where each reading comes from

| Source | Mechanism | Note |
| --- | --- | --- |
| `SystemEventSource` | `FileSystemWatcher` ×2, `DriveInfo` poll, `NetworkChange`, `DisplaySettingsChanged` | five mechanisms, one event |
| `SystemConditionSource` | `SHQueryUserNotificationState`, `RebootRequired` key, `DriveInfo` | polled every 5s |
| `VolumeSource` | `IAudioEndpointVolumeCallback` | pushed, not polled |
| `AudioDeviceSource` | `IMMNotificationClient` + `IPropertyStore` | the default output moving |
| `BatterySource` | `GetSystemPowerStatus` + `PowerModeChanged` | registered only if a battery exists |
| `BluetoothSource` | WinRT `DeviceWatcher` | AQS filtered to connected |
| `DeviceUsageMonitor` | `RegNotifyChangeKeyValue` | camera |
| `WifiInfo` | `WlanQueryInterface` | names the network on connect |

Six traps worth keeping written down, each of which cost time:

- **`PROPVARIANT` is 24 bytes on x64, not 16.** An 8-byte header, then a union 16 wide because its
  largest members are a length plus a pointer. Declaring only the two fields that get read implies
  16, and the property store then writes eight bytes past the buffer. That does not fail — it
  corrupts whatever was next and surfaces later as an `AccessViolationException` in an unrelated
  frame. `[StructLayout(LayoutKind.Explicit, Size = 24)]` is the fix, and the lesson generalises:
  **declare the real size of any struct COM writes into, not just the parts you read.**
- **`IMMNotificationClient` fires once per role.** One output switch raises three notifications, so
  a single change announces three times unless it is filtered to render/`eConsole`.
- **Battery is asked about before it is registered.** `IsPresent` is on the interface rather than
  implied by the event, so a desktop carries no battery activity and starts no watcher for one.

- **Removable drives are polled on purpose.** Volume arrival is a `WM_DEVICECHANGE` *broadcast*,
  and broadcasts never reach a message-only window — which is what a background watcher wants to
  be. Diffing `DriveInfo.GetDrives()` every two seconds gets the same answer with none of that.
- **`AUDIO_VOLUME_NOTIFICATION_DATA` starts with a 16-byte GUID**, so `bMuted` is at offset 16 and
  `fMasterVolume` at 20. Getting these wrong yields plausible-looking nonsense rather than an
  error.
- **A `DeviceWatcher` reports everything already connected before it reports anything new.** Those
  are not events. Announcing them would put a card on the island for every paired headset at every
  startup, so announcements begin only after `EnumerationCompleted`.
- **Downloads and Screenshots must be resolved with `SHGetKnownFolderPath`.** Downloads has no
  `Environment.SpecialFolder` member and Screenshots is a known folder in its own right rather than
  a fixed subfolder of Pictures. Both can be relocated to another drive from their Properties, and
  building either path out of the profile directory is wrong the moment anybody does. It fails
  *silently*, too: the watcher points at a folder that is not there, `TryWatch` returns null, and
  downloads simply never announce. The machine this was built on had Downloads on `E:\`, so the
  first version of this shipped broken on its own author's desktop.

### What is still not covered

The shell's Downloads folder is not the only place downloads land. A browser configured to save
somewhere else entirely — Chrome and Firefox both keep their own download directory setting,
independent of the known folder — will not be seen. Watching those would mean reading each
browser's preferences, which is a per-application integration this deliberately avoids.

### Glyphs must be escaped, not pasted

Every glyph is a Segoe Fluent Icons private-use codepoint, written as a `\uE916` escape in C# and
as `&#xE916;` in XAML. Pasting the literal character works right up until some tool in the chain
silently eats it, at which point the label renders with a blank where the icon should be and
nothing anywhere reports an error. That happened during Phase D and cost a debugging session.
