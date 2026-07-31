# Architecture

- `Dock.Core` never references Win32/Windows-specific APIs. It holds models (pinned items, running-app state), the config/persistence service, and view models. Fully unit-testable on any OS.
- `Dock.Interop` holds every risky native behavior (tray icon reading, taskbar show/hide, window event hooks, DWM thumbnails, monitor enumeration) behind an interface, each with a safe no-op/degraded fallback so a broken interop path degrades a feature instead of crashing the app.
- `Dock.App` is the WPF UI layer: one `DockWindow` instance per monitor, all bound to a single shared view model so mirrored content stays in sync by construction.
- `Dock.Guard` is a tiny separate process launched by `Dock.App` that watches the main process handle and force-restores the Windows taskbar if the app dies without signaling a clean exit.

Config and pinned-item state live in `%LOCALAPPDATA%\Dock\config.json`.
