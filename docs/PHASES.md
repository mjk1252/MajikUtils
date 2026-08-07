# Build Phases

Phases 0-6 built a screen-edge dock that replaced the Windows taskbar. Phase 7 replaced that
approach entirely: the dock, the taskbar hiding, the AppBar reservation, the tray relay and the
`Dock.Guard` watchdog were all deleted, and the surviving features moved onto two taskbar buttons.
The history is kept here because the feature list carried over intact.

- [x] **Phase 0** — Toolchain + solution scaffold.
- [x] **Phase 1** — Glass dock bar with pinned apps that launch (single monitor). *(removed in 7)*
- [x] **Phase 2** — Running-app tracking + mirrored dock across both monitors. *(removed in 7)*
- [x] **Phase 3** — System tray icon hosting (relay Explorer's tray). *(removed in 7)*
- [x] **Phase 4** — Taskbar hide/restore with watchdog safety net + Game Mode. *(removed in 7)*
- [x] **Phase 5** — Clock widget + full Liquid Glass visual polish. *(clock removed in 7)*
- [x] **Phase 6** — Installer (Inno Setup) + settings UI → v1.0.
- [x] **Phase 6.5** — Shelf, folder stacks, clipboard history, recent files, system stats,
      app launcher + winget.
- [x] **Phase 7** — Convert to taskbar buttons, each an independently pinnable window with its own
      AppUserModelID. The real taskbar is left alone. Landed as: a Drawer (Launch / Recent /
      Stacks / Clipboard), a Shelf that doubles as a drag target, and one fan-out button per
      folder stack.
- [x] **Phase 8** — Per-button jump lists via `ICustomDestinationList` (WPF's own `JumpList` binds
      to the process AppUserModelID, so it cannot give the buttons different lists). Every button
      carries *Exit MajikUtils*, which is what makes quitting reachable now that the tray icon is gone.
- [ ] **Phase 9 (stretch)** — A "Recent" jump-list category on the drawer, and a Frequent/Recent
      category per stack, using `AppendKnownCategory` / `AppendCategory`.
- [x] **Phase 10** — Island activities. The collapsed pill became a host for several competing
      activities rather than the now-playing row: an arbiter in `Dock.Core` picks a primary and a
      runner-up, media is one implementation, and a camera-in-use indicator is the second. Two
      slots, so the runner-up splits off into a bubble beside the pill instead of displacing what
      is in it. See [`ISLAND-ACTIVITIES.md`](ISLAND-ACTIVITIES.md).
- [x] **Phase 10.1** — Eight more activities on that frame: a countdown timer, the volume readout,
      clipboard copies, downloads, screenshots, removable drives, network changes, Bluetooth
      connections, do-not-disturb and a pending restart. Only three new view models were needed --
      most of them are announcements wearing different labels -- and the expanded panel became a
      generic list of activity rows rather than one hardcoded row per feature.
- [x] **Phase 10.2** — More watchers: the default audio output moving (which on a machine with
      virtual mixers is a question Windows answers nowhere), the wireless network by name, monitors
      connecting and disconnecting, low disk space, and battery. Battery registers only where there
      is one, so it costs a desktop nothing and lights up on a laptop.

Each phase should build, run, and be visibly testable before moving to the next. Commit at the end of every phase.
