# An iPhone or iPad is an account, like an Android one

Asked on 2026-11-11: an iPhone or iPad row only has "Open/Close" while an
Android account has tabs, F1-F8, Ctrl+Tab, arranging, full screen, name,
colour, lock, teams, startup and more. The owner wants **everything an
Android account can do, as far as iOS allows, in one release**, and accepts
that part of the iOS code stays separate from the Android one, as long as
the features are many and work well. Extends D227 (iPhone on the PC).

No iPhone or iPad is on hand. A player's iPad Pro M4 (iPadOS 27.2.1) shows
its screen but took no click; its sound is now off (branch
`fix/ipad-sans-son`, included here), the only difference from the last
iPhone that took clicks.

## Goals

One iPhone or iPad = one account in the same list as the Android accounts,
under its own device header, with:

1. **Row**: open/close, rename, colour, lock, tab toggle, drag handle,
   F-key badge, week's playtime, activity dot and status line, problem text
   with the help link, "at startup" checkbox, team membership.
2. **Window**: Ctrl+Tab and Ctrl+Shift+Tab, F1-F8, Ranger (Ctrl+R), Côte à
   côte (Ctrl+T), Grille (Ctrl+G), Ctrl+1 to Ctrl+4, Ctrl+5 and Échap, the
   account's colour on the frame, its key and name in the title, its place
   remembered and restored, window order following the list, the tabbed
   frame with its cross.
3. **Startup and teams**: opened by "Lancer" at startup and by a team like
   any account; closed by its cross, it leaves the next startup (D207).
4. **Game**: open DOFUS Touch on the device when the window opens (if not
   already running), and "Redémarrer le jeu" (stop then launch), through
   pymobiledevice3's app service.
5. **Device header**: close every window of the device, forget the device,
   the dim switch (iOS cannot switch its screen off, D227 follow-up).
6. What iOS already has stays: rotation button, dimming, unplug and return
   in place, sound on the PC for iPhones.

## Non-goals

- Several accounts on one device: iOS has no profiles nor virtual displays.
- Quality, distance, phone format, game mode, reduced render, the
  "Corriger" settings: they drive scrcpy or Android, not the iOS stream.
- Battery, heat, Wi-Fi in the title; keep-awake; real screen off.
- Wi-Fi iPhones (USB only, as today).
- macOS and Linux: their `IPhoneScreens` is a stub; nothing changes there.

## Anti-goals

- Any adb or scrcpy command aimed at an iPhone's UDID. Today about thirty
  paths reach a device from a scrcpy session or an account's DeviceId
  (screen off, sound relay, health, game watch, reopen, restart, `Routable`):
  none may ever see an iOS account.
- Breaking an Android path, or the iPhone that already works.
- Two lifecycles disagreeing: a tab, a playtime or a startup flag kept for a
  window that is gone, or dropped for one that only reconnects.

## Constraints

- `ScrcpySession` is closed to DtHub.App (internal constructor and setters,
  private registry) and its life follows scrcpy's output. An iPhone window
  therefore cannot be a `ScrcpySession` without reworking Core.
- The iPhone's process is Python (`serve-web`); its window is a WPF window
  of DT Hub's own process, holding a WebView2. `ResolveWindowAsync` finds a
  game window by its owner PID, which is wrong for it: its handle must be
  known up front.
- The window's aspect is 0 before the first frame and changes on rotation;
  Android's `SourceAspectRatio` is fixed per session.
- Nothing can be tried on a device. What a device must confirm is listed
  under "Verification".

## Approaches

- **A. The iPhone window as a scrcpy session** (a fake process and handle
  registered in `ScrcpySessionManager`). Every Android path would work for
  free, but every adb path would fire on the UDID, the session type would
  have to open up, and its life would follow a process that the iPhone
  window outlives on unplug. Rejected.
- **B. Every feature copied for the iPhone** in `IPhoneScreens` and the
  iPhone row. Quick for the first features, then two versions of each
  drift apart, and every new Android feature has to be redone. Rejected.
- **C. Chosen: an iOS account, plus one list of game windows.** The account
  side gets an iOS kind (stored, listed, shown with the Android row
  template). The window side gets a small common view, a *game window*
  (key, handle, aspect, kind), built from the scrcpy sessions and the open
  iPhone windows. Every window-only feature (Ctrl+Tab, F-keys, arranging,
  sizes, full screen, title, colour, geometry, order, tabs) reads that list
  instead of `ManagedSessions`/`AccountSessions`. Device paths keep reading
  scrcpy sessions only, so they never see an iPhone. iOS lifecycle (open,
  unplug, reopen, close, game launch) stays in `IPhoneScreens`, raising the
  same events the launcher already reacts to.

The owner asked for the recommended way, all at once.

## How it is built

### Account

- `StoredInstance` and `DofusInstance` gain `Platform` (`Android` default,
  `Ios`). An iOS account's key is `UDID|0|ios` (`PackageName` = `ios`,
  fixed); the DOFUS Touch bundle id goes in its own field once found.
- An iPhone listed by usbmux becomes an account the first time it is seen
  (as discovery does for a new Android profile); it is never forgotten by the
  Android profile sweep, only by "Oublier".
- The instance list builds device headers from Android devices **and**
  listed iPhones, and keeps iOS rows whose device is listed; the separate
  `IPhones` list and `IPhoneRowViewModel` go away.
- The row template hides what iOS cannot do: quality, distance, phone
  format, game mode, reduced render, "+" (add app). Status line: "Ouvert",
  "Débranché, en attente", "Démarrage…", or the problem with the help.

### Game windows

- `GameWindow(Key, Handle, Aspect, Kind)`. The launcher's list is the
  scrcpy sessions' windows (as today, `Aspect = SourceAspectRatio`) plus the
  open iPhone windows (`Aspect` = picture shape, updated on rotation), in
  list order, minus locked and tabbed ones where today's `ManagedSessions`
  excludes them.
- `WindowManagerService` methods that take sessions for placing, sizing,
  cycling, focusing, recolouring and retitling take game windows instead.
  The grid's special case for iPhones (`TakeScreensForArranging`) goes.
- `IPhoneMirrorWindow.Owns` stays for keyboard scope, but full screen must
  arm Échap in front of an iPhone window too.

### Tabs

- The iPhone window docks into `TabbedGameWindow` with the same `Dock`
  (`SetParent`, `WS_CHILD`). A spike on the local fake server (below)
  validates it first: docked, the WebView2 still shows and still posts
  touches.
- *Builder's call, made when writing:* if the spike shows a docked WPF
  window misbehaving (activation, keyboard, clicks), the viewer's WebView2
  is hosted directly in the frame's tab instead, the game-window handle
  then being the frame's child that holds it. iOS accounts do not ship
  without tabs.
- Docked, the window stops resizing itself (`FitTo`); its aspect is pushed
  to the frame instead, which needs the tab's aspect to be updatable
  (`GameTabViewModel` fixes it at attach today).
- `Deactivated` no longer fires for a child: the blur that releases held
  touches and keys is sent when the frame deactivates or the tab changes.
- `KeepOnly` keeps a tab whose key is an open iPhone window.

### Lifecycle

- Open: `IPhoneScreens.OpenAsync` as today, then the game launch, then the
  launcher's "window opened" handling (colour, title, tab, placement,
  startup flag).
- Close by its cross: playtime counted, the account leaves startup (D207).
  Close from DT Hub (button, tab cross, close-all, team): startup flag
  follows the same rules as Android.
- Unplug: the window waits in place (today's behaviour); it stays a game
  window and keeps its tab; playtime pauses.
- *Builder's call, same as Android:* a stream that stops on an error (not
  an unplug) is a failure: the account stays in startup (D207), the window
  shows the reason, and DT Hub reopens it like Android's recovery (same
  attempts and notices); out of attempts, the row says why.
- *Builder's call, same as Android:* "Arrêter le jeu à la fermeture"
  applies to iOS accounts: closing from DT Hub or by the cross stops DOFUS
  Touch on the device when the setting is on. Unplugged, there is nothing
  to stop and nothing is tried.

### Game on the device

- Listing apps, launching and stopping the game use pymobiledevice3's app
  service. *Builder's call:* through the `serve-web` process itself, by
  extra local endpoints added in `MirrorBootstrap`, so they reuse its
  tunnel: whether a second tunnel can live beside the stream's on one
  device is unknown and cannot be tried here. Before the stream is up
  (first open), the same endpoints are reached once the server answers.
- DOFUS Touch is found by name in the app list (its bundle id is unknown
  here) and kept in a field of the account; the account key stays
  `UDID|0|ios`.
- Not found: the window still opens, the row says the game is not
  installed. Launch failure: said on the row, the window stays.

## Verification

- Unit tests: settings round trip of `Platform`; iOS account creation and
  ordering; the game-window list (kinds, locked, tabbed, order); no device
  path given an iOS key; app-list parsing for the bundle id.
- Local fake server (`C:\Dev\dthub-uia\ipadclic`, the tool's real page with
  its placeholders filled): an iPhone window opened, docked into the tabbed
  frame, cycled with Ctrl+Tab and F-keys, arranged, put in full screen and
  back, coloured, retitled, its place restored; a click still posts a touch
  in each state.
- Full suite green, build without warning, format clean.
- **Only a device can confirm**: real touches (clicks on an iPad without
  sound), DOFUS Touch's bundle id and its launch, the unplug/return with a
  tab. Said in the changelog and to the first iOS player.

## Left to the builder

- Exact names and file split, how the game-window list is exposed, the
  status wording (three languages), the order of the work.
- Whether `GameWindow` lives in Core or App, as long as Core keeps no WPF.
