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

The badges come from **three sources**, and the number of them is the story. Reading the taskbar's
buttons was the obvious route and the wrong one twice over: the shell virtualizes those buttons
away while the taskbar is auto-hidden -- which is precisely the case the feature exists for, so it
answered correctly only when it was not needed -- and even on screen a badge is whatever the app
felt like putting there rather than a count of anything. Four separate string formats were parsed
wrong on the way to learning that; text written for screen readers is not an interface.

So the taskbar reader is now the least of the three:

`NotificationCentreSource` asks Windows through `UserNotificationListener`. Real notifications,
real app identity, real counts, no string parsing, and indifferent to whether the taskbar is drawn.
Documented as requiring package identity and the `userNotificationListener` capability, which this
application has neither of, and it returns `Allowed` regardless -- measured rather than assumed,
and re-measured at runtime, so a machine that says no leaves the source quiet. It is polled;
`NotificationChanged` is the obvious thing to use and genuinely does need package identity, and an
event that silently never fires is worse than a poll that plainly works. It sees only what raises
a Windows toast, which is not everything -- an app drawing its own notifications is invisible here,
and correctly so, since Windows does not know about it either.

`WindowAttentionSource` catches exactly that gap. Most chat applications raise no toast and badge
nothing, but they *flash* -- and a flash is `FlashWindowEx`, a window-manager event, so
`RegisterShellHookWindow` hears about it with the taskbar hidden and with no string parsed. It is
the only pushed source of the three. It answers a narrower question, that an app wants you rather
than how many things wait, because a flash carries no number and Windows knows none either. A
flash has a beginning and no end: the shell never says one stopped, so the window being activated
is what clears it, that being what actually stops it mattering.

The three merge in `BadgeCountViewModel` by AppUserModelID. Counted sources take the higher rather
than the sum, since an app with three in the centre usually also badges its button with a three and
adding would say six; attention goes last and only where nothing else knows the app, since letting
a numberless flash in beside "Outlook 3" would replace the three and lose the part worth reading.

The taskbar reader is kept because it is the one that works for an app badging numerically while
the taskbar is up, and because it was already written and costs a poll.

The badges were the harder half, because there is no API for them. `ITaskbarList3::SetOverlayIcon`
is a setter, and nothing reads an overlay icon back out of another process. What the shell *does*
publish is what each taskbar button tells an accessibility client about itself. So
`TaskbarBadgeSource` walks explorer's UI Automation tree -- `Shell_TrayWnd` and every
`Shell_SecondaryTrayWnd` -- and reads the badges off those strings.

A dot-style badge has no cardinality, and that is a hard limit rather than a shortcoming here.
Discord announces "Attention requested" whether one message is waiting or twenty, so an app that
badges with a dot can only ever contribute one to the count. Windows does not expose more, and
inventing a number would be worse than admitting to one.

The badge is in the button's **help text**, not its name, and that cost a release to find out. The
name is the obvious place to look and it never mentions the badge: Discord's button reads
`Discord - 1 running window pinned` whether or not anything is waiting, and says `Unread messages`
in the help text instead. Three shapes come back and all three matter -- a count, `0 notifications`
for a button reporting it has nothing, and a wordless `Unread messages` for a badge that is a dot
rather than a number. Telling the second from the third is the whole of the parser: conflate them
and you either lose every dot-style badge or invent one for every app on the bar.

The count also has to be bound to the notification wording rather than merely present in the same
string. `File Pilot - 1 running window pinned` contains a digit, and the first version of the
pattern took any digit it found -- which would have reported every ordinary running app as carrying
a badge of one.

Three things about that walk are load-bearing. It runs on a pool thread, because a UI Automation
call blocks on another process answering and the island's own thread is the one that must never
wait on explorer. It fetches every property it needs in a single `CacheRequest` round trip, which
is the difference between a walk worth doing every two seconds and one that is not. And it re-finds
the tray from the root each pass rather than holding the element, because explorer restarts and a
retained `AutomationElement` pointing at the taskbar that used to exist throws from then on with no
way back.

The parsing is in `Dock.Core` as `TaskbarButtonName`, away from anything that needs a running
explorer, so every shape the string comes in is asserted directly. It fails quiet rather than
clever: these are localised strings from a component nobody promised would keep saying the same
thing, so a name that matches nothing is a button with no badge -- never an exception, and never a
guess. `Read` returning null for a *failed* walk, as distinct from an empty snapshot for a
successful one that found nothing, is the same distinction the media source draws, and for the same
reason: an explorer restart must not blink every badge off the island.

`BadgeCountViewModel` turns all of that into a single number, and is chrome rather than an
activity for the same reason the clock is: it stands next to the clock in the collapsed layer, and
something you hid your taskbar to keep cannot afford to lose its turn on the pill.

What it shows is the **live total** -- everything the taskbar says is waiting, summed. It counted
arrivals against a baseline for two releases, and that was wrong. The idea was to spare anyone
permanently sitting on three unread mails a permanent three on the island. What it bought instead
was a number that could read zero while things were genuinely waiting, because badge semantics are
not consistent enough to difference: a dot floors to one waiting thing, a real count of one is also
one, and a genuine arrival between those two states produced no change at all. Counting `9+` as
nine has the same shape of problem one level up.

A count that is occasionally wrong in the direction of *missing something* is the one failure this
must not have, so it reports the standing total and lets the clutter fall where it may. The taskbar
showed exactly that; this stands in for the taskbar.

The bug that took three releases to find was in none of that. `Sync` reconciled the badge list with
`ObservableCollection.Move`, and the walk was returning the same pinned app twice -- so it computed
a `Move` past the end of a collection with one item in it, threw, and the exception landed on the
UI thread inside `OnUi`, which swallows them. `Apply` died before assigning the count, on every
poll, in silence, while three rounds of parser fixes went looking for a parsing problem. The walk
now deduplicates by AppUserModelID, and `Sync` is a positional reconcile that cannot throw whatever
it is handed. The second of those is the one that matters: a display path that fails silently is
worse than one that fails loudly, and this one had no way to notice.

What it draws is a chip per badged app -- the app's own icon, and the number beside it -- at the far
right of the pill past the clock, loudest first, capped at three with a `+N` for the rest. Beside
the clock rather than at the other end, because the right-hand group is everything the island shows
regardless of whose turn it is to hold the pill, and there is one place to look for both. The icon rather than a
colour swatch, which was the other idea and the worse one: Discord and Outlook are both blue, and
at the eight pixels a chip can spare there is no telling a blurple dot from a blue one. An icon is
unambiguous at that size because it is what the eye already learned to read off the taskbar. The
chips are why `CollapsedWidth` went from 260 to 330.

Resolving an icon from a taskbar button means resolving it from an AppUserModelID, which
`SHGetFileInfo` cannot do directly -- most of them are not paths, and a packaged app has no path at
all. `ShellIconProvider.GetAppIconPng` parses `shell:AppsFolder\<id>` into a PIDL and asks about
that instead, which reaches every kind of app the taskbar shows a button for. Not quite every id
resolves (Steam's does not), so a chip without an icon falls back to a glyph. Icons are cached for
the life of the app, misses included: an id the Applications folder cannot resolve this minute will
not resolve next minute either.

A wordless badge draws no number at all. The icon has already said the app has something, and
printing a "1" beside it would claim a precision Windows never gave -- it could be thirty.

Because the count keeps the pill on screen through `SetShown`, a notification arriving with nothing
playing brings the island out and holds it there. The full-screen check still runs first and still
wins: the count exists so a notification is not missed, but a topmost strip drawn across a game is
not the way to say so, and it will still be waiting afterwards.

## Jump lists

`JumpListBuilder` drives `ICustomDestinationList` directly rather than using WPF's
`System.Windows.Shell.JumpList`, which targets the *process* AppUserModelID and so can only ever
describe one list. `SetAppID` -- the call WPF does not expose -- is what gives each button its own.

Jump-list entries are shortcuts: they start a new process and cannot call into the running one, so
anything that must reach it goes through `--panel` (or `--exit`) and the single-instance pipe.
`--exit` with nothing running exits immediately rather than starting a UI just to close it.

Entries persist in the shell, not the process, which is why *Exit MajikUtils* still works on a pinned
button whose window is long gone.

## State

Everything lives under `%LOCALAPPDATA%\Majik\MajikUtils`, and the vendor folder in the middle is
load-bearing rather than tidiness. **The data directory must never be one an installer believes it
owns.**

State used to live in `%LOCALAPPDATA%\MajikUtils`, which is the exact directory Velopack installs
the application into. A delta update leaves the contents alone, so it worked for months. A full
update cleans the directory before writing, and took every note, todo, stack, shelf entry, pinned
clipboard item and setting with it -- unrecycled, because an installer tidying its own install
directory has no reason to think it is deleting user data. It is a sibling of the install directory
now, not the same folder, and `AppPathsTests` asserts that it stays one.

`AppPaths.AdoptDataFrom` migrates from both older layouts on first run, newest first, copying only
the files the app owns -- a list, not the folder's contents, because one of the folders it reads
*is* the install directory and copying everything would drag the runtime along with the settings.
It copies and never moves: the source is a directory an updater may delete without warning, so
leaving the original costs nothing and taking it costs everything.

- `settings.json` -- start-with-Windows, whether now-playing shows in the island, whether the
  camera indicator does, its shape/alignment/monitors, per-panel window placement.
- `shelf.json`, `stacks.json` -- shelf items and stack folders.
- `notes.json`, `todos.json` -- the island's scratchpad.
- `clipboard-pinned.json` -- pinned clipboard entries, the only part of clipboard history on disk.
- `icons\*.ico` -- generated artwork for the pinned taskbar buttons.
- `crash.log` -- every exception the app caught instead of dying to. Appended to, halved past
  256 KB, and safe to delete. See *Failure* below.
- `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\*.customDestinations-ms` -- the shell's
  own copy of each button's jump list. Written by the shell, not by us; listed here because it
  outlives the process and is where to look when a jump list seems stale.

## Failure

`CrashLog` and three handlers installed in `Program.Main` and `App.OnStartup`, which between them
cover the three ways an exception used to leave the process: off the dispatcher, off a plain
background thread, and out of a fire-and-forget `Task`. `Main`'s goes up before the `VelopackApp`
call, so a startup that falls over before WPF is running still leaves a trace.

The dispatcher's is the one that does real work -- it marks the exception handled unless
`CrashLog.IsFatal` says otherwise, so a throw in a click handler, a converter or a timer tick costs
one failed operation rather than the whole app. The other two can only write the trace down; by the
time either runs the runtime has already decided how this ends.

`App.OnUi` is the other half, and it is where most of the crashes actually came from. Every hardware
and system watcher in `App` is raised from a thread that is not ours -- a WASAPI callback, the
clipboard pump, a registry watcher, the thread pool -- and `Dispatcher.Invoke` is *synchronous*, so
an exception inside the delegate is rethrown on that calling thread, where nothing catches anything.
A null artwork blob or a device disappearing mid-read therefore took the app down at a moment that
correlates with plugging in headphones rather than with anything the user did. `OnUi` is the guarded
marshal every one of those sources now goes through; a bare `Dispatcher.Invoke` in `App` is a bug.

The list in `IsFatal` is short on purpose -- out of memory, a corrupted heap, a bad image -- and
anything not on it is assumed survivable. An `AccessViolationException` out of the audio interop is
on it, and is not really catchable anyway: .NET fails fast on those, so the honest outcome is a
crash that has at least been written down.

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
