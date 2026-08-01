# MajikUtils

A set of Windows 11 utility panels that live on the taskbar, built in WPF (.NET 9).

MajikUtils adds a set of independently pinnable taskbar buttons and leaves the real taskbar alone:

- **MajikUtils Drawer** — four tabs: **Launch** (search installed apps, or find and install new ones via
  winget — the taskbar button shows a progress bar while an install runs), **Recent**, **Stacks**
  (which folders are stacks) and **Clipboard**. Opens on Launch with the caret already in the
  search box, so the button is press-and-type.
- **MajikUtils Shelf** — a holding area for files. Drag a file down onto the button and hold: Windows
  opens the shelf, and you drop straight into it. Drag items back out whenever you need them.
- **One button per folder stack** — each stack you add gets its own taskbar button wearing that
  folder's own icon. Clicking it fans the folder's contents out in an arc right above the button,
  so a folder's files are one click from the taskbar.

Right-click any of them and choose *Pin to taskbar* to keep it there. Stack buttons are the ones
worth pinning: a pinned stack is permanent one-click access to that folder.

**Right-click any MajikUtils button** for its jump list: every one offers *Exit MajikUtils*, the drawer adds
Search apps / Clipboard history / Settings, and a stack adds *Open folder* for the contents its
fan doesn't show. These work on a pinned button even when MajikUtils isn't running -- clicking one
starts it.

Note that Windows' own *Close window* entry only puts a panel away. It has to: a real close would
destroy the taskbar button along with the window. *Exit MajikUtils* is the one that quits.

Clipboard history is captured in the background whether or not a panel is open. Press
`Ctrl+Alt+Shift+V` anywhere to open the drawer on its Clipboard tab.

## Custom icons

Drop your own artwork in `assets/icons/` (shipped with a build) or `%LOCALAPPDATA%\MajikUtils\icons\custom\`
(no rebuild needed, checked first). `drawer.png`, `shelf.png`, and `stack-<folder>.png` for a
stack — e.g. `stack-downloads.png`. See [`assets/icons/README.md`](assets/icons/README.md) for
sizes and the caveat about Windows caching pinned button icons.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for project layout.

## Projects

- `src/Dock.App` — WPF UI: the two panel windows and the panels they host.
- `src/Dock.Core` — Pure C# models/services, no Win32 dependency, unit-testable.
- `src/Dock.Interop` — All P/Invoke and Win32 shell interop (shell icons, per-window
  AppUserModelIDs, clipboard hooks, system stats), isolated behind interfaces with safe fallbacks.
- `tests/Dock.Core.Tests` — Unit tests for `Dock.Core`.
- `installer/` — Inno Setup script for packaging.

## Building

```
dotnet build
```

## Requirements

- Windows 11
- .NET 9 SDK
