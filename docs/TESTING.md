# Manual Test Checklist

Things to try yourself when you're at the machine. Grouped by phase. Items marked
**⚠ please verify** are things I couldn't confirm via automated screenshots and
should get an extra look.

Run it with `dotnet build` then launch
`src/Dock.App/bin/x64/Debug/net9.0-windows/win-x64/Dock.exe`.

## Phase 1 — Pinned apps
- [ ] Dock appears as a glass pill at the bottom of your primary monitor.
- [ ] Click Explorer / Notepad / Calculator / Settings icons — each should launch.
- [ ] Hover over an icon — it should magnify slightly.
- [ ] Click an icon — small bounce animation.
- [ ] Right-click a pinned icon — "Unpin" appears and works.
- [ ] Drag a shortcut/.exe from Explorer onto the dock — it gets pinned.
- [ ] Click "+" — file picker opens, picking an app pins it.
- [ ] Restart the app — pins persist.
- [ ] Right-click the dock's own tray icon (bottom-right notification area) → "Exit Dock" closes it cleanly.

## Phase 2 — Running apps + dual monitor
- [ ] Dock appears identically on **both** monitors (mirrored).
- [ ] Open an app — it shows up on the dock (both monitors) with a running-dot indicator.
- [ ] Click a running app's icon — brings it to front; click again while focused — minimizes it.
- [ ] Open multiple windows of one app (e.g. 3 Explorer windows) — click its icon shows a list to pick from.
- [ ] Close an app — its running-only icon disappears from the dock.

## Phase 3 — System tray icons
- [ ] Real tray icons (volume, network, language, mic, etc.) appear inline in the dock.
- [ ] **⚠ please verify**: click a tray icon proxy in the dock — does the real flyout (volume slider, network panel, hidden-icons list) open? I tried three relay techniques (legacy click, modern SendInput, UI Automation Invoke) and couldn't get a visible result via automated testing, but that may just be a limitation of synthetic input — a real click from you is the actual test.
- [ ] Right-click a tray icon proxy — does it bring up that icon's context menu?

## Phase 4 — Taskbar hide/restore (important safety check)
- [ ] Launch the dock — the real Windows taskbar disappears (both monitors).
- [ ] Exit via tray menu → "Exit Dock" — taskbar comes back immediately.
- [ ] **Crash-safety test**: launch the dock, then kill it from Task Manager (or `taskkill /F /IM Dock.exe`) instead of exiting normally — the taskbar should still come back within a second or two (a watchdog process does this). I verified this works via automated testing, but worth you confirming once yourself.
- [ ] Press `Ctrl+Alt+Shift+T` while the dock is running — taskbar should force back immediately (panic hotkey).
- [ ] Launch a fullscreen-exclusive game — the dock should hide itself while the game is fullscreen and reappear after (Game Mode). Borderless-windowed games won't trigger this (Windows doesn't report those as exclusive fullscreen) — that's expected, not a bug.

## Phase 5 — Clock + glass look
- [ ] Clock widget shows current time + date on the right side of the dock.
- [ ] **⚠ please verify**: click the clock — does a larger flyout (big clock, full date, calendar) pop up above it? Same as the tray-icon click issue above, I couldn't confirm this one fires via synthetic input — worth a real click.
- [ ] Move your mouse left-to-right across the dock — the bright spot on the pill's border should subtly follow your cursor.
- [ ] Overall look: less "flat gradient," more genuinely translucent (you should see a hint of your desktop through it), rounded-rectangle ("squircle") ends rather than fully circular pill ends. Let me know if it still doesn't read as "glass" enough.

## General
- [ ] Leave the dock running for a while during normal use (including a game) and see if it ever gets in the way, flickers, or uses noticeably more CPU/RAM than expected (Task Manager → Details → Dock.exe).
