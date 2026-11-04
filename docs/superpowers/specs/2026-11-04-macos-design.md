# DT Hub on macOS, first version

Asked on 2026-11-04: a Mac version of DT Hub, without any risk for the
Windows one. The game keeps running on the Android phone, as Ankama requires;
the Mac only shows and drives it, like the PC does.

iPhone support was dropped on purpose: iOS offers no adb, no virtual display
an outside program can create, no input injection and no cloned apps. None of
what DT Hub does is possible there.

This document is the handoff for the session that continues on the Mac. The
code was written and built on Windows; **nothing has run on macOS yet**.

## Scope of the first version

One window lists the DOFUS Touch accounts found on the plugged phones. Each
row opens or closes its account in its own scrcpy window, on its own Android
virtual display. "Tout ouvrir" opens them all, laid out in the same four-cell
grid as the PC's (`TileLayout.Quarter`). Quitting closes every scrcpy window.

Left out for now: Wi-Fi pairing screen, settings screen, hotkeys, tabs,
guides and Papycha, updates, packaging as a signed `.app`.

## How it is built, and why the PC version cannot break

- `src/DtHub.Mac`: an Avalonia 12 project, net10.0, UI written in code (no
  XAML, no bindings). It reuses `DtHub.Core` and `DtHub.Infrastructure` as
  they are. `DtHub.App` (WPF) is not touched.
- `MacServices.cs` is the Mac's share of `DtHub.App/AppServices.cs`. The
  container is validated at startup (`ValidateOnBuild`).
- adb and scrcpy come from Homebrew through `InstalledTool`
  (Infrastructure, tested), instead of the Windows archives of
  `build/dependencies.json`. It looks in `/opt/homebrew/bin`,
  `/usr/local/bin`, then `PATH`: an app opened from the Finder does not
  inherit the shell's `PATH`.
- Shared code, two changes only, both neutral on Windows:
  - `ProcessSession` adopts scrcpy into the Windows job object only on
    Windows. On macOS, `MacApp` stops the sessions on exit instead.
  - New translation keys (`ToolNotInstalled`, `Mac*`) in the three
    `Strings*.resx`.
- `DtHub.Mac` is in `DtHub.slnx`, so the Windows CI builds it with
  `-warnaserror` and checks its formatting (D49: what is outside the solution
  rots). `livraison.yml` only publishes `src/DtHub.App`: the Mac project is
  never shipped from there.

Checked on Windows on 2026-11-04: Release build of the solution with
`-warnaserror`, 0 warnings; 3013/3013 tests; `dotnet format
--verify-no-changes` clean; no vulnerable package.

## Approaches considered

- **Avalonia, reusing Core (chosen).** Same language and same code as the
  PC: device discovery, profiles, scrcpy arguments, display sizing and the
  grid are reused, not rewritten.
- Native Swift app: a better Mac feel, but every piece of Core rewritten,
  and two code bases to keep in step.
- .NET MAUI: Mac Catalyst only, and a weaker desktop story than Avalonia.

## Setting up the Mac

```bash
brew install scrcpy android-platform-tools
brew install --cask dotnet-sdk        # needs the .NET 10 SDK: check `dotnet --version`
git clone https://github.com/Falcomfr/DtHub.git && cd DtHub
git switch feat/macos
dotnet build src/DtHub.Mac
dotnet run --project src/DtHub.Mac
```

Do not build `DtHub.slnx` on the Mac: it contains the WPF project, which only
builds on Windows. The tests do build there (`dotnet test
tests/DtHub.Tests`), but they were only ever run on Windows: a failure about
paths or `\` may be an old Windows assumption in a test, not a regression.
Note which ones fail before changing anything.

Logs: `~/Library/Application Support/DtHub/logs/`. Settings:
`settings.json` in the same folder (no settings screen yet).

## What to check on the Mac, in this order

1. The app starts. A missing service would stop it right away
   (`ValidateOnBuild`).
2. Phone plugged in, USB debugging on: the accounts are listed. Wi-Fi works
   through `adb pair` / `adb connect` in the Terminal for now.
3. "Ouvrir" opens one account: a scrcpy window, the game on its virtual
   display, at the right ratio. **Unverified**: `--new-display` from a Mac
   host. It is created on the phone side, so it should behave as on the PC.
4. **Window placement, to calibrate.** `MainWindow.OpenAsync` converts
   Avalonia's pixels to points (`Screen.Scaling`) for scrcpy's
   `--window-x/y/width/height`, and gives no room to the macOS title bar
   (chrome `(0, 0)`). Its `shortcut:` comment marks this. Check on a
   Retina screen that four windows fill the four quarters without overlap.
5. Keyboard and mouse in the game, in both modes (`sdk` and `uhid`).
6. Sound, if enabled in the settings.
7. Closing a scrcpy window by hand updates its row. "Tout fermer" closes
   all of them. Quitting DT Hub leaves no scrcpy behind: `pgrep scrcpy`.
8. scrcpy from Homebrew is newer than the PC's pinned 5.0.1: watch the log
   for a rejected argument from `ScrcpyCommandBuilder`.

## Rules for the next session

- Never touch `src/DtHub.App`. Anything in `Core` or `Infrastructure` must
  leave Windows unchanged: guard Mac-only behavior with
  `OperatingSystem.IsMacOS()` or put it behind an interface, as
  `IWindowController` and `IHotkeyRegistrar` already are.
- New files in UTF-8 with BOM (`.editorconfig`). Every push to the branch
  runs the Windows CI, which is the judge for the PC side.
- Work on `feat/macos`. Merging into `main` is the user's call.

## Next steps, once the above works

1. Package a `.app` (`dotnet publish -r osx-arm64`, `Info.plist`, ad-hoc
   `codesign`), then decide with the user about an Apple developer account
   (99 $ a year) for notarization.
2. A Mac `IWindowController` (Accessibility API) to rearrange windows after
   launch, which unlocks the PC's grid and fullscreen logic.
3. Wi-Fi pairing screen, settings, then hotkeys (`IHotkeyRegistrar`).
