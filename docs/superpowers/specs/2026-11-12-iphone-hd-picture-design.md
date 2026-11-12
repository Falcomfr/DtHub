# iPhone and iPad "HD picture": the screen over QuickTime's USB capture

Asked on 2026-11-12. Players with an iPhone or iPad find the picture in DT
Hub ugly ("moche et étiré"). Measured the same day on a bench (the tool's
real viewer in Edge, x265 streams): DT Hub's display adds almost nothing to
the loss. It comes from the device. Through CoreDevice's DisplayService, the
path used since D227, iOS caps its HEVC encoder near 6 Mbit/s and drops the
capture resolution while the screen moves. Xcode's own Device Hub gets the
same stream, and no setting raises it: `serve-web --max-bitrate` never
reached the device and was removed upstream (pymobiledevice3 PR #2082).

QuickTime's USB screen capture (the "Valeria" configuration that QuickTime
and AVFoundation use on a Mac) goes through another encoder. It sends H.264
at the screen's native resolution. Two Windows projects report it:
ZenLink sees 12 to 30 Mbit/s against "about 4-6 Mbps (device limit)" over
DisplayService, and iPhoneMirror measured 1206x2622 at 58.6 fps.

The owner decided to build it directly, without first having a player
compare the two paths. That is the main risk of this design: no iPhone or
iPad is on hand, so nothing on the device side can be checked before a
player tries a test build.

## Decision taken with the owner

On Windows, getting at the capture configuration needs the libusb-win32
filter driver on the device, which takes administrator rights once. AGENTS.md
forbids "requesting elevation", and `docs/CONFIANCE.md` promises never to
ask for administrator rights. The owner chose a **bounded exception**:

- opt-in only: an "Image HD" setting the player turns on;
- DT Hub explains what it installs before Windows asks;
- one elevation, to install the filter, on Apple devices only, plus one to
  remove it if the player turns the setting off;
- the current path stays the default and the fallback.

The owner also asked whether clicks survive. They must: the filter sits
**on top of** Apple's driver and does not replace it. Apple Mobile Device
Service keeps the device, so the CoreDevice tunnel keeps carrying touches,
keys, buttons, brightness and the game commands (`IosGameControl`). The capture
configuration keeps the usbmux interface beside the two AV endpoints, so
both live on one cable at once. iPhoneMirror does exactly this (capture plus
wired control). ZenLink replaces Apple's driver instead and loses control in
that mode: that is the variant to avoid.

## Goals

1. An "Image HD (iPhone et iPad)" setting, off by default, in the iPhone
   part of the settings. Turning it on:
   - shows a dialog saying what is installed (the libusb-win32 filter
     driver, on Apple devices only), why, and that Windows will ask for
     permission;
   - then runs the filter installer elevated, once.

   Refused or failed, the setting stays off and says why.
2. With the setting on, opening an iOS window takes its picture from
   QuickTime's capture: H.264 at the device's native resolution, shown in
   the same viewer page as today. Clicks, keys, buttons, brightness, the
   game commands, sound, tabs, grid, full screen and titles work as they do
   today. The picture may arrive in another orientation (see Orientation):
   it is shown upright, and clicks land where they are made.
3. If the capture cannot start (filter missing, configuration refused, no
   frame within a few seconds), the window falls back to today's
   DisplayService stream by itself and logs why. The player still gets a
   picture, as today.
4. Closing the window stops the capture and puts the device back in its
   usbmux-only configuration. A player who never turned the setting on sees
   no change at all.
5. Turning the setting off offers to remove the filter (one more elevation).
   Kept or removed, DT Hub goes back to today's path.

## Non-goals

- macOS and Linux. On macOS, AVFoundation offers this capture without any
  driver. On Linux, it needs a udev rule installed once by root. Both come
  later, separately.
- Wi-Fi. QuickTime's capture is USB only, as the iOS window already is.
- Changing anything on the Android side.
- Raising DisplayService's bitrate, or asking it for a smaller picture.
  That is a separate lead, untried.

## Anti-goals (a failure even if the picture works)

- Clicks, keys or the game commands stop working with the setting on.
- A player's iPhone stops being seen by Apple Devices/iTunes or by DT Hub
  with the setting off. The filter must never replace Apple's driver: never
  Zadig-style.
- Any elevation the player did not explicitly ask for, or any driver
  installed on a non-Apple device.
- A window stuck black. Every failure falls back to today's path.

## Approaches considered

1. **Swap the video source inside the existing Python bootstrap (chosen).**
   `PyMobileDevice.MirrorBootstrap` already patches pymobiledevice3's
   `ScreenStreamServer` (sound decoder, brightness buttons, landscape
   touches, game-command server). It also gets a QuickTime source. When HD
   is on, it feeds the server's subscribers with QuickTime's H.264 access
   units instead of DisplayService's HEVC ones, and `/codec` answers
   `avc1.*` plus an avcC record. The viewer page, the touch path, the HID
   worker, the control server and every C# window stay as they are.
   WebCodecs decodes H.264 in hardware without the paid HEVC extension, a
   bonus. Cost: one new Python package (pyusb) and the protocol code.
2. **A C# implementation in DT Hub** (LibUsbDotNet, own decoder or own
   page): no new Python, but it duplicates the viewer, the stream framing
   and the touch mapping already working through serve-web. It is about
   twice the work for the same picture.
3. **Run another project's binary** (iPhoneMirror, ZenLink, qvh): ruled out.
   The first two are GPLv3, which DT Hub's MIT license cannot take in.
   Running a third-party binary from an unofficial source is forbidden by
   AGENTS.md. qvh's author gave up on Windows.

## Design

### Driver (Windows)

- **Source:** the official libusb-win32 release (GitHub `mcuee/libusb-win32`).
  It goes in `build/dependencies.json` with its hash, provisioned like uv
  through `IDependencyProvisioner`, and fetched only when the player turns
  the setting on.
- **Signature:** the driver files must be signed for Windows 10/11. The
  builder checks this on Windows 11, Secure Boot on, before anything else.
  If the official release does not load there, stop and report.
- **Install:** `install-filter.exe install --device=<hardware id>` runs
  elevated (`ShellExecute` verb `runas`), once per Apple device seen. The
  hardware id is the composite parent `USB\VID_05AC&PID_xxxx` of each iPhone
  and iPad listed by `usbmux list --usb`. The PID differs by model.
- **Checks before streaming:**
  - the parent still uses `usbccgp`;
  - `libusb0` is in its UpperFilters.

  If either fails, there is no HD for this window, and DT Hub falls back
  with a clear log line.
- **A device plugged in later**, never filtered: the setting shows
  "1 appareil à activer". Activating it runs one more elevated install.
  DT Hub never elevates on its own.
- **Removal:** `install-filter.exe uninstall --device=...`, elevated, for
  each device DT Hub installed. The list is kept in the settings.

### Capture (Python, in the bootstrap's process)

- **pyusb** goes in `build/iphone-requirements.txt`, pinned by hash. It uses
  the libusb0 backend, pointed explicitly at the provisioned `libusb0.dll`,
  never one found on the PATH.
- **Finding the device:** its USB serial number is the UDID without dashes
  (to confirm with the first real device).
- **Protocol:** written in DT Hub's own module, after quicktime_video_hack's
  technical documentation and ioscreen's Python code (both MIT, credited in
  THIRD-PARTY-NOTICES.md):
  - the vendor request `0x40, 0x52, 0x00, 0x02` enables the hidden
    configuration;
  - wait for the re-enumeration, then select the configuration whose
    interface is class 0xFF, subclass 0x2A;
  - claim only that interface's two bulk endpoints;
  - run PING, then SYNC CWPA/AFMT/CVRP/CLOK/TIME/SKEW/OG and
    ASYN HPD1/HPA1/NEED/FEED/EAT!;
  - stop with HPA0/HPD0/STOP/RELS, then restore the usbmux-only
    configuration.

  Clocks are monotonic and in nanoseconds, as documented. A ready-made
  `ioscreen` package is not used: its Windows branch needs a separate
  usbmuxd and stopping Apple's service.
- **Into the server:**
  - SPS/PPS come from CVRP's format description and give the avcC record
    and the `avc1.PPCCLL` codec string;
  - each FEED sample buffer gives one access unit of 4-byte
    length-prefixed NALUs, sent with serve-web's own framing (type 0 for
    an IDR, 1 otherwise).
- **Clicks need DisplayService's stream (checked in the tool's code).**
  backboardd drops touch reports unless a media stream is active
  (`hid_service.touch_session`: "Without the stream the same reports get
  silently dropped"). serve-web relies on its own video stream for that.
  In HD, DisplayService's video stream **keeps running**, and its access
  units are dropped instead of reaching the subscribers. That is what
  `touch_session` itself does. *Designer's call, owner to check:* this costs a second encoder on the device (heat, battery).
  Whether iOS runs both at once is unknown and is the first thing the test
  build checks. If DisplayService's stream fails beside the capture, the
  window falls back to today's path, clicks first.
- **Order of start.** Enabling the capture configuration makes the device
  leave and come back on USB, which drops every usbmux connection. So the
  bootstrap switches the configuration **before** it starts serve-web's
  tunnel, and waits for the device to come back. `IPhoneScreens`'s 10 s
  `usbmux list` poll and `IPhoneMirrorWindow.WatchAsync` must not take that
  gap, up to about 10 s while the window starts, for an unplug.
- **Stopping.** On a normal close, DT Hub first asks the bootstrap, on the
  control port, to stop the capture cleanly (HPD0, STOP, RELS, usbmux-only
  configuration), with a 2 s limit, then kills the tool as today. After a
  crash or a forced end, the device stays in the capture configuration
  until it is unplugged, and the next HD start resets it. usbmux keeps
  working in that configuration, so DT Hub and Apple Devices still see the
  device.
- **Late subscriber:** the viewer only starts on a key frame. Whether
  QuickTime sends IDRs periodically is unknown, and it has nothing like
  DisplayService's PLI. So the server keeps the last IDR and every access
  unit since, and replays them to a new subscriber (a page reload, or a
  window reopened while the process lives).
- **Sound stays as today.** It keeps coming from DisplayService's audio
  stream, which runs anyway (see Clicks). `--no-audio` and the phone's own
  speaker behave exactly as they do now. The capture asks for video only
  (HPD1, no HPA1). If the device refuses a video-only capture, the
  bootstrap also starts QuickTime's audio (LPCM 48 kHz, EAT!) and drops it.
  That may silence the phone's speaker while the window is open: logged,
  and reported back by the tester.
- **Orientation.** QuickTime's capture probably follows the interface
  orientation: an iPhone in DOFUS would send landscape frames where
  DisplayService sends a portrait picture with the game sideways. It also
  sends a new format description when the device turns, which means a new
  decoder config for the viewer. The requirement is not "same as today" but
  "the picture is upright and a click lands where it was made":
  - a landscape frame on a portrait-native panel takes the existing
    landscape touch path (the page's `landscape` flag, `to_panel` with
    SpringBoard's orientation);
  - the ↻ button does nothing harmful in HD.

  The builder checks this on the bench with landscape and portrait H.264
  streams.
- **Fallback:** any failure before the first frame, including the capture
  configuration not coming back with usbmux,, or no frame within
  5 s, logs the reason ("DT Hub: HD picture unavailable: ...", copied into
  DT Hub's log as "Outil iPhone : ..."). It then runs today's DisplayService
  path in the same process.
- **Mid-stream failure** (unplug, USB error): it ends like today's stream
  ending. `IosWindowLife` already reopens the window, and the next attempt
  tries HD again.

### Viewer page (`IPhoneMirrorWindow.ScreenOnly`)

- `pictureSize` (branch `fix/iphone-marge-encodeur`, which this work builds
  on) reads an **HEVC** SPS. It must run only for `hev1`/`hvc1` codecs.
- For `avc1`, Edge needs the picture size in the config too, or it crops to
  1280x720, measured for HEVC. It comes from the H.264 SPS in the avcC
  (frame cropping applied), or from a size the server adds to `/codec`.
  *Builder's call.* The 64-pixel guard stays.
- The `no-hevc` warning must not show when the stream is H.264.

### C# side

- Setting in `AppSettings`:
  - `IosHdPicture` (bool);
  - the list of hardware ids DT Hub filtered.
- The settings UI shows the dialog, the device count to activate, and the
  removal offer.
- `PyMobileDevice.MirrorRequest` gets an `hd` flag, passed as an environment
  variable with the `libusb0.dll` path.
- Driver install and removal are behind an interface (`IUsbFilterDriver`).
  Elevation, hardware-id discovery and filter checks are testable without
  a device.

### Documents

- **AGENTS.md:** a second bounded exception next to D227's. Elevation is
  allowed only to install or remove the libusb-win32 filter on Apple
  devices, only after the player's explicit choice. The binaries come from
  `build/dependencies.json`.
- **docs/CONFIANCE.md:** the "never administrator rights" promise is
  rewritten to say exactly this. It must stay true for every player who
  leaves the setting off.
- **docs/DECISIONS.md:** D234.
- **THIRD-PARTY-NOTICES.md:**
  - libusb-win32: library LGPL, driver GPL, downloaded, not bundled;
  - pyusb (BSD);
  - quicktime_video_hack and ioscreen (MIT) as protocol references.
- **Player help:** a line in the iPhone help, and in the release notes for
  the version.

## Verification

- **Unit tests (C#):**
  - settings;
  - building the install and uninstall commands;
  - parsing the filter check;
  - the `hd` flag in `MirrorRequest`.
- **Protocol:** checked offline against quicktime_video_hack's recorded
  message fixtures (MIT), in a dev script run with the tool's venv Python,
  as earlier bootstrap checks were (`C:\Dev\dthub-uia\rot`).
- **Bench (`C:\Dev\iphone-essai\nettete`):** the viewer fed an H.264 stream
  through the same framing, to check the decode, the picture size and the
  wrapper changes in Edge.
- **Driver:** install, check and uninstall the filter on this PC with an
  Android phone unplugged and no Apple device. The command must refuse or
  do nothing, and Apple Mobile Device Service must keep running.
- **Device:** a test build for a player with an iPhone (Djohnny8 tested
  before). They check:
  - the HD picture against the current one;
  - clicks;
  - the game opening;
  - sound;
  - unplug and replug;
  - turning the setting off and removing the filter.

  Nothing ships before that.

## Left to the builder

- Where the protocol module lives (an embedded resource run by the bootstrap
  is the usual pattern) and how it is split.
- The exact libusb-win32 version, provided its driver loads on a current
  Windows 11.
- The H.264 picture-size source for Edge (SPS or `/codec`).
- Keeping QuickTime's sound or DisplayService's (see Sound).
- Wording of the dialog and setting, in French, English and Spanish like the
  rest of the UI.
