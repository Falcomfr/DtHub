# Third-party components

DT Hub is published under the MIT licence. It relies on third-party
components that keep their own licence. This file lists those components,
how they reach the user, and the obligations that follow from that.

No copyright notice or third-party licence text may be removed.

## Overview

| Component | Licence | Distribution mode |
|---|---|---|
| scrcpy | Apache License 2.0 | Downloaded from GitHub on first launch |
| SDL (patched) | zlib | File embedded in the executable, written over scrcpy's |
| Android SDK Platform Tools (adb) | Android SDK License Agreement | Downloaded from Google on first launch |
| .NET runtime | MIT | Included by the self-contained publish |
| CommunityToolkit.Mvvm | MIT | NuGet package |
| Serilog and its sinks | Apache License 2.0 | NuGet packages |
| Microsoft.Extensions.* | MIT | NuGet packages |
| Microsoft.Web.WebView2 | Proprietary Microsoft licence | NuGet package, engine provided by Windows |
| papycha.fr logo | Author's agreement, see below | File embedded in the executable |
| Discord logo | Discord's brand guidelines, see below | Vector outline in the configurator's XAML |
| DTStuff and OcrApp icons | MakeInTouch's agreement, see below | Files embedded in the executable |
| Names and steps from DOFUS Touch | Ankama content, see below | Read from the game's data server on the user's PC |
| papycha.fr quest data | Proprietary, see below | File embedded in the executable |
| xUnit, coverlet | Apache License 2.0, MIT | Test dependencies, not distributed |

## scrcpy

- Project: https://github.com/Genymobile/scrcpy
- Copyright: Copyright (C) 2018 Genymobile, Copyright (C) 2018-2026 Romain Vimont
- Licence: Apache License 2.0

The Apache 2.0 licence allows redistribution. DT Hub still downloads
scrcpy on the user's machine, from the official archive published by the
project on GitHub, through the same verified mechanism as for ADB. Two
reasons: the installer stays light, and there is only one setup path to
maintain and to test.

The corresponding obligations are met as follows:

- the upstream archive contains its own `LICENSE.txt`, extracted as is
  and kept next to the executable;
- DT Hub does not modify any scrcpy file. The approach used to launch an
  application on a secondary Android user requires no modification, as
  explained in `docs/DECISIONS.md` and
  `third_party/scrcpy/MODIFICATIONS.md`;
- should a modification ever become necessary, it would be recorded in
  that same file, together with a reproducible patch.

The archive's `SDL3.dll` is the exception: DT Hub replaces it with a
patched build of SDL, see the SDL section below.

scrcpy's Windows archive itself contains a copy of `adb.exe`. DT Hub does
not use it: it uses its own, obtained directly from Google, whose version
it controls. The path to it is given by the `ADB` environment variable,
which scrcpy honours.

DT Hub is not affiliated with Genymobile or the scrcpy authors, and does
not use their name or logos as part of its own identity.

## SDL

- Project: https://github.com/libsdl-org/SDL
- Copyright: Copyright (C) 1997-2026 Sam Lantinga
- Licence: zlib

DT Hub embeds a build of SDL release-3.4.18 with one patch, so that a game
window docked in a tab receives text input, and writes it over the
`SDL3.dll` of scrcpy 5.0.1. This is an altered version, not the original
software. The patch, the reason for it, the rebuild procedure and SDL's
licence are in `third_party/sdl/`.

## Android SDK Platform Tools (adb)

- Publisher: Google LLC
- Licence: Android Software Development Kit License Agreement
- Official, versioned source, and therefore of immutable content:
  https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip

  The "latest" address is not used: its content changes without notice,
  and the checksum recorded in `build/dependencies.json` would no longer
  mean anything.

The Android SDK's licence agreement does not allow redistribution of the
binaries. The platform tools are therefore **not** included in the
repository or in the installer. DT Hub downloads them from the official
URL above, on first launch, into the user's data folder, and verifies
the archive before extracting it.

The user is informed of this: on first launch, a window names each
component, its version and the address it comes from, and shows the
progress of the download and then of the verification. No other source
is used.

## .NET

- Publisher: Microsoft Corporation
- Licence: MIT
- https://github.com/dotnet/runtime

DT Hub is published self-contained: the .NET runtime is included in the
application, which the MIT licence allows.

## CommunityToolkit.Mvvm

- Project: https://github.com/CommunityToolkit/dotnet
- Licence: MIT

## Serilog

- Project: https://github.com/serilog/serilog
- Licence: Apache License 2.0
- Packages used: `Serilog.Extensions.Hosting`, `Serilog.Sinks.File`,
  `Serilog.Sinks.Debug`

Application logging. Distributed with the executable.

## Microsoft.Extensions.Hosting and Microsoft.Extensions.Logging.Abstractions

- Project: https://github.com/dotnet/runtime
- Licence: MIT

Generic host and logging abstractions, from the same repository as the
.NET runtime. Distributed with the executable.

## Microsoft.Web.WebView2

- Publisher: Microsoft Corporation
- Project: https://developer.microsoft.com/microsoft-edge/webview2/
- Licence: Microsoft distribution terms, **which are not a free**
  **licence**. See the licence file shipped with the NuGet package.

This is the only dependency of this kind. Two distinct things follow
from that: the NuGet package, which only brings the bootstrapper and the
managed bindings, is distributed with the executable; the rendering
engine itself is **not** distributed by DT Hub, it is provided with
Windows 11 and reaches Windows 10 through Microsoft Edge. Its absence is
detected and reported to the user; it only prevents the guide windows.

## Discord logo

- Source: Discord's brand kit, https://discord.com/branding
- Where: the key that opens the DT Hub server, at the foot of the
  configurator, drawn as a vector outline in `ConfiguratorWindow.xaml`

Discord's brand guidelines let a community use the mark to link to its
own server, which is all this key does. It is shown whole and
unaltered, in a single colour, as those guidelines allow; the colour is
the interface's text colour so the key stays as quiet as its
neighbours.

**The mark is not covered by DT Hub's MIT licence.** It remains
Discord's trademark. DT Hub is not affiliated with Discord.

## DTStuff and OcrApp icons

- Source: the icons of the two apps' Play Store pages,
  https://play.google.com/store/apps/details?id=com.makeintouch.dtstuff and
  https://play.google.com/store/apps/details?id=com.makeintouch.ocrapp,
  at 128 pixels
- Files: `assets/dtstuff.png`, `assets/ocrapp.png`, embedded in the executable
- Recorded on 2026-10-10, **with MakeInTouch's agreement**, as stated by the
  project owner

They mark the two apps in the MakeInTouch block at the bottom of the Devices
tab, next to their names.

**These files are not covered by DT Hub's MIT licence.** The agreement
applies to DT Hub, not to anyone reusing this repository, who must contact
MakeInTouch.

## Calcofus logo

- Source: the Tofu of the site's own logo, https://calcofus.fr/media/10858951a40c870c7eec74fbdf5155a6.png,
  set on a rounded square drawn for DT Hub in the style of the other tools
- File: `assets/calcofus.png`, embedded in the executable
- Recorded on 2026-10-11, **with the site owner's agreement** to the name and
  logo, as stated by the project owner

It marks the Calcofus tile in the DOFUS Touch tools menu.

**This file is not covered by DT Hub's MIT licence.** The agreement applies to
DT Hub, not to anyone reusing this repository, who must contact the owner of
calcofus.fr.

## DofusRoom logo

- Source: the site's own egg, https://www.dofusroom.com/img/new-favicon.png,
  set on a rounded square drawn for DT Hub in the style of the other tools,
  edged in the site's orange
- File: `assets/dofusroom.png`, embedded in the executable
- Recorded on 2026-10-13, **with the right to the name and logo, and to
  change how the site shows inside DT Hub**, as stated by the project owner

It marks the DofusRoom tile in the DOFUS Touch tools menu.

**This file is not covered by DT Hub's MIT licence.** The right applies to
DT Hub, not to anyone reusing this repository, who must contact the owners of
dofusroom.com.

## DofusBook logo

- Source: the site's favicon, https://touch.dofusbook.net/favicon.ico, at 48
  pixels, set on a rounded square drawn for DT Hub in the style of the other
  tools, light so its black stays readable
- File: `assets/dofusbook.png`, embedded in the executable
- Recorded on 2026-10-11, at the project owner's request

It marks the DofusBook tile in the DOFUS Touch tools menu, a plain link that
opens the site in the browser.

**This file is not covered by DT Hub's MIT licence.** It remains DofusBook's.

## Lomitall.fr logo

- Source: the site's own logo, https://lomitall.fr/assets/site/brand/logo.png,
  scaled to 128 pixels and cut to a circle
- File: `assets/lomitall.png`, embedded in the executable
- Recorded on 2026-10-14, at the project owner's request

It marks the Lomitall.fr key beside the guides, which opens the portal in a
DT Hub window.

**This file is not covered by DT Hub's MIT licence.** It remains Lomitall's.

## papycha.fr logo

- Source: https://papycha.fr/wp-content/uploads/2022/04/cropped-luis-192x192.png
- File: `assets/papycha.png`, embedded in the executable
- Recorded on 2026-09-09, **with papycha.fr's agreement**

This is papycha.fr's site icon, the one that identifies their pages in a
browser tab, and the mascot that appears in their own logotype. It marks
the guides button and the credit in the guides window: the two places
where their work is shown.

**This file is not covered by DT Hub's MIT licence.** The agreement
applies to DT Hub, not to anyone reusing this repository, who must
contact papycha.fr.

**The agreement also covers the documentation's screenshots.** One of
them, `assets/screenshots/guide-de-quete.jpg`, shows the guides window
in use, and therefore two of papycha.fr's illustrations within one of
their guides. They are not extracted or reused: the window is shown as
it is, with its "Guides de papycha.fr" (papycha.fr's guides) credit
visible at the bottom. The same reservation applies: this agreement
does not carry over to a fork.

**The site's banner is excluded, and will stay excluded.** It carries
DOFUS Touch's registered logo, its ® symbol and Ankama's official
artwork. No agreement from papycha can hand over what does not belong
to them, and the project does not redistribute any Ankama resource.

## The Almanax on Ankama's portal

- Source: https://krosmoz.com, the Almanax of the day page
- Nothing is embedded: the page is read when the window opens

**No resource is copied or redistributed.** The window does not display
the page: it reads it and draws itself. The portal renders a whole
desktop page, with its background art and mood text, none of which
helps to know what to bring today; only the offering, the bonus, the
quest and the méryde are kept, which are game facts and not graphical
creations. The window's footer names the source on screen, "Almanax
officiel, krosmoz.com".

**What this does not authorise.** No image, no item icon, no portal
banner. The general rule further below applies here as everywhere else:
no Ankama graphical resource enters this repository.

## Names and steps from DOFUS Touch's game server

- Source: the data server DOFUS Touch itself reads its texts from
- Nothing from the game is embedded: the names are read on the player's PC,
  in the player's language, and kept in a local cache for 7 days
- Embedded: `assets/dofus-touch-ids.json`, DT Hub's own pairing of
  papycha.fr's identifiers to the game's identifiers. It holds numbers only,
  no game text.
- Quest step rewards (experience, kamas, reward items and their quantities)
  and the names of reward items are read from the same server in the same way:
  on the player's PC, kept in the same cache, nothing embedded

**No resource is copied or redistributed.** The guides window reads the
official names of quests, dungeons, bosses, key items and achievements, the
levels of quests, the rewards of the quest being read and, on demand, the
official steps of a quest, then draws
them itself. Same principle as the Almanax above: the repository carries no
file extracted from Ankama's game. When the player's language is not French,
the window credits the game under the list and under the steps.

**If the server cannot be read**, the window falls back to papycha.fr's French
titles; the official steps button still shows, and its panel says the steps
could not be read. Nothing breaks.

**What this does not authorise.** No image, no icon, no game file. DT Hub
makes a handful of requests per language, one class at a time: the server
refuses addresses that ask too much.

## Quest data from papycha.fr

- Source: https://papycha.fr, read through its public content API
  (`wp-json/papycha-public/v1/`), whose usage policy is at
  https://papycha.fr/utilisation-contenus-api/
- File: `assets/papycha-catalog.json`, embedded in the executable
- Recorded on 2026-10-11 by DT Hub's own indexing, the last read through
  the site's WordPress API before it closed
- Embedded with papycha.fr's authorization, obtained on 2026-10-17, as a
  local copy that serves when the site's API is unavailable or lacks an
  entry

**This file is not covered by DT Hub's MIT licence.** It holds the quest
catalog as papycha.fr arranges it: 782 quest titles and page addresses,
the sections they are filed under and their order, the achievement each
quest belongs to and its place in it, starting positions and characters,
prerequisites, and the site's 93 dungeons, raids and lairs and 22 paths
with their level, key and soul stone. These are titles of works from the
DOFUS Touch game, and above all an organisation that papycha.fr
established through editorial work that belongs to them.

No quest text, no description, no walkthrough is reproduced: the file
only serves to list, search and order the site's pages, and the
application always opens the site for the content itself. The guides
window is titled after papycha.fr.

DT Hub is not affiliated with papycha.fr or with Ankama. Anyone reusing
this repository under the MIT licence must treat this file separately
and contact papycha.fr.

## xUnit

- Project: https://github.com/xunit/xunit
- Licence: Apache License 2.0

Test dependency only, absent from the distributed application.

## Trademarks and content of mirrored applications

The icons and names of the applications installed on the phone are read
from the device and displayed locally in the picker, solely so the user
can recognise their own applications. They are neither redistributed
nor stored outside the user's machine, nor used as communication
material for DT Hub.

DT Hub does not embed any logo or graphical resource belonging to a
third-party application publisher. The only two images in the
repository, `assets/app.png` and `assets/app.ico`, are drawn by
`build/make-icon.py`.

Two exceptions, named here because a rule that does not state its
exceptions no longer protects anything: the quest titles from the file
described above, and the screenshots, which show the application
running and therefore the game it displays. The former serve
interoperability, the latter illustrate the documentation.

The screenshots are named, since the principle is to name things:

| File | What it shows |
|---|---|
| `assets/screenshots/deux-comptes.jpg` | two game windows side by side |
| `assets/screenshots/cadre-a-onglets.jpg` | the same two, tabbed |
| `assets/screenshots/guide-de-quete.jpg` | the guides window, and therefore a papycha.fr page |
| `assets/screenshots/appareils.png` | the device configurator alone, no third-party resource |
| `assets/screenshots/liste-des-donjons.png` | the list DT Hub composes itself, no third-party resource |
| `assets/screenshots/almanax.png` | the Almanax window, which draws itself from the day's facts |

None serves as the project's identity: no icon, no favicon, no preview
image. Those are drawn by `build/make-icon.py`. And all of them are
removable without the documentation ceasing to read properly.

## What the DOFUS Touch team said in response

The project was presented to them before any publication, with a
demonstration video, on 2026-09-07. The reply came the next day,
2026-09-08.

**What it says.** That as it stands, the team sees no difficulty, on
two conditions: that the game not be altered, and that no automation
solution be offered. These are exactly the two limits that
`CONTRIBUTING.md` already sets and that the roadmap declares out of
scope for good.

**What it does not say, and must not be made to say.** This is neither
a validation, nor a partnership, nor an endorsement. The team made
clear it would give the project no promotion, having no say over it.
DT Hub is not affiliated with Ankama, and the README's legal notice
says so from its very first line.

**"As it stands" is the phrase that matters.** The absence of
objection concerns the application as it was shown. Any feature that
altered the game or automated an action would fall outside it. That is
why these two limits are not priorities to be weighed, but boundaries
of the project.

The person who replied is not named: they did so on behalf of their
team, and their handle has no place in a public repository.

## Adding a dependency

Before adding a third-party component:

1. check that its licence allows the intended use, including
   redistribution;
2. if redistribution is not allowed, prefer downloading from the
   official source, with verification;
3. add an entry in this file, with the link, the licence and the
   distribution mode;
4. keep the licence and notice files provided upstream.
