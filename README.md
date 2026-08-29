<div align="center">

<img src="assets/app.png" alt="DT Hub" width="112" height="112">

# DT Hub

**A graphical hub for mirroring several Android devices at once, on Windows.**

Built on [ADB](https://developer.android.com/tools/adb) and
[scrcpy](https://github.com/Genymobile/scrcpy). Not an emulator: your apps keep
running on your real phones.

[![Build](https://github.com/Falcomfr/DtHub/actions/workflows/ci.yml/badge.svg)](https://github.com/Falcomfr/DtHub/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

---

> **Status: early development (v0.1).** The feature list below describes the
> target of the 0.x cycle. See [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md)
> for what actually works today, and [docs/ROADMAP.md](docs/ROADMAP.md) for the plan.

## What it does

DT Hub turns a pile of ADB and scrcpy command lines into a normal Windows
application. You pair your phones once, pick the apps you want, save the
selection as a profile, and press one button.

- **Several phones at once**, over USB or wireless debugging.
- **Several apps per phone**, including apps installed under a secondary
  Android user: work profile, second space, cloned app. Any user id works,
  not just the usual ones.
- **Launch profiles.** A profile is a named list of sessions to open together.
- **Stacked windows.** Every session opens at the same position and size, so
  they overlay exactly. `Ctrl+Tab` cycles through them.
- **Adaptive sizing.** Presets at 60 / 70 / 80 / 90 percent of the active
  monitor, plus borderless fullscreen. All percentages are configurable.
- **Configurable shortcuts** with a real editor and conflict detection.
- **Clipboard sync** both ways, using scrcpy's native mechanism.
- **Nothing to install beforehand.** No Android Studio, no ADB, no Java,
  no .NET runtime.

## What it deliberately does not do

DT Hub displays, launches, arranges and forwards your own input. It contains
no bot, no macro, no input replay, no screen recognition for playing, and no
way to synchronise actions across sessions. One user gesture is one action.

## Screenshots

_Placeholder. Screenshots will be added once the main window is complete._

<!--
| Home | Devices | Apps |
|---|---|---|
| ![Home](docs/images/home.png) | ![Devices](docs/images/devices.png) | ![Apps](docs/images/apps.png) |
-->

## Install

Download `DtHub-Setup.exe` from the
[latest release](https://github.com/Falcomfr/DtHub/releases/latest) and run it.
It installs per user, adds a Start menu entry, and updates itself from GitHub
releases. No administrator rights are required.

Requirements: Windows 10 version 1809 or later, x64.

## First run

### Connect over USB

1. On the phone, open **Settings > About phone** and tap **Build number**
   seven times to unlock developer options.
2. In **Developer options**, enable **USB debugging**.
3. Plug the phone in and accept the authorisation prompt on the phone screen.

DT Hub picks it up automatically.

### Connect over Wi-Fi

Android 11 and later. Phone and PC must be on the same network.

1. In **Developer options**, enable **Wireless debugging**.
2. Tap **Pair device with pairing code**. The phone shows an IP address,
   a pairing port and a six digit code.
3. In DT Hub, choose **Add a phone > Wi-Fi** and type those three values.

DT Hub pairs, discovers the connection port over mDNS and connects. On later
launches it reconnects on its own: already connected devices first, then the
last known address, then mDNS discovery.

You never have to type an ADB command.

### Then

Pick the apps you want on each phone, in the **Apps** page. Cloned apps and
work profiles appear as separate entries with their Android user shown next to
them. Save the selection as a profile, go back home, press **Launch**.

## Default shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+Tab` | Next session |
| `Ctrl+1` to `Ctrl+4` | Size presets, 60 / 70 / 80 / 90 percent |
| `Ctrl+5` | Borderless fullscreen |
| `Ctrl+R` | Re-centre and re-stack every window |
| `Ctrl+0` | Close every session |

Shortcuts only apply while a window managed by DT Hub is focused, so
`Ctrl+Tab` keeps working normally in your browser. All of them can be
reassigned in **Settings > Shortcuts**.

## Where your data lives

`%LOCALAPPDATA%\DtHub\` holds `settings.json`, `devices.json`,
`profiles.json`, a metadata `cache/` and rotating `logs/`. Pairing codes and
secrets are never written to the logs. Uninstalling removes the application;
delete that folder to remove the settings too.

## Limitations

- Windows only, x64 only.
- Wireless pairing requires Android 11 or later. USB works further back.
- Launching an app under a secondary Android user requires that user to be
  running on the phone.
- Some apps refuse to run on a secondary display or block mirroring
  altogether. That is the app's choice and DT Hub does not work around it.
- Performance depends on your network and on the phone's encoder.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
git clone https://github.com/Falcomfr/DtHub.git
cd DtHub
dotnet build DtHub.slnx
dotnet test  DtHub.slnx
dotnet run --project src/DtHub.App
```

Contributors and AI agents should read [AGENTS.md](AGENTS.md) first: it
documents the architecture, the conventions and the hard rules.

## Security

DT Hub never disables your antivirus, never adds exclusions and never asks for
administrator rights during normal use. Third party tools are downloaded from
their official sources and verified. Every dependency URL is listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licences

DT Hub is released under the [MIT licence](LICENSE). Third party components
keep their own licences, listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Not affiliated

DT Hub is an independent open-source project and is not affiliated with
Genymobile, Google, device manufacturers, or applications mirrored through it.
All trademarks belong to their respective owners.
