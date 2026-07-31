# Manual Test Checklist

Things to try yourself when you're at the machine. Grouped by phase.

Run it with `dotnet build` then launch
`src/Dock.App/bin/x64/Debug/net9.0-windows/win-x64/Dock.exe`.

## Phase 1 — Pinned apps
- [x] Dock appears as a glass pill at the bottom of your primary monitor.
- [x] Click icons to launch apps.
- [x] Hover magnify / click bounce animation.
- [ ] Right-click a pinned icon — "Unpin" appears and works.
- [ ] Drag a shortcut/.exe from Explorer onto the dock — it gets pinned.
- [ ] Click "+" — file picker opens, picking an app pins it.
- [ ] Restart the app — pins persist.

## Phase 2 — Running apps + dual monitor
- [x] Dock appears identically on both monitors (mirrored).
- [x] Running apps show up with a running-dot indicator.
- [ ] Click a running app's icon — brings it to front; click again while focused — minimizes it.
- [ ] Multiple windows of one app — click shows a picker list.

## Phase 3 — System tray
- [x] Tray icons readable and rendering.
- [x] Grouped behind a single chevron (not shown inline anymore).
- [x] Clicking the chevron opens the real Windows hidden-icons flyout, in the right place.
- [x] Clicking the clock opens the real Notification Center.
- [ ] Right-click a tray icon proxy (if you ever need this — currently the chevron/clock are
      the main entry points).

## Phase 4 — Taskbar hide/restore (confirmed working)
- [x] Launching hides the real taskbar on **both** monitors (fixed: previous "move
      off-screen" approach silently failed; now uses layered-window alpha, verified live).
- [x] Exiting cleanly restores it.
- [x] Crash-recovery (force-kill Dock.exe) restores it via the Dock.Guard watchdog.
- [x] Maximized windows now stop at the dock's edge instead of running underneath
      (confirmed via work-area check: ~94px reserved on both monitors).
- [ ] `Ctrl+Alt+Shift+T` panic hotkey forces the taskbar back.
- [ ] Fullscreen-exclusive game hides the dock itself (Game Mode) and restores after.

## Phase 5 — Clock + glass look
- [x] Clock shows live time/date.
- [x] Squircle shape, genuinely transparent (desktop visible through it).
- [x] Mouse-tracking highlight along the border.
- [ ] Own custom clock flyout (calendar) as a fallback if the real clock relay isn't found —
      not really testable now that the real relay works, but worth knowing it exists.

## Phase 6 — Settings + installer
- [ ] Settings window (tray menu → "Settings...") — toggle hide-taskbar, start-with-Windows,
      dock position (Bottom/Left/Right) — confirmed Left renders correctly as a vertical bar.
- [ ] Installer at `dist/Dock-Setup-1.0.0.exe` — double-click it yourself (it needs a click
      through a real dialog I can't drive automatically) and confirm it installs to
      `%LOCALAPPDATA%\Programs\Dock`, adds a Start Menu shortcut, and uninstalls cleanly.

## General
- [ ] Leave the dock running during normal use (including a game) — check it doesn't flicker,
      get in the way, or use noticeably more CPU/RAM than expected (Task Manager → Details).

## Known limitations (not bugs, just how it is)
- Icons truly buried in Windows' lazy-loaded overflow flyout (not just "hidden," but never
  yet opened this session) can't be read until that flyout is actually opened once — there's
  no way to peek at it without invasively flashing it open ourselves, which isn't done.
- Tray-icon/chevron/clock detection relies on English element names ("Show Hidden Icons",
  "Clock ...") — may not match on non-English Windows installs.
