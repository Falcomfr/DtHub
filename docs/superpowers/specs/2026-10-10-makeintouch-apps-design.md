# MakeInTouch apps in the Devices tab

**Date:** 2026-10-10
**Status:** approved design, not implemented

## Goal

DT Hub presents two DOFUS Touch companion apps by MakeInTouch, helps the
player install them on a phone or tablet, and once installed shows them in
the accounts list like a game account, only smaller. The two help questions
at the bottom of the Devices tab move behind a single "Help" button to make
room.

The two apps:

| App | Package | One line (from its store page) |
|---|---|---|
| DTStuff | `com.makeintouch.dtstuff` | Optimise your gear and build the best stuffs for DOFUS Touch. |
| OcrApp | `com.makeintouch.ocrapp` | Set out to conquer the Ochre Dofus with OcrApp. |

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
- Below it, a coloured **card** presents, side by side or stacked depending on
  the width, each MakeInTouch app **installed on no known device**: its icon,
  its name, its one line, and an **Install** button.
- A **close cross** on the card hides it. Once hidden, only a very small
  button (a 16 px icon, with a tooltip naming the two apps) remains at the
  bottom of the tab to show it again. The choice is a setting, kept across
  launches (`PartnerAppsCardHidden`, default false).
- When both apps are installed on at least one device each, the card and its
  small button are gone.
- With no device connected, the card still shows, its Install buttons
  disabled, with "Connect a device first" in muted text.

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
  new package (section 3); the store window is an ordinary session that the
  player closes like any other.

### 3. Rows in the accounts list

- On each device scan, for each device where a MakeInTouch package is
  installed (user 0 only), DT Hub adds a `StoredShownApp` for it **unless the
  player removed it before** on that device.
- Removing one with the bin records its key in a new setting,
  `DeclinedPartnerApps` (a set of `StoredShownApp.Key`). A declined app is
  never added again automatically on that device; ticking it again in the
  "+" picker takes it out of that set.
- The row is **compact**: one line, a 16 px icon instead of 20, no device
  status line, smaller vertical padding. It opens its window like any shown
  app, with its own virtual display, and keeps the bin and restart buttons.
  It is placed after the game accounts of its device.
- The row is compact only for the two MakeInTouch packages; other shown apps
  keep today's layout.

### 4. Texts and languages

All new texts in `Strings.resx`, `Strings.fr.resx` and `Strings.es.resx`:
the Help button and its tooltip, the card's heading, the two one-line
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
  - which apps the card offers given the packages installed per device;
  - auto-add skips a declined key, and ticking the app in the picker clears
    the decline;
  - the store intent's arguments, with and without `--display`;
  - reading "unable to resolve Intent" as a missing Play Store.
- First step of the implementation, before anything else: open the Play
  Store page on a virtual display of the 13T Pro by hand, and record whether
  it renders and accepts clicks. If it does not, route B becomes the only
  route, and this spec is amended before going on.
- The card, the Help menu and the compact row are checked with the
  throwaway WPF program kept outside the repository, as for earlier changes
  to the list, in the light and dark themes, in French.
