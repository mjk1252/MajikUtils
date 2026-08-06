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
at a track and wrong for everything else in it, so clicking a tab *pins* the island: the pointer
poll stops deciding, `WS_EX_NOACTIVATE` is lifted so a search box can hold real keyboard focus, and
it stays until Esc, a click on the lit tab, or the foreground moving to another application.

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

Placement is on the monitor named in settings (the primary by default) rather than the cursor's --
the one thing in MajikUtils that does not follow the pointer -- but goes through the same
physical-pixel `MonitorPlacement` path as the stack windows, for the same DPI reasons. The monitor
is stored by adapter device name, the only identifier stable across sessions, and an unplugged
screen falls back to the primary.

`ForegroundWindow.IsFullScreenOn` takes the island off screen when a game or a full-screen
video owns that monitor. It compares rectangles rather than window styles: exclusive full-screen,
borderless windows and full-screen browser tabs all get there differently and only agree on the
result.

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

- `%LOCALAPPDATA%\MajikUtils\settings.json` — start-with-Windows, whether now-playing shows in the
  island, its shape/alignment/monitor, per-panel window placement.
- `%LOCALAPPDATA%\MajikUtils\shelf.json`, `stacks.json` — shelf items and stack folders.
- `%LOCALAPPDATA%\MajikUtils\notes.json`, `todos.json` — the island's scratchpad.
- `%LOCALAPPDATA%\MajikUtils\icons\*.ico` — generated artwork for the pinned taskbar buttons.
- `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\*.customDestinations-ms` — the shell's
  own copy of each button's jump list. Written by the shell, not by us; listed here because it
  outlives the process and is where to look when a jump list seems stale.
