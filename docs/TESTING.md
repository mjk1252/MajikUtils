# Manual Test Checklist

Things to try yourself when you're at the machine.

Run it with `dotnet build` then launch
`src/Dock.App/bin/x64/Debug/net9.0-windows/win-x64/Dock.exe`.

## Taskbar buttons
- [ ] The **real Windows taskbar is visible and behaves normally** — nothing hides it any more.
- [ ] Separate, ungrouped buttons appear: "Dock Drawer", "Dock Shelf", and one per folder stack
      wearing that folder's own icon.
- [ ] Click Drawer — it restores. Click again — it minimises.
- [ ] Alt+F4 on a panel — it minimises, and the button survives.
- [ ] CPU/GPU appear in the drawer's own header, and **nowhere on the taskbar** — no animated
      button icon, no percentages in the hover tooltip.
- [ ] Right-click each button → Pin to taskbar. Fully exit Dock (Drawer → gear → Exit Dock).
      Click each pinned button — Dock relaunches and opens that panel.
- [ ] Move and resize a panel, put it away, restart Dock — it comes back where you left it.

## Launcher (Drawer -> Launch tab)
- [ ] The drawer opens on Launch with the caret already in the search box.
- [ ] Typing filters installed apps; clicking one launches it.
- [ ] A query of 2+ characters also searches winget after a short pause.
- [ ] Clicking Install shows a progress bar on the Drawer taskbar button.

## Drawer panel
- [ ] Rail switches between Launch, Recent, Stacks and Clipboard; the active tab stays lit and
      clicking it again does not deselect it.
- [ ] **Recent** — lists recent files, opens one on click, drags one out to another app.
- [ ] **Stacks** — "Add folder..." registers a folder, and a taskbar button for it appears
      immediately. Removing it makes the button disappear.

## Stack buttons
- [ ] Clicking a stack's taskbar button **fans its contents out in an arc** above that button.
- [ ] Clicking the button again puts it away. So does clicking empty space in the fan, or
      clicking another app.
- [ ] The furthest (top-right) entry is fully on screen, not clipped.
- [ ] Entries open on click and drag out to other apps. Edits to the folder show up on reopen.
- [ ] A stack near either end of the taskbar still fans fully on-screen.

## Shelf button
- [ ] Drag a file from Explorer onto the Shelf taskbar button and hold — the shelf opens, and
      dropping adds the file. This is the one that needs SuppressAutoMinimise to work.
- [ ] Drag items back out; right-click to remove.
- [ ] Clicking the button twice opens and closes it.

## Clipboard
- [ ] Copy text in another app **while every panel is minimised**, then open the drawer's
      Clipboard tab and confirm it was captured. Clicking an entry re-copies it.
- [ ] `Ctrl+Alt+Shift+V` from anywhere opens the drawer on its Clipboard tab.

## Settings + installer
- [ ] Drawer → gear → Settings — toggling "Start with Windows" persists across a restart.
- [ ] Reboot with start-with-Windows on: every button reappears, taskbar unaffected.
- [ ] Installer at `dist/Dock-Setup-1.0.0.exe` — installs to `%LOCALAPPDATA%\Programs\Dock`,
      adds a Start Menu shortcut, and uninstalls cleanly.

## General
- [ ] Leave Dock running during normal use — check it doesn't get in the way or use noticeably
      more CPU/RAM than expected (Task Manager → Details). The Drawer redraws its icon once a
      second whether or not it is open.
