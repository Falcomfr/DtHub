# MakeInTouch apps in the Devices tab

**Date:** 2026-10-10
**Status:** approved design, not implemented

## Goal

DT Hub presents two DOFUS Touch companion apps by MakeInTouch, helps the
player install them on a phone or tablet, and once installed lets them open
it in a window on the PC from a compact line, choosing the device. These apps
are opened now and then, not on every phone at every launch: they get one
line each, not a row under every device. The two help questions
at the bottom of the Devices tab move behind a single "Help" button to make
room.

The two apps:

| App | Package | One line (from its store page) |
|---|---|---|
| DTStuff | `com.makeintouch.dtstuff` | Optimise your gear and build the best stuffs for DOFUS Touch. |
| OcrApp | `com.makeintouch.ocrapp` | Your companion for the Ochre Dofus. |

MakeInTouch gave its agreement to show the names and the icons of both apps
in DT Hub (stated by the project owner on 2026-10-10). The icons are taken
from their Play Store pages and bundled with the application, as
`papycha.png` already is, with an entry in `THIRD-PARTY-NOTICES.md`.

## What exists today

- Bottom of the Devices tab (`ConfiguratorWindow.xaml`, around line 960): two
  quiet buttons, `FrozenWindowHelp` and `DeadInputHelp`, shown when
  `Instances.ShowsWindowHelp` is true. They open `SleepHelpWindow` and
  `InputHelpWindow`.
- Shown apps: `StoredShownApp` (DeviceId, UserId, PackageName, Label) in
  `AppSettingsDocument.ShownApps`, written by `SettingsService.SetShownAppsAsync`,
  turned into rows by `DofusInstanceService.DiscoverShownAppsAsync`. A row is a
  shown app when `Instance.AppLabel` is not null (`InstanceRowViewModel.IsShownApp`).
  Removing one with the bin deletes the record and remembers nothing.
- Installed packages are read with `pm list packages --user N`
  (`DofusInstanceService.TryEnumerateAsync`).
- Launching an app on a given display already exists:
  `AndroidAppLauncher.LaunchAsync(serial, userId, package, component, displayId)`,
  the virtual display id being parsed from scrcpy's output
  (`ScrcpyOutputParser.TryParseVirtualDisplayId`, `ScrcpySession.VirtualDisplayId`).
- Manual test on the 13T Pro, 2026-10-10:
  `am start -a android.intent.action.VIEW -d market://details?id=com.makeintouch.dtstuff`
  opens the app's Play Store page (as a bottom sheet) on the phone's screen.
  An `input tap` on "Install" did not install anything, the phone having
  locked itself meanwhile: automating the tap is out of scope.

## Design

### 1. Bottom of the Devices tab

- A single quiet **Help** button replaces the two questions. It opens a small
  menu holding the two existing entries, which open the same windows as
  today. The button is shown under the same condition as the two questions
  were (`ShowsWindowHelp`).
- Below it, a coloured **MakeInTouch block** holds one entry per app:
  - an app **installed on at least one connected device** is a **compact
    line**: 16 px icon, name, and an **Open** button;
  - an app installed on none is a **presentation**: its icon, its name, its
    one line, and an **Install** button.
- A **close cross** hides the whole block, installed apps included. Once
  hidden, only a very small button (a 16 px icon, with a tooltip naming the
  two apps) remains at the bottom of the tab to show it again. The choice is a
  setting, kept across launches (`PartnerAppsCardHidden`, default false).
- With no device connected, the block still shows the presentations, their
  Install buttons disabled, with "Connect a device first" in muted text.

### 2. Guided install

- **Which device.** The Install button targets a connected device that does
  not have the app. With one such device, it acts at once; with several, a
  small menu lists them by display name.
- **Route A, preferred.** DT Hub opens a scrcpy session with a virtual display
  on that device, like a game window but with no game, titled
  "Play Store - DTStuff" (or OcrApp), then runs
  `am start -a android.intent.action.VIEW -d market://details?id=<package> --display <id>`.
  The player clicks Install with the mouse, on the PC.
- **Route B, fallback.** If the virtual display cannot be opened (no id read,
  scrcpy fails, or the display follows the device lock as D119 describes), the
  same intent is started without `--display`, on the phone's own screen, and
  DT Hub says, in the panel's banner: "Unlock your phone and tap Install."
- **No Play Store.** If the intent finds no handler (`am start` reports
  "Activity not started, unable to resolve Intent"), DT Hub says the Play
  Store is missing on that device. AppGallery is left for later.
- **After the install.** Nothing polls. The existing device scan finds the
  new package, and the app's presentation turns into its compact line; the
  store window is an ordinary session that the player closes like any other.

### 3. Opening an installed app

- **Open** targets a connected device that has the app (user 0). With one
  such device it acts at once; with several, a small menu lists them by
  display name.
- The app opens in a window on the PC, on its own virtual display, like a
  shown app.
- The store window and the app window are **transient**: launched, never
  remembered. They do not reopen at the next launch, do not become accounts,
  and leave no geometry, tab or play time in the settings. A second click
  while that window is open brings it forward instead of opening another.
- Nothing is added to `ShownApps`; the "+" picker is unchanged.

### 4. Texts and languages

All new texts in `Strings.resx`, `Strings.fr.resx` and `Strings.es.resx`:
the Help button and its tooltip, the block's heading, Open, the two one-line
descriptions, Install, the close and show-again tooltips, "Connect a device
first", the route B message, and the missing Play Store message. The app
names are not translated.

## What is not done

- Tapping "Install" for the player (fragile: phone language, layout, lock
  screen, Xiaomi's input injection setting).
- AppGallery for Honor and Huawei devices without Google services.
- Any other partner app, or a configurable list: the two packages are
  constants.
- Installing on several devices in one click.

## Testing

- Unit tests, `DtHub.Tests`:
  - for each app, which devices have it and which lack it, given the
    packages installed per device; clone profiles do not count;
  - the hidden block setting is kept;
  - the store intent's arguments, with and without `--display`;
  - reading "unable to resolve Intent" as a missing Play Store.
- First step of the implementation, before anything else: open the Play
  Store page on a virtual display of the 13T Pro by hand, and record whether
  it renders and accepts clicks. If it does not, route B becomes the only
  route, and this spec is amended before going on.
- The block, its two kinds of entry and the Help menu are checked with the
  throwaway WPF program kept outside the repository, as for earlier changes
  to the list, in the light and dark themes, in French.
