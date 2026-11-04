# DT Hub on macOS

Asked on 2026-11-04: a Mac version of DT Hub, without any risk for the
Windows one. The game keeps running on the Android phone, as Ankama requires;
the Mac only shows and drives it, like the PC does.

iPhone support was dropped on purpose: iOS offers no adb, no virtual display
an outside program can create, no input injection and no cloned apps. None of
what DT Hub does is possible there.

## What the Mac version is

The PC's application, on the Mac: the same main window (devices and their
accounts, windows and shortcuts tabs, profiles, tools, guides, Almanax),
the same launcher, layouts, tabs, full screen, global shortcuts, Wi-Fi
pairing, help, app picker, guides and web tools. Left out: updates (the
PC's install a Windows executable; a Mac release channel is to decide),
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
2. Updates on the Mac: a macOS job building `DT Hub.app` for releases, and
   an update target that installs it. The user's decision, with the next
   point.
3. Notarization: an Apple developer account (99 $ a year), without which
   another Mac's Gatekeeper refuses the ad-hoc signed app.
