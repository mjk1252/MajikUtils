# Dock

A set of Windows 11 utility panels that live on the taskbar, built in WPF (.NET 9).

Dock adds a set of independently pinnable taskbar buttons and leaves the real taskbar alone:

- **Dock Drawer** — four tabs: **Launch** (search installed apps, or find and install new ones via
  winget — the taskbar button shows a progress bar while an install runs), **Recent**, **Stacks**
  (which folders are stacks) and **Clipboard**. Opens on Launch with the caret already in the
  search box, so the button is press-and-type.
- **Dock Shelf** — a holding area for files. Drag a file down onto the button and hold: Windows
  opens the shelf, and you drop straight into it. Drag items back out whenever you need them.
- **One button per folder stack** — each stack you add gets its own taskbar button wearing that
  folder's own icon. Clicking it fans the folder's contents out in an arc right above the button,
  so a folder's files are one click from the taskbar.

Right-click any of them and choose *Pin to taskbar* to keep it there. Stack buttons are the ones
worth pinning: a pinned stack is permanent one-click access to that folder.

Clipboard history is captured in the background whether or not a panel is open. Press
`Ctrl+Alt+Shift+V` anywhere to open the drawer on its Clipboard tab.

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
