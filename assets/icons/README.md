# Taskbar button icons

Drop image files here to replace the artwork MajikUtils draws for its taskbar buttons.

## Filenames

| File | Button |
|---|---|
| `drawer.png` | MajikUtils Drawer |
| `shelf.png` | MajikUtils Shelf |
| `stack-<folder>.png` | The stack for that folder, e.g. `stack-downloads.png` for `C:\Users\me\Downloads` |

Stack names are matched lowercased, on the folder's own name — not its full path and not its id.
A stack with no file here keeps using the folder's real Explorer icon, which is usually what you
want.

`.ico` works too and is checked after `.png`. For an `.ico` containing several sizes, MajikUtils uses
the largest frame.

## Format

- **Square**, with a transparent background.
- **256×256 PNG** is the safe choice. Windows renders the taskbar at 16–32px depending on DPI and
  scales from what you supply, so anything smaller than 64px looks soft on a high-DPI display.
- Keep the artwork slightly inside the edges. Windows does not pad taskbar icons, so a design that
  runs to the border sits noticeably larger than its neighbours.

## Two places to put them

- **Here** (`assets/icons/`) — copied next to the exe on build, so these are what a build ships with.
- **`%LOCALAPPDATA%\MajikUtils\icons\custom\`** — checked first, and needs no rebuild. Use this to try an
  icon out, or to change one on an installed copy.

## After changing an icon

Restart MajikUtils. Icons are read once at startup.

Nothing needs converting by hand: MajikUtils regenerates the `.ico` a pinned button needs under
`%LOCALAPPDATA%\MajikUtils\icons\` from whatever icon it ends up using. Those files are named with
a hash of the artwork, deliberately — the shell caches a button's icon against the path it read it
from and will not re-read a file whose name has not changed, so a new icon has to arrive at a new
path to be picked up at all. Older hashes are cleaned up automatically.

If a button still shows old artwork after a restart, that is Windows' own icon cache: unpin it,
restart MajikUtils, and pin it again.
