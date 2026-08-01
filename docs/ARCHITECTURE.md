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

Every taskbar button is a window: `DrawerWindow`, `ShelfWindow`, and one `StackWindow` per
configured folder, created and destroyed as the `Stacks` collection changes. Anything that does
*not* need a button of its own is a tab inside the drawer instead -- the launcher, recent files,
the stack list and clipboard history all live there.

Windows groups taskbar buttons by AppUserModelID, so several buttons from one process only stay
separate if each HWND is stamped with a distinct ID — see `Dock.Interop/Shell/AppIdRegistrar.cs`.
The same property store carries the `Relaunch*` values, which are what a *pinned* button uses to
start Dock back up when nothing is running.

Because a window loses its taskbar button the moment it stops being visible, the panels never
hide and never close while the app runs: `PanelWindow` minimises instead, and reinterprets
Alt+F4 the same way. That also means the show/hide interaction comes free from the shell —
clicking a taskbar button already restores a minimised window and minimises a foreground one.

A pinned button relaunches `Dock.exe --panel <name>` rather than talking to the running process,
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
monitor under the cursor and works entirely in physical pixels, via `SetWindowPos` -- Dock is
PerMonitorV2 DPI aware, so two monitors can be at different scales and WPF's DIP-based
`Left`/`Top` become ambiguous the moment a window crosses between them. Physical coordinates are
the one space every monitor agrees on.

Because panels place themselves fresh on every open, only their *size* is persisted, and only for
the drawer -- the one panel the user can resize.

**Opening the shelf mid-drag.** Hovering a drag over a taskbar button makes the shell restore that
window, which is the whole reason the shelf has a button of its own. The catch is that the
dragging application owns the foreground for the entire gesture, so the restored shelf looks
abandoned by every test `PanelWindow` uses -- hence `SuppressAutoMinimise`, raised on `DragEnter`.

## Jump lists

`JumpListBuilder` drives `ICustomDestinationList` directly rather than using WPF's
`System.Windows.Shell.JumpList`, which targets the *process* AppUserModelID and so can only ever
describe one list. `SetAppID` -- the call WPF does not expose -- is what gives each button its own.

Jump-list entries are shortcuts: they start a new process and cannot call into the running one, so
anything that must reach it goes through `--panel` (or `--exit`) and the single-instance pipe.
`--exit` with nothing running exits immediately rather than starting a UI just to close it.

Entries persist in the shell, not the process, which is why *Exit Dock* still works on a pinned
button whose window is long gone.

## State

- `%LOCALAPPDATA%\Dock\settings.json` — start-with-Windows, per-panel window placement.
- `%LOCALAPPDATA%\Dock\shelf.json`, `stacks.json` — shelf items and stack folders.
- `%LOCALAPPDATA%\Dock\icons\*.ico` — generated artwork for the pinned taskbar buttons.
- `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\*.customDestinations-ms` — the shell's
  own copy of each button's jump list. Written by the shell, not by us; listed here because it
  outlives the process and is where to look when a jump list seems stale.
