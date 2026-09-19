# Implementation status

Legend: **DONE** finished and verified, **IN PROGRESS** underway,
**TODO** not started, **BLOCKED** requires external action.

Last updated: 2026-09-18

## Verified on real hardware

Xiaomi 13T, Android 16, primary profile "Alice Martin" and cloned
profile "XSpace" (999), DOFUS Touch installed on both.

Second device since 2026-09-13: Xiaomi Mi 9T Pro, Android 11, two
accounts. All four accounts across the two phones were opened at once,
with displays ready in 689 and 1124 ms on the Mi 9T Pro side. The 13T
Pro was at the time registered twice by ADB, under its address and
under its mDNS name, and appeared only once in the list.

| Element | Status |
|---|---|
| Downloading and verifying ADB from Google | DONE |
| Detecting the phone over Wi-Fi, including under its mDNS name | DONE |
| Reading Android profiles and classifying their types | DONE |
| Detecting the game on both profiles, activity resolved | DONE |
| Automatic reconnection after a drop | DONE |
| Displaying instances in the startup window | DONE |
| Actually opening both game windows | DONE |
| Game displayed full screen on the virtual display, no black bars | DONE |
| Exact overlay, same position and same size | DONE |
| Anchoring the game block and placing the configurator | DONE |
| The twelve keyboard shortcuts, three contexts each | DONE |

## Core

| Element | Status |
|---|---|
| Running processes without a console, bounded, cancellable | DONE |
| Long-lived process with output read as it streams | DONE |
| ADB client, errors translated into actionable messages | DONE |
| Parsers for `devices`, `getprop`, `mdns`, `pair`, `connect` | DONE |
| Manifest of verified dependencies, ADB and scrcpy | DONE |
| JSON persistence tolerant of corruption | DONE |
| Discovery and remembering of phones | DONE |
| Assisted Wi-Fi pairing, code never logged | DONE |
| Automatic reconnection in three stages | DONE |
| Android profiles, no assumed identifier | DONE |
| Discovery of game instances per profile | DONE |
| Launching via ADB on any profile | DONE |
| Independent scrcpy sessions, one virtual display per session | DONE |
| Window stacking, nine-position grid | DONE |
| Shortcuts, validation, conflicts, Win32 registration | DONE |
| Settings, merging of remembered instances | DONE |

## Interface

| Element | Status |
|---|---|
| Startup window, continuous monitoring | DONE |
| Wi-Fi pairing panel, network pre-fill | DONE |
| Floating configurator, `Ctrl+P`, free corner | DONE |
| Windows tab: position, size, screen | DONE |
| Devices tab: status, checkbox, name, restart | DONE |
| Shortcuts tab: editing, conflicts, restore | DONE |
| Dark theme. There is no light palette | DONE |
| Dark title bar on windows that keep Windows' native one | DONE |
| Named type scale, six steps | DONE |
| Logging with rotation, accessible folder | DONE |
| Tabbed frame: dock, reorder, pop out, close | DONE |
| First-launch preparation window, named sources | DONE |
| Incident report, redacted, to copy and send | DONE |
| Interface in English, French and Spanish, including brand sheets | DONE |
| Quest tracking backed by papycha.fr, in its own window | DONE |
| Keyboard: Enter and Escape on dialog boxes | DONE |
| Per-account state, session time and status line on the row | DONE |
| Band of device vitals pinned under the account list | DONE |
| Colour per account, on the row and on the frame tab | DONE |
| Colour on a free game window's frame | DONE (Windows 11, measured by `build/sonde-bordure`) |
| Dungeon list in aligned columns, headings corrected | DONE |
| Motion: panel, tab underline, row appearance | DONE |

## Distribution

| Element | Status |
|---|---|
| Single-file publishing, 60 MB, no installer | DONE |
| Decision: no installer and no Velopack | DONE |
| Authenticode signing | TODO (not necessary for personal use) |
| Public GitHub repository | TODO |
| First tagged release | TODO |
| GitHub Actions build and tests | DONE |
| Pipeline: shape, restricted permissions, pinned actions | DONE |
| Artifact checks: single file, minimum size floor | DONE |
| Update from the repository, hash verified | DONE |
| Attribution to papycha.fr, outside the scope of the MIT license | DONE |
| History purged of development screenshots | DONE |

## Compatibility

Audited on 2026-08-31, device by device and machine by machine.

| Element | Status |
|---|---|
| No hardcoded path, IP address, or serial number | DONE |
| Reading properties via lists of alternative keys | DONE |
| Display resolution computed from the PC monitor, not the device | DONE |
| No Android profile identifier inferred or assumed | DONE |
| Numeric parsing under invariant culture | DONE |
| Android version checked before launch | DONE |
| Resolution fallback stepping down the tiers, down to 720 | DONE |
| scrcpy refusals sorted into categories before retrying | DONE |
| Game copies with a derived package name detected | DONE |
| Unreadable profile list reported | DONE |
| Awareness of per-screen scaling | DONE |
| Menu paths valid for a tablet | DONE |
| Windows on ARM | BLOCKED (scrcpy is not distributed there) |
| Brand sheets verified on something other than Xiaomi | TODO |
| Launch on a secondary profile verified on a second device | DONE |

## Remaining work

| Element | Status |
|---|---|
| Full test of launching both accounts | DONE |
| Additional options in the Windows tab | TODO (to be defined) |
| Mirroring settings exposed in the interface | TODO |
