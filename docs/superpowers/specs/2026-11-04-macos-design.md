# DT Hub on macOS and Linux

Asked on 2026-11-04: a Mac version of DT Hub, without any risk for the
Windows one. The game keeps running on the Android phone, as Ankama requires;
the Mac only shows and drives it, like the PC does.

iPhone support is not part of this work. iOS offers no adb, no virtual
display an outside program can create and no cloned apps, so one account per
iPhone at best. Since iOS 27, Xcode 27's Device Hub lets a computer show an
iPhone's screen and send it touches (Developer Mode), and pymobiledevice3 does
it from Windows: a separate track, untried, see D226.

## What the Mac version is

The PC's application, on the Mac: the same main window (devices and their
accounts, windows and shortcuts tabs, profiles, tools, guides, Almanax),
the same launcher, layouts, tabs, full screen, global shortcuts, Wi-Fi
pairing, help, app picker, guides, web tools and updates. Left out:
desktop shortcuts, and what macOS forbids (see below).

## How it is built, and why the PC version cannot break

- **The PC's own code runs on the Mac.** `src/DtHub.Mac` (Avalonia 12)
  compiles `src/DtHub.App/ViewModels/*.cs` and nearly all of
  `src/DtHub.App/Services/*.cs` as they are (csproj, "Shared with the PC"),
  so a change on the PC reaches the Mac without being copied, and the
  Windows CI, which builds `DtHub.Mac` with `-warnaserror`, says when one
  breaks it. `src/DtHub.App` is never edited for the Mac.
- `src/DtHub.Mac/Pc/` holds, under the same names, the Mac counterparts of
  the few WPF-only pieces the shared code names: a small WPF shim
  (dispatcher, clipboard, collection view), the dialogs (`MacDialogService`,
  blocking like WPF's through `Modal`), `WindowPlacements`, and the
  waiting, problem and tabbed windows.
- `src/DtHub.Mac/Views/` are the PC's windows ported to AXAML, on the same
  view models, with the PC's dark theme (`Themes/`).
- `Platform/MacWindowController` is the PC's `IWindowController` on macOS:
  a handle is a CGWindowID, other apps' windows move through the
  Accessibility API, ours through AppKit (`Native/MacNative.swift`). Global
  shortcuts: Carbon (`Native/MacHotkeys.swift`, `MacHotkeyRegistrar`). Web
  windows: our own WKWebView in Avalonia (`Native/MacWebView.swift`), since
  Avalonia's web view cannot block navigations by frame or inject scripts
  first. The Swift files build into `libDtHubMac.dylib`, on a Mac only.
- adb and scrcpy are downloaded on first launch, as on the PC, from the
  macOS entries of `build/dependencies.json` (Google's platform-tools zip,
  scrcpy's .tar.gz, same versions as the PC), with the PC's progress window.
  `ProvisionedTool` falls back to Homebrew's copies only if the download
  fails. Nothing to install by hand.
- Shared code, changes neutral on Windows: `ProcessSession` (job object on
  Windows only), the provisioner (.tar.gz and exec bits off Windows),
  `HotkeyBinding` ("Cmd" for the Windows key on macOS), `Strings` (on macOS
  only, a `MacText_` twin of a key wins and "PC" reads "Mac"), new `Mac*`
  translation keys.

## What macOS forbids, and what the Mac does instead

- **Housing a game window in ours (the PC's tabs).** The tabbed frame is a
  strip right above the tabs' game windows, stacked at the same place; the
  selected one is in front, moving the strip moves them. An app not in
  front may not bring another forward (macOS 14+), so the strip follows the
  game actually in front rather than name a hidden one.
- **Taking a window's frame away from outside.** Full screen keeps scrcpy's
  title bar.
- **Raising another app's window without giving it the keyboard.** The
  grid does not bring every game above other apps' windows, as the PC does.
- **Moving other apps' windows at all** needs the Accessibility permission:
  DT Hub asks for it at startup and opens the setting (`AccessibilityCheck`).

## Building and running on a Mac

```bash
# .NET 10 SDK (dotnet-install.sh into ~/.dotnet works without admin rights)
git clone https://github.com/Falcomfr/DtHub.git && cd DtHub
git switch feat/macos
dotnet build src/DtHub.Mac -warnaserror
dotnet test tests/DtHub.Tests
dotnet run --project src/DtHub.Mac
dotnet publish src/DtHub.Mac -c Release -r osx-arm64   # DT Hub.app, ad-hoc signed
# or with a stable identity, so macOS keeps the Accessibility permission
# from build to build (ad hoc, every build is a new app to it):
dotnet publish src/DtHub.Mac -c Release -r osx-arm64 \
  -p:MacSigningIdentity=<certificate SHA-1 or name> -p:MacSigningKeychain=<keychain path>
```

On the development Mac a local certificate, "DT Hub Local Signing", lives in
its own keychain (`~/Library/Keychains/dthub-signing.keychain-db`). It is
enough for that Mac only; another Mac still needs a Developer ID and
notarization.

Never build `DtHub.slnx` on the Mac: it contains the WPF project. Logs and
settings: `~/Library/Application Support/DtHub/`.

## Checked on the Mac, 2026-11-04 (evening)

MacBook Pro, Apple chip, macOS 27.0.1, Retina; Xiaomi 13T Pro over Wi-Fi
(paired with `adb pair`), three accounts. Seen on screen:

- Main window as on the PC, its three tabs; battery, Wi-Fi band, "Éteindre
  l'écran"; accounts launched by the PC's launcher at the set anchor and
  size, title bar accounted for.
- Tabs: the strip above the stacked games, switching by click and Ctrl+Tab,
  reopened at startup in their tabs.
- Full screen (Ctrl+5) and back, grid (Ctrl+G), side by side (Ctrl+T).
- Ctrl+P hides and shows the panel; the hotkey editor captures a new key.
- Add-device window, app picker (the phone's real apps), Almanax (the day's
  offering), guides (catalogue and a dungeon page), Lomitall.
- adb and scrcpy downloaded into DT Hub's folder and used.
- `DT Hub.app` opened with no `PATH`, as from the Finder: the Accessibility
  request shows, the games open; without the permission they stay where
  scrcpy puts them.
- Quitting leaves no scrcpy and no virtual display.

After the reviews (same evening), from the signed `DT Hub.app` with the
Accessibility permission:

- Tabs: switching by click, Ctrl+Tab, F1 to F3 (a free window and tabbed
  ones); the strip follows a game moved by hand; a tab dragged before
  another, the accounts list following; rows dragged in the panel.
- The phone's adb link cut then back: waiting window, games reopened, all
  in view again (a game used to stay in the Dock).
- Copy and paste: Cmd+A/C/V in a field and in a guide page; the Edit menu.
- The panel's minimize button, and Ctrl+P bringing it back; a placement
  saved off screen comes back inside it.
- The permission kept across a rebuild with a stable signature; the log in
  the PC's format; no fault caught by the new net (one was, the PC's
  kernel32 processor reading, now replaced).

Not seen: the help windows (they open on a phone problem), sound, typing
text in the game, the strip following a game brought forward from outside,
the Papycha sign-in, the map and DofusRoom windows, the fourth grid cell,
JavaScript dialogs and downloads in the web tools, a failed page's retry,
a macOS logout with DT Hub open.

## First check on the Mac, 2026-11-04 (morning, the first minimal window)

MacBook Pro, Apple chip, macOS 27.0.1, Retina (scale 2, 1512x982 points),
Xiaomi 13T Pro over USB with three accounts (main profile, a second user,
XSpace). The Homebrew on that Mac belonged to another account, so the tools
came from their official archives, linked into `~/.local/bin` and found
through `PATH`: .NET SDK 10.0.403, adb 37.0.1, scrcpy 5.0.1, gh 2.103.3.

- `dotnet build src/DtHub.Mac`: 0 warnings. `dotnet test tests/DtHub.Tests`:
  3010/3013. The three failures test Windows-only code the Mac does not use:
  `Win32ShortcutWriterTests` (two tests, `.lnk` through the Windows shell)
  and `SdlFocusFixTests.Ne_plante_pas_quand_scrcpy_tient_encore_le_fichier`
  (relies on Windows file locking). They now use `WindowsFact`: run as
  before on Windows, skipped elsewhere (3010 passed, 3 skipped on the Mac).
- 1. Starts, window "DT Hub", no error in the log.
- 2. The three accounts are listed.
- 3. Each opens on its own virtual display (824x536, 119 dpi), game shown
  at the right ratio, no black bars.
- 4. Fixed: scrcpy's `--window-x/y/width/height` all describe the picture,
  and macOS adds a 32-point title bar above it. `MainWindow` now reserves
  it in the cell (`TitleBarHeight`). Measured after the fix: three windows
  in three quarters, edge to edge, no overlap. The fourth quarter was not
  seen (three accounts only).
- 5. `sdk`: a click opens the game's settings panel; keys reach the phone
  (`dumpsys input`). `uhid`: clicks and keys reach the phone from the
  "scrcpy" input device, Android pointer shown. Typing text into the game
  was not tried (it needs logging in).
- 6. Sound: not checked.
- 7. Closing a window by hand sets its row back to "Ouvrir"; "Tout fermer"
  and quitting leave no scrcpy and no virtual display.
- 8. scrcpy 5.0.1, as on the PC: no argument rejected.
- Fixed in passing: the macOS app menu read "Avalonia Application" with an
  "About Avalonia" item; it now reads DT Hub, without that item.

## Linux (D228)

The same project, `src/DtHub.Mac`, also targets `linux-x64` and
`linux-arm64`. It keeps its name because the Mac session works on it.

Setting up a Linux PC: only the .NET 10 SDK (from the distribution, or
https://dot.net/v1/dotnet-install.sh), then `dotnet run --project
src/DtHub.Mac`. On x86_64, DT Hub downloads Google's Linux platform tools and
scrcpy's Linux archive itself, hash-checked, as on the Mac
(`platform-tools-linux`, `scrcpy-linux` in the manifest). On ARM, or when
that download fails, it falls back to a copy in `~/.local/bin` or the PATH:
scrcpy's archive holds scrcpy, scrcpy-server and adb, to copy there.

Avoid the distribution's scrcpy: Ubuntu 24.04 ships 1.25, too old for
`--new-display`. USB debugging also needs the phone's udev rule on some
distributions (`android-sdk-platform-tools-common` on Debian and Ubuntu).

Checked again on 2026-11-05 in WSL, from an empty home: both archives
downloaded and checked in about two seconds, data in
`~/.local/share/DtHub`, no error in the log. That run found a shared bug,
fixed: without `~/.local/share`, .NET gave an empty data path and DT Hub wrote
everything into "DtHub" under its working folder (`AppPaths` now asks .NET
to create it; Windows unchanged). Links and folders open with `xdg-open`, and
the shortcut editor reads the typed key from Avalonia, not libDtHubMac.

Checked on 2026-11-04 in WSL (Ubuntu 24.04, WSLg):
- build with `-warnaserror`, 0 warnings;
- 3011 tests passed, 3 skipped (Windows only);
- without the tools, the window shows the Linux hint;
- with the three files in `~/.local/bin`, adb starts and the window waits
  for a phone.

Tried with a real phone on 2026-11-05: the owner's Xiaomi 13T Pro lent to
WSL with usbipd (`usbipd bind`, `usbipd attach --wsl`, then read and write
access on its /dev/bus/usb node, which a udev rule gives on a real Linux PC).
The phone and its three accounts were listed, DOFUS Touch opened on its
virtual display, showed, and answered clicks and keys. The very first
opening failed once (the server printed "Aborted"), then never again: not
explained, to watch. Still to do on a real Linux PC:
- items 2 to 7 of the Mac list above;
- window placement: `TitleBarHeight` is the Mac's, and under Wayland scrcpy
  cannot place its window at all (X11 or XWayland needed for the grid).

Since the Mac work of the same evening, window placement, global shortcuts
and the web windows go through `libDtHubMac.dylib`, macOS only. On Linux the
services fall back (`MacServices`): no window control after launch (scrcpy
places its windows from its arguments; no grid, tabs or full screen
afterwards), no global shortcuts, the guides and web tools open in the
browser, adb and scrcpy from the player's install. Not run on Linux since.

## Updates and installing (2026-11-05)

As on the PC, and from the same releases: the release pipeline's `mac`
job adds `DtHub-mac.zip` (the `.app`, by `ditto`) and its `.sha256` to the
draft. `BundleUpdateService` (Infrastructure, used by the Mac alone) asks
for the latest release at startup, downloads the archive in the
background when "Update itself automatically" is checked, checks its
digest, unpacks it with `ditto` and checks its signature with `codesign`;
a copy signed with a certificate also requires the new bundle to meet its
designated requirement (same identifier, same certificate), so an ad hoc
or foreign build is refused. At exit, whatever the way out (the Quit
button, or the last game window closed with the panel hidden), the
running bundle steps aside into the updates folder and the new one takes
its place under the running copy's name; the next startup shows the note
and sweeps, never the old bundle while the new one is missing. A
development build (under the source tree) or Linux is offered nothing.
Checked on this Mac on 2026-11-05: a 0.12.2 in `/Applications` found a
0.12.3 served from a local folder, showed the banner and the note, put it
in place at Quit, started as 0.12.3 with the note "just installed" and the
Accessibility permission kept (same certificate). Checked again after the
PC's review: a copy renamed "DT Hub 2.app" updated in place when it closed
by itself with no window left; an ad hoc 0.12.4 refused by the certificate
check; a digest file that did not come kept the archive, reused at the
next start without downloading it again.

The first install is `curl -fsSL https://dthub.app/install-mac.sh | bash`
(`site/install-mac.sh`, served by the site once on `main`). A file curl
writes carries no quarantine mark, so Gatekeeper opens the app without a
Developer ID, and the archive DT Hub downloads itself is not marked either.
A `.app` downloaded with a browser is refused instead, unless the player
goes through Privacy & Security, "Open Anyway". Checked with a local
archive: refuses while DT Hub runs, installs and opens it, refuses a
wrong checksum and leaves the installed app alone. Its body is one
function called on its last line, so a download cut short runs nothing;
the old copy steps aside until the new one is in place (a failing move,
simulated, left the old version), and an existing copy is updated where it
is, ~/Applications included, never a second one beside it.

Signing in the pipeline: with the `MAC_SIGNING_P12` and
`MAC_SIGNING_PASSWORD` secrets, set on 2026-11-05 from the local "DT Hub
Local Signing" (self-signed, SHA-1 EDD9503D…27DD), so every release keeps
the identity this Mac's builds had. Ad hoc without them: each version would
be a new identity to macOS, and the player would tick Accessibility again
after each update. The import and signing steps were rehearsed here in a
fresh keychain, without any prompt.

Without a Mac at hand, the Check workflow's `mac` job runs the tests on
GitHub's Mac, builds the app and captures what a first start shows
(artifact "mac-captures", with the log).

## Wi-Fi phones on the Mac (2026-11-05)

Two things the PC never meets:

- **macOS's local network protection follows the application that started
  a process.** The adb server outlives DT Hub; once DT Hub has quit, that
  server gets "No route to host" for every phone on Wi-Fi, and the next
  DT Hub, reusing it, saw the phone as refusing it and showed "pair
  again". The Mac now stops the adb server at startup
  (`SweepLeftoversAsync`), so the one the first call starts is this run's.
- **adb's own mDNS discovery finds nothing on a Mac**, whatever
  `ADB_MDNS_OPENSCREEN` says. `BonjourAdbClient` adds what macOS's Bonjour
  sees (`Native/MacBonjour.swift`): after the phone's wireless debugging
  was switched off and on, its new port was found and the phone came back
  by itself ("ReconnectedByDiscovery").

## Audit of 2026-11-10, and known limits

Read from the PC after 0.14.3, whose shared launcher took a service
`MacServices` did not register: the Mac and Linux builds stopped at
startup, and the Check workflow's Mac job still showed green (its start
step was `continue-on-error`).

- **Guarded since:** `MacServicesTests` builds `MacServices` with
  `ValidateOnBuild`, so a missing registration fails `dotnet test` on
  Windows and Linux (the shared graph) and in the Mac jobs of both
  workflows (the Mac's own branch), before anything is published. The
  Check workflow's Mac start is no longer optional.
- **Still only seen by the CI:** the app starts, downloads adb and scrcpy
  and asks for Accessibility; that dialog is modal, so the CI never sees
  the main window on the Mac (Linux's start does build it).
- **Known limits, unverified without a Mac:** everything listed under
  "Not seen" above; Intel Macs are not supported (the app, the native
  library and the scrcpy archive are arm64 only, `install-mac.sh` says
  so); F1 to F8 reach DT Hub only with Fn, or with "Use F1, F2, etc. keys
  as standard function keys", on a Mac keyboard; macOS 15 and later refuse
  a global shortcut whose only modifier is Option (or Option and Shift),
  and the editor then shows it as refused; no Cmd+W or Cmd+M on DT Hub's
  own windows; a copy run from a browser's download, translocated by
  Gatekeeper, cannot replace itself and keeps announcing the update.
- **Release risk:** without the `MAC_SIGNING_P12` secret the release is
  signed ad hoc, and every installed copy, signed with the certificate,
  refuses it (`SameSignerAsync`): Mac players would stay on their
  version with only a banner and a log line. The job does not fail then.

## Rules for the next session

- Never touch `src/DtHub.App`. Anything in `Core` or `Infrastructure` must
  leave Windows unchanged: guard Mac-only behavior with
  `OperatingSystem.IsMacOS()` or put it behind an interface, as
  `IWindowController` and `IHotkeyRegistrar` already are.
- New files in UTF-8 with BOM (`.editorconfig`). Every push to the branch
  runs the Windows CI, which is the judge for the PC side.
- Work on `feat/macos`. Merging into `main` is the user's call.

## Next steps

1. The player ticks DT Hub in Accessibility once, then the layouts work
   from the `.app` as they do in development.
2. The first real release with the `mac` job, once merged: the job has
   only been rehearsed step by step on this Mac, never run by GitHub.
3. No notarization, by choice (2026-11-05): the install goes through
   `install-mac.sh`. The site's install page does not mention the Mac yet.
