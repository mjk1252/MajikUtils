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
- [x] **Phase 7** — Convert to taskbar buttons: two independently pinnable panels
      (Launcher, Drawer), per-window AppUserModelIDs, live CPU/GPU button icon. The real taskbar
      is left alone.
- [x] **Phase 8** — Per-button jump lists via `ICustomDestinationList` (WPF's own `JumpList` binds
      to the process AppUserModelID, so it cannot give the buttons different lists). Every button
      carries *Exit Dock*, which is what makes quitting reachable now that the tray icon is gone.
- [ ] **Phase 9 (stretch)** — A "Recent" jump-list category on the drawer, and a Frequent/Recent
      category per stack, using `AppendKnownCategory` / `AppendCategory`.

Each phase should build, run, and be visibly testable before moving to the next. Commit at the end of every phase.
