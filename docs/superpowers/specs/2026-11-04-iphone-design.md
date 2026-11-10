# iPhone on the PC

Asked on 2026-11-04: DT Hub on Windows shows and drives an iPhone, made as
ready as possible without an iOS 27 device to try it on. The game keeps
running on the iPhone, as Ankama requires. Decision: D227.

The owner decided to ship it without a prior trial: the first player with
an iOS 27 iPhone tries it, with the list below. **The picture and the
touches have never been seen.** Everything up to the stream has run on the
PC against an iPhone XR on iOS 18.7.9.

Python at runtime breaks a rule of AGENTS.md; the owner made it a written,
bounded exception for this window (see AGENTS.md and D227).

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
  iPhone window is first opened. Once, and again whenever the embedded list
  changes (a hash stamp in the venv):
  - `uv venv --python 3.13.18`, with uv's Python only;
  - `uv pip sync --require-hashes build/iphone-requirements.txt`, which
    holds pymobiledevice3 11.49.5 and its 89 packages with their hashes;
  - everything goes under `tools/iphone`.

  Then every call is `<venv>\Scripts\python.exe -m pymobiledevice3 ...`
  (`PyMobileDevice.ToolRequest`). To update pymobiledevice3, regenerate the
  list with the command at its top.
- `PyMobileDevice` (Infrastructure):
  - `ListAsync` runs `usbmux list --usb` (a Wi-Fi-synced iPhone would show
    twice);
  - `StartMirrorAsync` runs `mounter auto-mount` (the display service comes
    with the developer disk image), then starts `serve-web`;
  - `Explain` turns the tool's last error line into a message for the
    player;
  - one setup at a time across every window, and one mount at a time (they
    share the downloaded disk image), with a timeout per step: 10 minutes
    for the setup, 5 for the mount, 1 for the list. The list waits for the
    setup only: a mount without Internet used to hold it for 5 minutes.
- `ProcessRequest.KillEntireTree`: killing uv leaves its Python running
  (measured), and the venv's `python.exe` is a launcher too. So every
  iPhone call kills the whole tree, on close and on timeout. The flag is
  off for everything else: scrcpy may have started the shared adb server.
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

Release build with `-warnaserror`, 0 warnings; 3040/3040 tests; `dotnet
format --verify-no-changes` clean. Then DT Hub's own `PyMobileDevice`, from
a scratch program, against the XR:
- from an empty folder: uv downloaded and hash-checked, then Python 3.13.18
  and the 90 hash-checked packages, about 300 MB, in 24 seconds with the
  scratch program's build; 4 seconds on the next run, setup skipped;
- the XR listed as "iOS 18.7.9, CanMirror=False";
- the disk image mounted;
- `serve-web` started and stopped with the French "this iPhone does not
  have iOS 27" message; no `python.exe` left running.

Edge 156 on this PC (NVIDIA) reports HEVC decoding as supported in hardware.

Not run: the WPF windows themselves. They were built and analysed, but
never opened.

## First real iPhone, 2026-11-09

A player on iOS 27.2 (HP laptop, Ryzen 5 7520U, DT Hub 0.14.1) got the
screen, and a click opened DOFUS Touch. On the way:
- Avira (most likely) removed `python.exe` from `tools\iphone\python` and
  `msedgewebview2.exe` from WebView2: setup then failed with "Python
  interpreter not found" on every try, and the viewer window with
  0x80070002. Deleting `tools\iphone` and repairing WebView2 fixed both.
- Edge needed the paid HEVC Video Extensions; the free OEM one is refused.
- Only the top of the screen showed. Edge's WebCodecs, given no size in
  the decoder config (the viewer gives none), crops every frame to
  1280x720. Reproduced on this PC with a 1264x2752 x265 frame; the window
  script now re-wraps each frame at its coded size (same pixels as a
  sized config, portrait and landscape).

Then (0.14.4, 2026-11-10, checked in Edge with the viewer's own CSS, not on
an iPhone): the canvas fills the window and the window takes the picture's
shape; a small button over the screen clicks the viewer's hidden Rotate;
right click (the viewer's Home) and "?" (its hidden help, which swallowed
every key) are caught first. iPhones are listed under the phones once the
tools are installed (`IPhoneScreens`, at most every 10 s), and the grid puts
their windows in the cells after the phones'. No tabs, no F1-F8 yet.
Known, not fixed: no sound on Windows (AAC-ELD decoding is macOS only in
pymobiledevice3), an unplugged iPhone leaves a frozen picture (reconnection
waits for tunneld), AltGr characters do not reach the iPhone.

## Audit, 2026-11-10

Checked against pymobiledevice3 11.49.5's own source, the viewer run in
Edge 156 with DT Hub's window script (a mock server logging what the page
posts), and unit tests. Nothing here was seen on an iPhone.

- A device below version 27 (an iPad on iPadOS 26, say) had a greyed Open
  button and "iOS 27 needed" at the end of its line. Its row, in the
  device list and in the iPhone window, now says the version found, the
  one needed (iOS for an iPhone, iPadOS for an iPad, from usbmux's
  `DeviceClass`), where to update, and that without the update offered
  the device cannot be shown.
- The page's watch on refused touches never fired: serve-web queues
  `/touch`, `/key` and `/button` and answers 200 at once. A refusal shows
  only in the tool's log ("queued /touch -> ..." or "queued HID dispatch
  failed"), which DT Hub's log keeps ("Outil iPhone", first 300 lines).
  The watch is gone.
- Unplugged: the tool's reconnection polls tunneld, which DT Hub does not
  run, so the picture froze. The window now stops the tool when the iPhone
  leaves a successful USB list (or the tool says it waits for tunneld),
  says so, and opens again in the same place once the iPhone is listed.
- AltGr is Ctrl+Alt on Windows: the viewer sent Ctrl+Option and the
  physical key. An AltGr character now goes to the iPhone's clipboard,
  then Cmd+V, whatever the iPhone's keyboard layout. It replaces what the
  iPhone had copied.
- The viewer's Ctrl+H, L, S, [, ], \ (Home, Lock, Siri, volume) and
  Ctrl+P, =, -, 0 (screenshot, zoom) are swallowed; WebView2's own keys
  (F5, Ctrl+R, Ctrl+F, Ctrl+wheel) are off.
- Messages in the player's language for a camera or microphone in use,
  a mount without Internet (the tool ends on a requests traceback naming
  `HTTPSConnectionPool(`), and any step that timed out.
- The viewer releases fingers and keys on the page's blur; the window
  also sends one when it is deactivated.
- The viewer's English "Stream offline" banner says, translated, to
  unlock the iPhone and how to turn Auto-Lock off.

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
- iPad: whether it takes touches at all is unknown. serve-web sends every
  touch to the main touchscreen surface, as for an iPhone. If an iPad on
  iPadOS 27 shows its screen but ignores clicks, the tool's log lines
  ("Outil iPhone : ... queued /touch") are the first thing to read.
- Auto-Lock: the tool holds `PreventUserIdleSystemSleep`, which keeps the
  system awake but probably not the screen. A locked iPhone stops the
  picture; the viewer restarts the stream every 30 seconds, so unlocking
  brings it back. Setting Auto-Lock to Never avoids it; the banner says so.
- The unplug check relies on `usbmux list --usb`, run at most every 10
  seconds by the device list: up to that long before the window reacts.
  A device missing from one successful list for another reason would
  restart its stream once.
- AltGr through the clipboard: unchecked whether DOFUS Touch's chat field
  pastes on Cmd+V; the character is lost if it does not.
- A setup, list or mount still running when DT Hub quits is not in its
  job object (one-shot calls never are, as adb may be among them): it runs
  on until it ends by itself, its timeout being DT Hub's.
