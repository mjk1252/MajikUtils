# Architecture

- `Dock.Core` never references Win32/Windows-specific APIs. It holds models (shelf items, stacks,
  clipboard entries, recent files), the persistence services, and view models. Fully
  unit-testable on any OS.
- `Dock.Interop` holds every native behavior (shell icon extraction, per-window AppUserModelID
  stamping, the clipboard-format listener and global hotkey, system stats, winget) behind an
  interface, each with a safe no-op/degraded fallback so a broken interop path degrades a feature
  instead of crashing the app.
- `Dock.App` is the WPF UI layer: a set of `PanelWindow` subclasses bound to a single shared
  `DockViewModel`, plus the `Views/Panels` UserControls they host.

## Why several windows

Every taskbar button is a window. That used to mean three kinds of them; it now means one
`StackWindow` per configured folder, created and destroyed as the `Stacks` collection changes.

Everything else moved into `IslandWindow`. The drawer and the shelf were windows only because a
taskbar button has to be one, and the island reaches all of it more directly: the launcher, recent
files, the stack list, clipboard history and the shelf are sections of its expanded panel, and the
`Views/Panels` UserControls they were always built from are simply hosted there instead.

The command palette went the same way, and for a different reason: it never needed a taskbar button,
it needed a *text box*, and the island had grown one. `CommandPaletteViewModel` survives untouched --
the ranking and merge across apps, stacks, recent files and clipboard history were never the part
that wanted a window -- and `SearchPanel` hosts its results as one more section.

Windows groups taskbar buttons by AppUserModelID, so several buttons from one process only stay
separate if each HWND is stamped with a distinct ID — see `Dock.Interop/Shell/AppIdRegistrar.cs`.
The same property store carries the `Relaunch*` values, which are what a *pinned* button uses to
start MajikUtils back up when nothing is running.

Because a window loses its taskbar button the moment it stops being visible, the panels never
hide and never close while the app runs: `PanelWindow` minimises instead, and reinterprets
Alt+F4 the same way. That also means the show/hide interaction comes free from the shell —
clicking a taskbar button already restores a minimised window and minimises a foreground one.

A pinned button relaunches `MajikUtils.exe --panel <name>` rather than talking to the running process,
so `SingleInstance` holds a named mutex and forwards the panel name to the first instance over a
named pipe.

Clipboard capture and the `Ctrl+Alt+Shift+V` hotkey live on a message-only window
(`Dock.Interop/Shell/ClipboardMonitor.cs`) rather than on a panel, because the panels spend
nearly all their time minimised.

## Two things the shell does for us

**Placing a panel.** There is no API for a taskbar button's rectangle. But clicking a button
leaves the cursor on it, so every panel reads the cursor on open and parks its bottom edge on the
work area, lined up horizontally with that point -- the stack fan then appears to spring from the
button itself.

The cursor also identifies *which monitor's* taskbar the click came from, which matters because
`SystemParameters.WorkArea` only ever describes the primary monitor: positioning with it opens
every panel on the primary no matter which screen the user is on. `MonitorPlacement` resolves the
monitor under the cursor and works entirely in physical pixels, via `SetWindowPos` -- MajikUtils is
PerMonitorV2 DPI aware, so two monitors can be at different scales and WPF's DIP-based
`Left`/`Top` become ambiguous the moment a window crosses between them. Physical coordinates are
the one space every monitor agrees on.

Because panels place themselves fresh on every open, only their *size* is persisted.

**Opening the shelf mid-drag.** Hovering a drag over a taskbar button makes the shell restore that
window, which is why the shelf used to have a button of its own. The island gets there without the
shell's help: its cursor poll keeps running during a drag, so a drag that reaches the top edge
expands the island -- which is also what clears `WS_EX_TRANSPARENT` and makes it a drop target at
all, since a click-through window is never found under the pointer.

## The island

`IslandWindow` is the one window that is not a `PanelWindow`, and inverts nearly all of their
constraints: it owns no taskbar button, so it is free to hide; it must never take focus unasked,
since it sits where the pointer passes; and it has to let clicks through, because it covers a strip
of screen other windows use.

**Hovered versus pinned.** A hover panel that vanishes when the pointer leaves is right for glancing
at a track and wrong for everything else in it, so clicking anywhere on the island *pins* it: the
pointer poll stops deciding, `WS_EX_NOACTIVATE` is lifted so the box can hold real keyboard focus,
and it stays until Esc, a click on the lit scope, or the foreground moving to another application.

The two states show different amounts of island, which is `UpdateChrome`'s whole job. A hover draws
the activity rows and the now-playing block and stops; the scope strip, the box and the section host
exist only once pinned (or while a drag is holding a section open). That is the difference between
the pill *growing into* something and merely uncovering something already laid out behind it.

It also means order matters when resizing: `ResizePill` measures the panel, and the answer depends
on which bands are showing, so `UpdateChrome` has to run first. It did not, once, and every session's
first hover opened a pill sized for the whole workspace with a media block rattling around in it.

**One width.** The panel was 480 wide for the scratchpad and 660 for a section, so every scope click
slid the island sideways under the pointer that clicked it. `IslandWidth` is 560 for every state now.
An overlay hanging off the top edge is allowed to grow downwards -- that is the gesture -- and is not
allowed to move.

**One box.** Everything typed at the island goes through `CaptureViewModel.Parse`, a pure static
that reads a line and says what it meant: a task, a duration, a clock time, a sum, a note, or a
search. Being pure and total is what makes it cheap to extend and possible to assert -- the whole
grammar is exercised without a window in `CaptureVerbTests`.

Every rule fails soft, which is the part that took the care. `@home buy milk` is a task, because what
follows the at-sign is not a clock; `buy 2 x 4 timber` is a task, because the calculator refuses
anything it cannot consume whole; `1h30` is a timer rather than a subtraction with a missing operand.
The box is where things get written down, and a grammar that eats input is worse than one that
occasionally under-reads it.

A search is the one reading the view model cannot act on itself, since the launcher is a place in a
window and `Dock.Core` knows nothing about windows: it comes back as an intent for `IslandWindow` to
route. The box lives above the section host rather than inside the Capture pane, which is what lets
it act on any scope -- and is exactly why the separate palette window stopped being necessary.

Two scopes bring a search field of their own (Apps, whose box also drives the debounced winget
search, and Clipboard), so the global box hides while either is open. One search box on screen at a
time.

**Shape and placement.** `NotchShape` draws either a notch fused to the screen edge (top corners
flaring outwards, which is what makes it read as part of the edge rather than a window pushed off
it) or a detached pill. Which one, which end of the edge, and which monitor are settings; the pill
is anchored inside a window that spans the full expanded footprint, so growing it moves it inwards
rather than off the side of the screen.

**Where the media comes from.** `MediaSessionSource` reads WinRT's
`GlobalSystemMediaTransportControls` -- the same session behind the volume flyout -- so every
player that appears there is covered without any per-application work. That projection only exists
on a Windows-versioned TFM, which is why `Dock.Interop` and `Dock.App` target
`net9.0-windows10.0.19041.0` rather than plain `net9.0-windows`.

The session belongs to another process that can exit mid-call, so every read is best-effort. What
matters more is telling *"nothing is playing"* apart from *"this reading tells us nothing"*: a
track change is not one event, and publishing an empty snapshot for the gap between two songs
would tear the island down and rebuild it between tracks. Transient states return "ignore" and
publish nothing; only a real stop publishes null. `App` then holds even that back for a grace
period, so a player restarting its session does not blink the island away.

Positions are extrapolated, not polled: the system republishes a position only when something
changes it, so `MediaSnapshot` carries the position *and the moment it was read*, and
`MediaViewModel.Tick` runs the clock forward from there.

**Why the pointer is polled.** The window is click-through whenever its controls are not showing,
and a click-through window receives no mouse messages at all -- so the very state the pointer has
to break it out of is the one that cannot report the pointer. A 120ms `GetCursorPos` poll drives
the whole behaviour instead, and covers the idle top-edge strip too, where there is no window
under the pointer to raise anything. `WS_EX_TRANSPARENT` is dropped only while the panel is open
(`OverlayWindowStyles`), and the poll is skipped entirely while the island is pinned.

**Why the window never resizes.** Only the pill inside it grows and shrinks; the window stays at
the expanded footprint throughout. Animating a layered top-level window's bounds per frame is what
makes overlays like this stutter. The pill's silhouette is a `NotchShape` rather than a rounded
`Border`, because its top corners have to curve back *outwards* into the screen edge -- a rounded
rectangle stuck to the top of a screen reads as a window that was pushed off it.

Placement is on the monitors named in settings rather than the cursor's -- the one thing in
MajikUtils that does not follow the pointer -- but goes through the same physical-pixel
`MonitorPlacement` path as the stack windows, for the same DPI reasons. Monitors are stored by
adapter device name, the only identifier stable across sessions.

**Several islands.** The island can hang from every screen at once, or from a chosen few, so `App`
holds a window per device name rather than one window. They share every view model, which is what
makes them one island as far as anything displayed is concerned: the same track, the same clock,
the same badge chips. `SyncIslands` reconciles the set against `AppSettings.EffectiveMonitors` and
is idempotent, because it is called from three places -- startup, a settings change, and a monitor
being plugged in or unplugged -- and a change to the shape must not blink an island on a screen the
change had nothing to do with.

`EffectiveMonitors` is where the three settings that can disagree are resolved, and it lives in
`Dock.Core` with the fallbacks asserted. The rule worth knowing: the answer is never empty. There
is no way to ask for no island at all, so unticking the last monitor leaves it on the primary
rather than losing it with no way back, and unplugging every chosen screen falls back rather than
showing nothing.

Requests from outside -- a hotkey, a relaunch from a pinned shortcut -- go to one island, not all
of them, since four search boxes wanting the same keystrokes is not a feature. `ActiveIsland` picks
the one on the screen the pointer is on, which is the closest thing available to where the user is.

**Telling full-screen from maximised.** `ForegroundWindow.IsFullScreenOn` takes the island off
screen when a game or a full-screen video owns that monitor. It compared the foreground window's
rectangle against the monitor's bounds, which was right until somebody auto-hid their taskbar:
ordinarily a maximised window stops at the taskbar and falls short of the monitor by its height,
but with the taskbar hidden every maximised window covers the screen exactly, and the island
disappeared behind all of them. The caption bar is what tells the two apart -- going full-screen
means dropping it, and a maximised window keeps it however much of the screen it covers.

**Island activities.** The collapsed pill is a `ContentControl` over whichever activity currently
holds it, not a fixed now-playing row. `IslandActivityHost` (in `Dock.Core`, and the one piece of
this with real logic in it, so it is unit-tested) arbitrates: highest `IslandPriority` wins, ties
broken by whichever most recently became active, and a `Linger` window keeps a source that flaps --
a player restarting between albums, a camera renegotiating a stream -- from tearing the island down
and rebuilding it a moment later. `Retire` is what finally clears an activity's display state,
which is deliberately *not* the moment it stops claiming a slot: the gap between two tracks has to
keep showing the track.

`TimerActivity` and `ProgressActivity` are the two worth reading as examples, because they are the
ones with a live value: both draw a ring in the bubble that says how far along they are without a
word, and both rank `Background` for the same reason -- evicting a track somebody chose in order to
spell out a number they can read off an arc is a bad trade. `ProgressActivity` is deliberately a
sink rather than a source: whoever is doing the work calls `Report`, which is what lets a winget
install and a file copy share one activity without it knowing what either of them is.

There are two slots. `Primary` takes the pill; `Secondary` splits off into a bubble beside it,
drawn from the same `NotchShape` so the two silhouettes always agree about the screen edge. That is
what lets the camera indicator say its piece without taking a playing track off the island -- it
ranks `Background`, below music, because "the camera is on" is a dot's worth of information. See
[`ISLAND-ACTIVITIES.md`](ISLAND-ACTIVITIES.md) for the geometry, which the hit region and the
silhouette have to agree on exactly.

**The one Alert.** `BirthdayActivity` is the only thing in the app at `IslandPriority.Alert`, and
the only one that outranks a playing track. Everything else here was built to stay out of the
music's way -- a timer draws a ring rather than take the pill, a condition is a dot on principle --
so the top rung needed something with a genuine claim on it: a fact about today that is right once
a year, and wrong the moment it is missed.

It is also the only activity with no clock behind it. An announcement expires, a condition is
re-polled, a track ends; this one is dismissed by hand, which is what the button in its expanded
template exists for and why `Linger` is zero. Dismissal is stored as a *single date* in settings
rather than a list of acknowledgements: a dismissal only ever covers the day it was made on, so
anything older is already meaningless and there is nothing to prune. `Enabled` (the Settings
toggle) is deliberately separate from it, because a toggle folded into the dismissal date would
switch itself back on at midnight.

The confetti is `ConfettiCanvas`, drawn in a single `OnRender` behind everything in the pill and
clipped by it -- a hundred rotating rectangles as a hundred WPF elements would be a hundred measure
passes a frame on a window that is already animating its own size, so the pieces are structs in an
array that never reallocates. It falls for exactly as long as the pill is up: the confetti and the
birthday are the same statement, so they start and stop together, and *Dismiss* stops it dead
rather than letting the last pieces land.

What keeps that affordable is that the class attaches its `CompositionTarget.Rendering` hook only
while it is running and drops it the frame the last piece leaves the pill. No birthday means no
frame handler at all, rather than one returning early sixty times a second forever. The island
gates it on being on screen too, so an island hidden behind a full-screen game is not animating
something nobody can see.

`BirthdayStore` is the only store here that watches its own file, because it is the only one whose
file is edited by something that is not MajikUtils. It watches the *directory* rather than the file:
most editors save by writing a temporary file and renaming it over the original, which destroys the
handle a file-scoped watch is holding. Events are coalesced over 400ms, since one save raises
several. The list itself is held in memory by `App` -- the activity clock runs four times a second
and all it asks is whether the date has rolled over, which is not worth a file read every 250ms.

**Theming.** `Theme.Apply` writes four brushes -- the island's surface and the three steps of the
text ramp -- from three colours in settings. It is a small class because the work was done when the
ramp was cut to three named levels; the ramp's two dimmer steps are the chosen colour at reduced
*alpha* rather than three separate settings, since a secondary that is a different hue from the
primary reads as an error rather than as a step down.

The same freezing rule as the accent brushes applies, for the same reason: a `ResourceDictionary`
seals every `Freezable` put into it, so each entry is replaced outright and every reference to those
four keys in the XAML is a `DynamicResource`. A `StaticResource` resolves once when the BAML loads
and would hold the white it started with for the rest of the session. That is the whole reason
applying a theme live works at all. Parsing and the blank-means-default rule live in
`ThemeColors` in `Dock.Core`, where they can be asserted without a window.

**Standing in for a hidden taskbar.** Auto-hiding the taskbar costs you two things you cannot get
back any other way: the time, and the badges telling you something is waiting. The island already
hangs off the same edge, so it is where both go.

The clock is `ClockViewModel`, and it is deliberately *not* an `IIslandActivity`. Activities take
turns holding the pill; a clock that could lose its turn would be missing at exactly the moment it
is wanted. So it is chrome -- a second column in the collapsed layer, beside whatever holds the
pill -- and it is the one thing besides an activity that can keep the island on screen, which is
what `SetShown` reads it for. It rides the same 250ms tick as everything else and rebuilds its text
only when the minute turns, and it formats through `CultureInfo.CurrentCulture.ShortTimePattern`
rather than a setting of its own, since Windows has already asked that question.

The other half -- showing which applications had something waiting -- was built, shipped and then
taken out again. It is worth a paragraph because the reason is not that it was hard.

It was tried three ways. Reading the taskbar's own badges failed twice over: the shell virtualizes
those buttons out of the accessibility tree the moment the taskbar auto-hides, which is precisely
the case the feature existed for, and a badge is whatever an app felt like putting there rather
than a count of anything -- four separate string formats were parsed wrong before that was clear.
Windows' notification centre worked and told the truth, but only about applications that raise a
toast, which most chat applications do not. The shell's flash notifications caught those, but a
flash carries no number, so it could say that something wanted you and never how much.

Three sources, each covering a different hole in the others, none of them able to answer the
question the same way twice. What reached the screen was an icon strip that was right often enough
to be trusted and wrong often enough not to be, which is the worst thing a notification can be. It
was removed rather than kept, and if it comes back it should come back on one source that answers
completely.

The clock stayed, because the clock always knows what time it is.

**Why it flickered, in the end.** Not an activity, and not the geometry -- the reported island was
crossing its own peek strip. The log said it outright once the coordinates were in it:

```
22:58:05.622  expanded (pointer on it @1084,0 in [894,0 260x11])
22:58:05.902  hidden (poll: nothing)
22:58:06.417  expanded (pointer on it @991,0 in [894,0 260x11])
```

`y=0` both times, `x` moving, and the region 260 wide by 11 tall with no slack: that is the strip
that summons a hidden island, and it sits directly in the path of a pointer travelling along the
top edge of the screen. On that machine the island was on the left monitor of two, so moving the
mouse between screens went straight through it. Every crossing brought the island out and put it
away again, which from the user's chair is the island flickering unprompted -- they were not going
near it on purpose, which is exactly why it read that way.

`HoverGate` is the answer: a hover is somebody stopping, a transit is somebody passing through, and
the difference between them is time. The gate only changes its mind once the pointer has held to
it for `Dwell`, which costs a deliberate hover a fraction of a second and costs a transit nothing,
because a transit never lasts that long. It applies in both directions, so a single stray reading
neither opens nor closes the island. Requests from outside -- a hotkey, a pinned shortcut -- force
it open, since those are not hovers and have nothing to prove.

It took seven wrong theories to get here, every one of them derived from reading the code rather
than from measuring the machine. The one that turned out to be right was not proposed at all; it
was read off a log entry. `FlickerWatch` and the coordinates in its reasons are the part of this
worth keeping.

**Why it flickered.** `VolumeMixerActivity` read Core Audio's session state literally, and Core
Audio marks a session Inactive whenever the application stops *rendering* -- which the gaps between
two sounds do. So the island tore itself down in every gap and rebuilt itself at the next sound: a
flicker every few seconds, with the pointer nowhere near it, on any machine with a browser open.
Firefox is the worst of them, cycling its per-content-process sessions constantly with nothing
obviously playing. The 1.5s linger was shorter than the 1.2s poll plus Core Audio's own inactivity
timeout, so it covered none of it.

An application that was audible eight seconds ago is still an application making sound.
`VolumeMixerActivity` now stamps each process when it is genuinely loud and treats anything inside
that window as loud, which is the same lesson `MediaSessionSource` already learned about the gap
between two tracks. The setting that switches the mixer off the pill was the only workaround, and
turning it off is what kept this invisible on the machine it was developed on.

**When it flickers.** The island was reported flickering open and closed on a machine nobody
debugging it could reach, with the pointer nowhere near it. Reading the code produced four
plausible causes and no way to tell which was real, which is how an afternoon goes into fixing
things that were never broken.

So the island diagnoses itself. `SetShown`, `SetExpanded` and `SetPinned` each take a reason and
offer the transition to `FlickerWatch`; when transitions arrive faster than a person could be
causing them -- six inside three seconds -- one entry goes to `crash.log` naming the last several
and what asked for them, then it stays quiet for five minutes. The reasons are the whole value:
the pointer poll reports *which* of activity, clock, waiting notifications or pointer is keeping
the island alive, so a report says not merely that it flickered but what kept deciding it should.

The three setters are instrumented rather than their callers, so nothing new can change the
island's state without being counted.

## Updates

Velopack, checking GitHub Releases. Two things make that possible:

`Dock.App/Program.cs` is the real entry point now, in place of the one WPF's SDK generated from
`App.xaml`'s `ApplicationDefinition` -- `Dock.App.csproj` retargets `App.xaml` to a plain `Page`
and points `StartupObject` at `Program` instead. `VelopackApp.Build().Run()` has to run before
anything else touches the filesystem or the `SingleInstance` mutex: a launch that follows an
install or an update carries hidden command-line flags Velopack uses to finish that step, and
anywhere else in the startup path is too late to catch them.

`UpdateService` (also `Dock.App`, not `Dock.Core` -- Velopack is as tied to the shape of a Windows
install as `Dock.Interop` is to Win32) wraps a `Velopack.UpdateManager` pointed at
`GithubSource("https://github.com/mjk1252/MajikUtils", null, false)`. `App` checks once at
startup and every six hours after; `UpdateManager.IsInstalled` is what keeps this a no-op for a
dev build run straight from `bin/Debug`, which has no Velopack install to update. A download that
finishes tells `IslandWindow` to add *Restart to update MajikUtils* to the gear menu --
deliberately not automatic, since swapping the running version out from under someone is the one
thing an update should never do without asking.

Two build scripts, for two different jobs:

- `tools/build-release.ps1` -- the Inno Setup installer most people download for a *first* install.
- `tools/build-velopack-release.ps1 -Version X.Y.Z` -- packs `releases/`, everything a GitHub
  Release needs for *already-installed* copies to find and download: `MajikUtils-win-Setup.exe`
  (works as a first-install path too, unsigned and less polished than the Inno one),
  `MajikUtils-X.Y.Z-full.nupkg` (what `UpdateService` actually downloads), and `releases.win.json`
  (the manifest `GithubSource` reads to know a release exists at all).

Cutting a release means running both, then creating a GitHub Release tagged `vX.Y.Z` with every
file `build-velopack-release.ps1` put in `releases/` attached to it -- the Inno installer can go on
the release too, or wherever else it is normally shared. Nothing here uploads to GitHub on its
own: creating the release is a deliberate, manual, one-at-a-time act.
