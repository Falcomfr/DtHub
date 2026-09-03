<div align="center">

<img src="assets/app.png" alt="DT Hub" width="112" height="112">

# DT Hub

**Play several DOFUS Touch accounts side by side on Windows, from your own phones.**

Built on [ADB](https://developer.android.com/tools/adb) and
[scrcpy](https://github.com/Genymobile/scrcpy). Not an emulator: the game runs
on your real phone, DT Hub only mirrors it and arranges the windows.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

---

> **Status: early development (v0.1).** Usable, but rough edges remain. See
> [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md).

## What it does

You install DOFUS Touch once on your phone's main profile, and again on a
cloned profile such as Xiaomi's Second Space or Dual Apps. Each profile holds
its own account. DT Hub finds every one of them and opens each in its own
window on your PC.

- **One file to run.** `DtHub.exe`, no installer, no administrator rights,
  no .NET to install. It downloads ADB and scrcpy on first launch, telling you
  what it fetches and from where, and writes nothing next to itself: everything
  it keeps goes under `%LOCALAPPDATA%\DtHub`. Deleting that folder resets it.
  Windows itself unpacks a handful of graphics libraries into
  `%TEMP%\.net\DtHub` to run a single-file program; DT Hub clears the ones
  earlier versions left there every time it starts.
- **It puts itself in your Start menu**, pointing at wherever you keep the file.
  It never copies or moves itself. Move the file and the shortcut follows on the
  next launch.
- **First launch asks one question**: which instances to open. After that it
  just opens them.
- **Any Android profile id works.** A cloned profile is not always 999, and
  DT Hub never assumes it is.
- **Stacked windows.** Every instance opens at the same place and size, so
  they overlay exactly. `Ctrl+Tab` cycles through them.
- **A floating configurator**, shown or hidden with `Ctrl+P`, holding the
  window position, the device list and the shortcuts.
- **Clipboard sync** both ways, through scrcpy's own mechanism.
- **A quest guide**, opened with `Ctrl+Q`, that reads
  [papycha.fr](https://papycha.fr) beside the game and remembers where you
  stopped. It shows the site's own pages; it never plays for you.
- **English, French and Spanish**, following your Windows display language, and
  English for anything else.
- **A report you can send.** When something fails, DT Hub shows what happened
  and hands you a report you can paste anywhere: serial numbers, addresses,
  account names and your Windows user name are stripped out of it first.
  Nothing is ever sent on its own.

## What it deliberately does not do

DT Hub displays, launches, arranges and forwards your own input. It contains
no bot, no macro, no input replay, no screen recognition for playing, and no
way to mirror one action across several accounts. One gesture is one action,
on one account.

## How it works

Each instance gets its own Android virtual display, created by scrcpy. The
game is then started on that display, for that Android profile, through ADB.
Nothing on the phone is modified, and scrcpy itself is used unchanged. The
reasoning is in [docs/DECISIONS.md](docs/DECISIONS.md).

## First launch

### Over USB

1. On the phone, open **Settings > About phone** and tap **Build number**
   seven times.
2. In **Developer options**, enable **USB debugging**.
3. Plug the phone in and accept the prompt on its screen.

The phone appears on its own, there is no button to press.

### Over Wi-Fi

Android 11 and later, phone and PC on the same network.

1. In **Developer options**, enable **Wireless debugging**.
2. Tap **Pair device with pairing code**.
3. DT Hub finds the phone on the network and fills in its address and port.
   Type the six digit code and press **Pair**.

Afterwards it reconnects on its own at every launch, even when the phone's
port changes after a reboot.

## Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+P` | Show or hide the configurator |
| `Ctrl+Tab` | Next instance |
| `Ctrl+Shift+Tab` | Previous instance |
| `Ctrl+R` | Put every window back in place |
| `Ctrl+T` | Tile the windows side by side |
| `Ctrl+1` … `Ctrl+4` | Resize every window to one of four steps |
| `Ctrl+5` | Full screen |
| `Ctrl+Q` | Show or hide the quest guide |
| `Ctrl+0` | Close every window |

Shortcuts only apply while a DT Hub window is focused, so `Ctrl+Tab` keeps
working normally in your browser. All of them can be changed in the
configurator.

## Where your data lives

`%LOCALAPPDATA%\DtHub\` holds `settings.json`, `devices.json`, the downloaded
tools and rotating logs. Pairing codes are never written to the logs. Delete
that folder to reset everything.

## Limitations

- Windows only, x64 only.
- Wireless pairing needs Android 11 or later. USB works further back.
- Virtual displays need Android 11 or later.
- A cloned Android profile must exist on the phone; DT Hub does not create one.
- The first launch needs an internet connection, once: 19 MB of tools, with a
  window showing what is being fetched.
- DT Hub asks GitHub whether a newer version exists every time it starts. It
  never installs anything without asking, and the **Automatic updates** setting
  decides whether the new version is prepared or merely announced.
- The guide windows need Microsoft's WebView2, which ships with Windows 11 and
  reaches Windows 10 through Edge. Without it, DT Hub says so and everything
  else still works.
- The file is not signed, so SmartScreen warns the first time you run it.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
git clone https://github.com/Falcomfr/DtHub.git
cd DtHub
dotnet build DtHub.slnx
dotnet test  DtHub.slnx
dotnet run --project src/DtHub.App
```

Contributors and AI agents should read [AGENTS.md](AGENTS.md) first.

## Security

DT Hub never disables your antivirus, never adds exclusions and never asks for
administrator rights. Third party tools are downloaded from their official
sources and their checksum is verified before anything is extracted. Every
dependency URL is listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Shortcuts use `RegisterHotKey`, not a keyboard hook: only the combinations you
configured are ever intercepted, and only while a DT Hub window is focused.

## Contribuer, signaler

Les défauts se signalent par une [issue](https://github.com/Falcomfr/DtHub/issues/new/choose) ;
l'application sait composer un rapport déjà biffé de ce qui vous identifie.
Une faille se signale en privé, voir [SECURITY.md](SECURITY.md). Les conventions
du dépôt sont dans [CONTRIBUTING.md](CONTRIBUTING.md) et `AGENTS.md`.

## Licences

DT Hub is released under the [MIT licence](LICENSE). Third party components
keep their own, listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

One file is not covered by that licence: `assets/quest-successes.json` is a
table of quest and achievement titles compiled by [papycha.fr](https://papycha.fr),
which keeps its own rights over it. DT Hub uses it only to know in which order
to open the site's own pages, and always sends you to the site for the guides
themselves.

## Not affiliated

DT Hub is an independent open-source project. It is not affiliated with,
endorsed by, or connected to Ankama, Genymobile, Google, papycha.fr, or any
device manufacturer. DOFUS and DOFUS Touch are trademarks of Ankama. DT Hub
embeds no Ankama artwork, code or game assets, and does not modify the game in
any way. Screenshots in this page show the app running, so the game it mirrors
appears in them.
