# Dock

A custom Windows 11 dock/taskbar replacement styled after macOS Tahoe's Liquid Glass, built from scratch in WPF (.NET 9).

See [`docs/PHASES.md`](docs/PHASES.md) for the build plan and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for project layout.

## Projects

- `src/Dock.App` — WPF UI: dock windows, flyouts, glass visuals.
- `src/Dock.Core` — Pure C# models/services, no Win32 dependency, unit-testable.
- `src/Dock.Interop` — All P/Invoke and Win32 shell interop (tray hosting, taskbar control, window tracking), isolated behind interfaces with safe fallbacks.
- `src/Dock.Guard` — Minimal watchdog process that restores the Windows taskbar if the main app exits abnormally.
- `tests/Dock.Core.Tests` — Unit tests for `Dock.Core`.
- `installer/` — Inno Setup script for packaging.

## Building

```
dotnet build
```

## Requirements

- Windows 11
- .NET 9 SDK
