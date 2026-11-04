# iPhone on the PC, trial version

Asked on 2026-11-04: DT Hub on Windows shows and drives an iPhone, made as
ready as possible without an iOS 27 device to try it on. The game keeps
running on the iPhone, as Ankama requires. Decision: D227.

This document is the handoff for whoever tests it with a real iOS 27
iPhone. **The picture and the touches have never been seen.** Everything
up to the stream has run on the PC against an iPhone XR on iOS 18.7.9.

## What Apple allows

- iOS 27 and later only: Xcode 27's Device Hub opened two services,
  `com.apple.coredevice.displayservice` (video) and
  `com.apple.coredevice.hid.universalhidservice` (touch). Older iOS
  refuses the stream. The XR cannot go past iOS 18.
- Developer Mode on (Settings > Privacy & Security > Developer Mode).
- One account per iPhone: no cloned app, no virtual display on iOS.
- On Windows, Apple's USB service comes with the Apple Devices app
  (Microsoft Store), listening on 127.0.0.1:27015.

## How it is built

- pymobiledevice3 11.49.5 already does the whole job: `developer core-device
  display serve-web --http-port N` serves the screen as a web page (WebCodecs,
  HEVC) that sends clicks, drags and keys back as touches. Its tunnel runs in
  user space, so no administrator rights are needed.
- **uv** brings Python and pymobiledevice3, so the player installs nothing.
  It is a third entry in `build/dependencies.json`, fetched only when the
  iPhone window is first opened. Every call is `uv tool run --python 3.13
  --from pymobiledevice3==11.49.5 pymobiledevice3 ...`, with uv's Python only
  and all its folders under `tools/iphone` (`PyMobileDevice.Request`).
- `PyMobileDevice` (Infrastructure):
  - `ListAsync` runs `usbmux list`;
  - `StartMirrorAsync` runs `mounter auto-mount` (the display service comes
    with the developer disk image), then starts `serve-web`;
  - `Explain` turns the tool's error text into a message for the player.
- `ProcessRequest.KillEntireTree`: killing uv leaves its Python running
  (measured), so the iPhone session kills the whole tree. The flag is off
  for everything else: scrcpy may have started the shared adb server.
- `IPhoneWindow`: the list. It opens from a link in the add-device window
  ("J'ai un iPhone ou un iPad").
- `IPhoneMirrorWindow`: one per iPhone. It polls the page until it answers,
  shows it in the shared WebView2 environment, and hides pymobiledevice3's
  bars and panels with a script run before the page's own. It also reports
  a PC that cannot decode HEVC. Closing the window kills the tool.

## Approaches considered

- **pymobiledevice3's web viewer in WebView2 (chosen).** Video decoding,
  touch mapping and keys are already written and maintained upstream. DT
  Hub only starts a process and shows a page, as it does for scrcpy and
  the guides.
- Its VNC server (`serve-vnc`) with a VNC client: on Windows it needs PyAV
  decoding in Python and a third program.
- Talking to the two services from C#: a rewrite of the tunnel, RTP/HEVC
  and HID code, for the same result.
- A Bluetooth HID dongle with AirPlay (works before iOS 27): a relative
  pointer, extra latency and hardware to buy. Left aside.

## Checked on the PC on 2026-11-04

Release build with `-warnaserror`, 0 warnings; 3034/3034 tests; `dotnet
format --verify-no-changes` clean. Then DT Hub's own `PyMobileDevice`, from
a scratch program, against the XR:
- uv downloaded and hash-checked, then Python 3.13.18 and 90 packages,
  about 290 MB, in 15 seconds;
- the XR listed as "iOS 18.7.9, CanMirror=False";
- the disk image mounted;
- `serve-web` started and stopped with the French "this iPhone does not
  have iOS 27" message.

Edge 156 on this PC (NVIDIA) reports HEVC decoding as supported in hardware.

Not run: the WPF windows themselves. They were built and analysed, but
never opened.

## What to check with an iOS 27 iPhone, in this order

1. Add-device window > "J'ai un iPhone ou un iPad": the iPhone is listed
   with its iOS version, the Open button enabled.
2. Open: the first time, the disk image mounts (needs Internet), then the
   window shows the screen alone, without pymobiledevice3's bars.
3. A click lands where it should: the canvas keeps the viewer's own
   sizing, only the surroundings are hidden. Try the corners.
4. A drag scrolls, keys type in the game's chat.
5. Sound: the viewer turns it on by default; check it plays, or mute.
6. Unplugging the iPhone: the window says why it stopped. Closing it
   leaves no `python.exe` from `tools\iphone` running.
7. Two iPhones at once: each its own window and port.

## Known limits, for later

- The viewer keeps 88 points of width and 150 of height around the
  picture (its own layout). Adjusting it means patching its sizing
  function, which would also move the touch mapping: only once item 3 is
  verified.
- No orientation handling: the viewer rotates with the iPhone.
- The Mac (`feat/macos`) could reuse `PyMobileDevice` with uv from
  Homebrew; nothing is wired there.
