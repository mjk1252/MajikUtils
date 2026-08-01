# Dock

A set of Windows 11 utility panels that live on the taskbar, built in WPF (.NET 9).

Dock adds two independently pinnable taskbar buttons and leaves the real taskbar alone:

- **Dock Launcher** — search installed apps, or find and install new ones via winget. The taskbar
  button shows a progress bar while an install runs.
- **Dock Drawer** — recent files, folder stacks (with the fan-out), a drop shelf and clipboard
  history. Its taskbar icon is a live CPU/GPU gauge; hovering the button shows the numbers.

Right-click either button and choose *Pin to taskbar* to keep it there.

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
