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

**Placing a stack fan.** There is no API for a taskbar button's rectangle. But clicking a button
leaves the cursor on it, so `StackWindow.PositionOnShow` reads the cursor and parks its bottom
edge on the work area, centred there -- the fan appears to spring from the button itself. The
window has no background at all (not even `Transparent`, which is still hit-testable), so the gaps
between fan tiles pass clicks through to the desktop, and clicking through dismisses the fan.

**Opening the shelf mid-drag.** Hovering a drag over a taskbar button makes the shell restore that
window, which is the whole reason the shelf has a button of its own. The catch is that the
dragging application owns the foreground for the entire gesture, so the restored shelf looks
abandoned by every test `PanelWindow` uses -- hence `SuppressAutoMinimise`, raised on `DragEnter`.

## State

- `%LOCALAPPDATA%\Dock\settings.json` — start-with-Windows, per-panel window placement.
- `%LOCALAPPDATA%\Dock\shelf.json`, `stacks.json` — shelf items and stack folders.
- `%LOCALAPPDATA%\Dock\icons\*.ico` — generated artwork for the pinned taskbar buttons.
