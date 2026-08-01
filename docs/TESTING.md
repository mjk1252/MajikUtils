# Manual Test Checklist

Things to try yourself when you're at the machine.

Run it with `dotnet build` then launch
`src/Dock.App/bin/x64/Debug/net9.0-windows/win-x64/Dock.exe`.

## Taskbar buttons
- [ ] The **real Windows taskbar is visible and behaves normally** — nothing hides it any more.
- [ ] Two separate, ungrouped buttons appear: "Dock Launcher" and "Dock Drawer".
- [ ] Click Drawer — it restores. Click again — it minimises.
- [ ] Alt+F4 on a panel — it minimises, and the button survives.
- [ ] Drawer's icon animates with CPU/GPU load; hovering the button shows the percentages.
- [ ] Right-click each button → Pin to taskbar. Fully exit Dock (Drawer → gear → Exit Dock).
      Click each pinned button — Dock relaunches and opens that panel.
- [ ] Move and resize a panel, put it away, restart Dock — it comes back where you left it.

## Launcher panel
- [ ] Typing filters installed apps; clicking one launches it.
- [ ] A query of 2+ characters also searches winget after a short pause.
- [ ] Clicking Install shows a progress bar on the Launch taskbar button.

## Drawer panel
- [ ] Rail switches between Recent, Stacks, Shelf and Clipboard; the active tab stays lit and
      clicking it again does not deselect it.
- [ ] **Recent** — lists recent files, opens one on click, drags one out to another app.
- [ ] **Stacks** — "Add folder..." registers a folder. Clicking a tile **fans its contents out in
      an arc**. Clicking the same tile again closes the fan (this is the fiddly one — the dismiss
      used to reopen it instead). Entries open on click and drag out to other apps. Edits to the
      folder on disk show up without reopening.
- [ ] Fan closes when you switch tabs or minimise the window (no orphaned popup left on screen).
- [ ] **Shelf** — drag a file onto the panel to hold it; drag it back out; right-click to remove.
- [ ] **Clipboard** — copy text in another app **while both panels are minimised**, then open the
      Clipboard tab and confirm it was captured. Clicking an entry re-copies it.
- [ ] `Ctrl+Alt+Shift+V` from anywhere opens the drawer on its Clipboard tab.

## Settings + installer
- [ ] Drawer → gear → Settings — toggling "Start with Windows" persists across a restart.
- [ ] Reboot with start-with-Windows on: both buttons appear, taskbar unaffected.
- [ ] Installer at `dist/Dock-Setup-1.0.0.exe` — installs to `%LOCALAPPDATA%\Programs\Dock`,
      adds a Start Menu shortcut, and uninstalls cleanly.

## General
- [ ] Leave Dock running during normal use — check it doesn't get in the way or use noticeably
      more CPU/RAM than expected (Task Manager → Details). The Drawer redraws its icon once a
      second whether or not it is open.
