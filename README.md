# MajikUtils

A set of Windows 11 utilities that live in an island at the top of the screen, built in WPF (.NET 9).

**The island** is the whole app. It hangs from the top edge of a monitor as a notch fused to the
screen edge or, if you prefer, a pill floating just below it — at either end of that edge or in the
middle, on whichever monitor you pick (*Settings*). Collapsed it shows whatever is playing. Point at
it and it opens into album art, a progress bar, prev/play-pause/next, a todo list, notes, and a strip
of tabs along the bottom:

- **Shelf** — a holding area for files. **Drag a file to the island** and it opens the shelf ready to
  drop into. Drag items back out whenever you need them.
- **Clipboard** — the history captured in the background. `Ctrl+Alt+Shift+V` anywhere opens the
  island straight onto it.
- **Search apps** — installed apps, or new ones found and installed via winget. Opens with the caret
  already in the search box, so it is click-and-type.
- **Recent files** and **Stacks** (which folders are stacks).
- **Notes and todo** — a scratchpad. Type a task, press Enter, tick it off later.
- **The gear** — *Settings...* and *Exit MajikUtils*.

Pointing at the island opens it; clicking a tab holds it open until you click away or press Esc. It
never takes focus until you ask it to, lets clicks through whenever its controls aren't showing, and
gets out of the way of full-screen apps.

One thing still lives on the taskbar:

- **One button per folder stack** — each stack you add gets its own taskbar button wearing that
  folder's own icon. Clicking it fans the folder's contents out in an arc right above the button,
  so a folder's files are one click from the taskbar. Right-click one and choose *Pin to taskbar*:
  a pinned stack is permanent one-click access to that folder, and its jump list adds *Open folder*
  for the contents the fan doesn't show, plus *Exit MajikUtils*. A pinned button works even when
  MajikUtils isn't running — clicking it starts it.

Note that Windows' own *Close window* entry only puts a stack away. It has to: a real close would
destroy the taskbar button along with the window. *Exit MajikUtils* is the one that quits.

## Icons

The exe icon is `assets/MajikUtils.ico`, built from `assets/icon-source.png` by
`tools/make-icon.ps1` — rerun that after changing the source art. It carries 16/24/32/48/64 as
BMP frames and 128/256 as PNG, which is the encoding split the Windows shell expects.

## Custom taskbar button icons

Drop your own artwork in `assets/icons/` (shipped with a build) or `%LOCALAPPDATA%\MajikUtils\icons\custom\`
(no rebuild needed, checked first). `stack-<folder>.png` for a stack — e.g. `stack-downloads.png`.
See [`assets/icons/README.md`](assets/icons/README.md) for sizes and the caveat about Windows
caching pinned button icons.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for project layout.

## Projects

- `src/Dock.App` — WPF UI: the island window, the panels it hosts, and the stack windows.
- `src/Dock.Core` — Pure C# models/services, no Win32 dependency, unit-testable.
- `src/Dock.Interop` — All P/Invoke and Win32 shell interop (shell icons, per-window
  AppUserModelIDs, clipboard hooks, system stats), isolated behind interfaces with safe fallbacks.
- `tests/Dock.Core.Tests` — Unit tests for `Dock.Core`.
- `installer/` — Inno Setup script for packaging.

## Building

```
dotnet build
```

For a release build and installer:

```
pwsh tools/build-release.ps1
```

That publishes to `publish/MajikUtils` (self-contained, ReadyToRun) and compiles
`dist/MajikUtils-Setup-<version>.exe`. Inno Setup 6 is needed for the installer step; note it
installs per-user by default, under `%LOCALAPPDATA%\Programs`, not Program Files.

## Requirements

- Windows 11
- .NET 9 SDK
