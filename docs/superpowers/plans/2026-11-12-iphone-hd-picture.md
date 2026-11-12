# iPhone and iPad HD Picture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** an opt-in "Image HD" setting that takes the iOS window's picture
from QuickTime's USB capture (native-resolution H.264), keeping clicks, sound
and game commands on today's CoreDevice path, and falling back to it on any
failure.

**Architecture:** the Python bootstrap that already patches pymobiledevice3's
`ScreenStreamServer` loads a new DT Hub module, `dthub_quicktime.py`, when
the window is opened in HD. The module:
- switches the device to the capture configuration before serve-web's
  tunnel opens;
- runs the QuickTime protocol over pyusb/libusb0;
- feeds the server's subscribers with H.264 access units, while
  DisplayService's own stream keeps running (it holds the touch gate) with
  its frames dropped.

On the C# side, the setting, the libusb-win32 filter driver (installed
elevated, Apple devices only), the wiring and the page wrapper change.

**Tech Stack:** .NET 10 / WPF, xUnit (French test names), Python 3.13 in the
iPhone venv, pyusb 1.3.1 (libusb0 backend), libusb-win32 1.4.0.2.

**Spec:** `docs/superpowers/specs/2026-11-12-iphone-hd-picture-design.md`.
Read it first. This plan argues from it.

## Global Constraints

- Windows only. macOS, Linux and the Android side are unchanged.
- The setting is off by default. A player who never turns it on sees no
  change, and DT Hub never asks them for elevation.
- Elevation only runs `install-filter.exe` (`install` or `uninstall`), only
  after the player's click, and only for hardware ids matching
  `^USB\\VID_05AC&PID_[0-9A-F]{4}$`.
- The filter is never installed as the device's driver (no Zadig, no
  WinUSB). The parent must stay on `usbccgp`.
- libusb-win32: release `release_1.4.0.2`, `libusb-win32-bin-1.4.0.2.zip`,
  2636261 bytes, sha256
  `00004c92cdb99be36e17fb2377165eb97e63b48ba895bfc04a642ea9c3e26d94`.
  Its `bin/amd64/install-filter.exe`, `libusb0.dll` and `libusb0.sys` are
  signed by DonTech ApS. `bin/libusb0.cat` is signed by "Microsoft Windows
  Hardware Compatibility Publisher" (checked 2026-11-12).
- pyusb `1.3.1`. Wheel sha256
  `bf9b754557af4717fe80c2b07cc2b923a9151f5c08d17bdb5345dac09d6a0430`,
  sdist sha256
  `3af070b607467c1c164f49d5b0caabe8ac78dbed9298d703a8dbf9df4052d17e`.
- Its libusb0 backend is loaded from the provisioned `libusb0.dll`, never
  found on the PATH.
- The QuickTime enable request is `bmRequestType 0x40, bRequest 0x52,
  wValue 0x00, wIndex 0x02`. The AV interface has class `0xFF` and
  subclass `0x2A`.
- In HD, DisplayService's video stream keeps running. Its AUs are dropped,
  never shown. Sound stays DisplayService's: QuickTime is asked for video
  only (HPD1, no HPA1).
- The fallback triggers on no first QuickTime frame within **5 s**. A window
  starting in HD tolerates the device's absence from `usbmux list` for
  **30 s** (two re-enumerations of up to 10 s each when a killed tool left
  the capture shown). A clean capture stop on close waits **2 s** at most.
- Repository rules:
  - English in code, comments and documents;
  - test method names in French;
  - `when` filter on every `catch (Exception)`;
  - UI strings in `Strings.resx`, `Strings.fr.resx` and `Strings.es.resx`;
  - build and test through `rtk proxy dotnet ...` with a log file
    (CLAUDE.md);
  - stage by explicit path, and run `git status -sb` before each commit:
    the repository is shared between sessions.

## Review Focus

1. **A device whose USB serial does not match its UDID**, or a second Apple
   device plugged in: the capture must claim the device whose serial equals
   the UDID without dashes, case-insensitive, or none. Never "the first
   Apple device". Test in Task 5.
2. **The player refuses the Windows prompt, or there is no Apple device
   plugged in** when they tick the box: the box goes back unticked with a
   message, nothing is saved, and no second prompt follows. Test in
   Task 3 (`Win32Exception` 1223 means refused).
3. **A hardware id that is not Apple's** reaching `InstallAsync` (a bad
   registry read, a hand-edited settings file): refused before any process
   starts. Test in Task 3.
4. **The capture configuration never comes back with usbmux** (unplugged
   mid-switch, configuration refused): the fallback starts today's path
   instead of a black window. Test in Task 5 (fallback decision).
5. **Rotation in HD:** a new format description mid-stream (the device
   turned) must reach the viewer as a new decoder config, not as frames
   the old decoder chokes on. Test in Task 4 (framing of a config change),
   checked on the bench in Task 7.

---

### Task 1: Dependencies (libusb-win32 and pyusb)

**Files:**
- Modify: `build/dependencies.json` (new entry after `uv`)
- Modify: `src/DtHub.Infrastructure/Dependencies/DependencyManifest.cs` (key constant beside `UvKey`, line 48)
- Modify: `build/iphone-requirements.txt` (pyusb, alphabetical place, both hashes)
- Test: `tests/DtHub.Tests/Dependencies/DependencyManifestTests.cs`

**Interfaces:**
- Produces: `DependencyManifest.LibUsbWin32Key = "libusb-win32"`. The
  entry's `archiveRootDirectory` is `"libusb-win32-bin-1.4.0.2"` and its
  `executable` is `"bin/amd64/install-filter.exe"`. `libusb0.dll` sits next
  to it.

- [ ] **Step 1: Failing test** `Le_filtre_usb_vient_de_la_version_officielle_de_libusb_win32`:
  - `DependencyManifest.Get(DependencyManifest.LibUsbWin32Key)` has
    `Url == "https://github.com/mcuee/libusb-win32/releases/download/release_1.4.0.2/libusb-win32-bin-1.4.0.2.zip"`;
  - its `Sha256` is the constraint's value;
  - its `Executable` ends with `install-filter.exe`.
- [ ] **Step 2: Run** `rtk proxy dotnet test --filter Le_filtre_usb > $L/t.log 2>&1`, then grep. Expected: FAIL (key unknown).
- [ ] **Step 3: Add the JSON entry** (fields as `uv`'s):
  - `license`: `"LGPL-3.0 (bibliothèque), GPL-3.0 (pilote)"`;
  - `licenseUrl` pointing at `COPYING_LGPL.txt` on the repository;
  - `redistributionNote`: downloaded only when the player turns Image HD
    on, never bundled; the filter is installed by `install-filter.exe`,
    elevated, on Apple devices only.

  Then add the constant, and add pyusb 1.3.1 with both hashes to the
  requirements.
- [ ] **Step 4: Run** the full suite. Expected: PASS. `EmbeddedResourceTests` and the manifest's own checks stay green.
- [ ] **Step 5: Commit** `feat(iphone): libusb-win32 and pyusb as dependencies of the HD picture`.

### Task 2: Settings

**Files:**
- Modify: `src/DtHub.Core/Settings/AppSettingsDocument.cs` (after `SharpImage`, line 302)
- Modify: `src/DtHub.Core/Settings/SettingsService.cs` (beside `SetSharpImageAsync`, line 395)
- Test: `tests/DtHub.Tests/Settings/SettingsServiceTests.cs`

**Interfaces:**
- Produces:
  - `bool IosHdPicture { get; set; }` (default false);
  - `List<string> IosHdFilteredDevices { get; set; } = []`;
  - `Task SetIosHdPictureAsync(bool enabled, IReadOnlyList<string> filteredDevices, CancellationToken cancellationToken = default)`,
    which writes both fields in one update.

- [ ] **Step 1: Failing test** `L_image_HD_et_ses_appareils_filtres_sont_gardes`:
  - set `true` with `["USB\\VID_05AC&PID_12A8"]`, reload, assert both;
  - set `false` with `[]`, reload, assert both.

  Also `Un_fichier_sans_image_HD_la_laisse_eteinte`: a settings JSON
  without the fields gives `false` and an empty list.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** No schema bump: missing fields default, as for `SharpImage`.
- [ ] **Step 4: Run the full suite, expect PASS.**
- [ ] **Step 5: Commit** `feat(settings): the HD picture setting and the devices it filtered`.

### Task 3: The filter driver

**Files:**
- Create: `src/DtHub.Core/IPhone/IUsbFilterDriver.cs`
- Create: `src/DtHub.Infrastructure/IPhone/UsbFilterDriver.cs`
- Modify: the App's service registration, where `PyMobileDevice` is registered (`git grep -n "AddSingleton<PyMobileDevice>"`). Mac/Linux register nothing.
- Test: `tests/DtHub.Tests/IPhone/UsbFilterDriverTests.cs`

**Interfaces:**
- Produces `IUsbFilterDriver`:
  - `IReadOnlyList<string> PresentAppleDevices()`: the hardware ids
    `USB\VID_05AC&PID_xxxx` of the present Apple composite parents
    (service `usbccgp`);
  - `bool IsFiltered(string hardwareId)`: the parent is on `usbccgp` and
    its UpperFilters contain `libusb0`;
  - `Task<UsbFilterResult> InstallAsync(IReadOnlyList<string> hardwareIds, CancellationToken cancellationToken)`;
  - `Task<UsbFilterResult> UninstallAsync(IReadOnlyList<string> hardwareIds, CancellationToken cancellationToken)`;
  - `string? LibUsb0Path()`: the provisioned `libusb0.dll`, or null if it
    is not there.
- Produces `enum UsbFilterResult { Done, Refused, Failed }`.
- Produces static helpers on `UsbFilterDriver`, tested:
  - `bool IsAppleHardwareId(string id)`, the constraint's regex;
  - `string ParentHardwareId(string pnpDeviceId)`: from
    `USB\VID_05AC&PID_12A8\00008120001A...`, gives `USB\VID_05AC&PID_12A8`;
  - `string[] Arguments(bool install, string hardwareId)` gives
    `["install", "--device=" + id]` or `["uninstall", "--device=" + id]`.
- Consumes: `IDependencyProvisioner`, `DependencyManifest.LibUsbWin32Key` (Task 1).

- [ ] **Step 1: Failing tests:**
  - `Seul_un_identifiant_Apple_est_accepte`: `USB\VID_05AC&PID_12A8` is
    true; `USB\VID_046D&PID_C52B`, `USB\VID_05AC&PID_12A8\serial` and an
    empty string are false;
  - `L_identifiant_parent_perd_le_numero_de_serie`;
  - `Les_arguments_visent_un_seul_appareil`;
  - `Un_identifiant_etranger_est_refuse_sans_rien_lancer`: `InstallAsync`
    with `USB\VID_046D&PID_C52B` throws `ArgumentException`, and the fake
    elevated runner was never called.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** behind an internal seam `Func<string, string[], Task<int>> runElevated`. Its real implementation:
  - starts `ProcessStartInfo { FileName = installFilter, UseShellExecute = true, Verb = "runas" }`
    with the arguments joined and quoted, then waits for exit;
  - `Win32Exception` with `NativeErrorCode == 1223` gives `Refused`;
  - any other non-zero exit gives `Failed`.

  Install provisions libusb-win32 first. Presence and filters are read
  with `System.Management`: `Win32_PnPEntity` where `PNPDeviceID LIKE 'USB\\VID_05AC&PID_%'`
  and `Service = 'usbccgp'`, then `DEVPKEY_Device_UpperFilters` through
  `GetDeviceProperties`. *Builder's call* if a lighter API works the same.
- [ ] **Step 4: Add test** `Un_refus_de_Windows_n_est_pas_un_echec`: the seam throws `Win32Exception(1223)`, and the result is `Refused`.
- [ ] **Step 5: Run the full suite, expect PASS.**
- [ ] **Step 6: Commit** `feat(iphone): install the libusb-win32 filter on Apple devices, on request`.

### Task 4: QuickTime protocol and H.264 helpers (Python, offline)

**Files:**
- Create: `src/DtHub.Infrastructure/IPhone/dthub_quicktime.py`, embedded as `DtHub.Infrastructure.dthub_quicktime.py` (csproj, beside the requirements)
- Create: `tests/quicktime/fixtures/*`, copied from quicktime_video_hack `screencapture/packet/fixtures` (MIT; credit in Task 8). Copy:
  - the SYNC pairs: `cwpa-request1`, `cwpa-reply1`, `afmt-request`,
    `afmt-reply`, `cvrp-request`, `cvrp-reply`, `clok-request`,
    `clok-reply`, `time-request1`, `time-reply1`, `skew-request`,
    `skew-reply`, `og-request`, `og-reply`, `stop-request`, `stop-reply`;
  - `asyn-feed`, `asyn-hpd1`, `asyn-hpd0`, `asyn-need`, `asyn-rels`,
    `asyn-sprp`.
- Create: `tests/quicktime/test_dthub_quicktime.py` (unittest)

**Interfaces (module functions, pure):**
- `parse_message(data: bytes) -> Message`: a dataclass with `kind`
  (`"PING"`, `"SYNC"`, `"ASYN"`), `magic` (e.g. `"CWPA"`, `"FEED"`),
  `correlation_id: bytes`, `clock_ref: int`, `payload: bytes`.
- `reply(message: Message, local_clock_ref: int, now_ns: int) -> bytes`:
  the RPLY for CWPA, AFMT, CVRP, CLOK, TIME, SKEW, OG!, STOP.
- `hpd1(device_clock_ref: int) -> bytes`, `hpd0() -> bytes`,
  `need(device_clock_ref: int) -> bytes`.
- `format_description(cvrp: Message) -> VideoFormat`:
  `VideoFormat(sps: bytes, pps: bytes, width: int, height: int)`.
- `sample_nalus(feed: Message) -> bytes | None`: the AU as 4-byte
  length-prefixed NALUs, or None for an empty-media FEED.
- `avcc(sps, pps) -> bytes`, `codec_string(sps) -> str`, giving
  `"avc1.%02X%02X%02X"` from SPS bytes 1-3.
- `frame(kind: int, payload: bytes) -> bytes`: serve-web's framing,
  `(len+1).to_bytes(4,"big") + bytes([kind]) + payload`. Kinds: 0 key,
  1 delta, 3 new decoder config (JSON).
- `class KeyCache`: `add(au: bytes, key: bool)`, `replay() -> list[bytes]`.
  It holds the last IDR and every AU since, dropped at the next IDR.

- [ ] **Step 1: Failing tests:**
  - each request fixture parses to its magic;
  - `reply(...)` for CWPA, AFMT, CVRP, CLOK, TIME, SKEW, OG, STOP equals
    the reply fixture byte for byte. Use the clock refs and the time the
    fixtures carry: read them from the request/reply pair in the test, not
    hardcoded;
  - `hpd1`, `hpd0`, `need` equal `asyn-hpd1`, `asyn-hpd0`, `asyn-need`
    when given the fixtures' clock ref;
  - `format_description(cvrp-request)` gives an SPS starting with `0x67`
    and positive width and height;
  - `sample_nalus(asyn-feed)` is a non-empty run of length-prefixed NALUs
    whose lengths add up exactly;
  - `KeyCache`: after key A, delta B, key C, delta D, `replay()` is
    `[C, D]`;
  - `frame(3, b'{}')` is `b'\x00\x00\x00\x03\x03{}'`.
- [ ] **Step 2: Run** with the tool's Python: `python.exe -I -m unittest discover -s tests/quicktime -v`, from the worktree, Windows path. The interpreter is `%LOCALAPPDATA%\DtHub\tools\iphone\venv\Scripts\python.exe`. Expected: FAIL (module missing).
- [ ] **Step 3: Implement** after quicktime_video_hack's `doc/technical_documentation.md` and `screencapture/packet/*.go` (MIT). Port, don't import.
- [ ] **Step 4: Run, expect PASS.** Then run the .NET suite: `EmbeddedResourceTests` must see the new resource.
- [ ] **Step 5: Commit** `feat(iphone): QuickTime capture protocol, checked against recorded messages`.

### Task 5: Capture session and serve-web integration (Python)

**Files:**
- Modify: `src/DtHub.Infrastructure/IPhone/dthub_quicktime.py`
- Modify: `tests/quicktime/test_dthub_quicktime.py`

**Interfaces:**
- Consumes: Task 4's functions.
- Produces: `install(screen_stream_module, udid: str, libusb0_path: str) -> None`,
  called by the bootstrap before `runpy` (Task 6). It does five things:
  1. Before serve-web starts, it finds the device with pyusb's libusb0
     backend (`usb.backend.libusb0.get_backend(find_library=lambda _: libusb0_path)`),
     by `serial_number.replace("-", "").upper()`. That is Review Focus 1.
  2. It sends the enable request and waits up to 10 s for the device to
     come back with an interface of class 0xFF and subclass 0x2A. Then it
     selects that configuration and claims only that interface.
  3. It runs the protocol on a thread: PING, SYNC replies, HPD1 (no HPA1),
     NEED per FEED. Its AUs go into a `queue.Queue`.
  4. It patches the server:
     - DisplayService's AUs reach no subscriber;
     - `/codec` answers `{"codec": codec_string, "description": b64(avcc),
       "size": {"width": w, "height": h}}`;
     - new subscribers get `KeyCache.replay()` first;
     - a CVRP after the first one, when the device turns, broadcasts
       `frame(3, json)` and resets the cache;
     - the control server gets `POST /capture/stop`: HPD0, STOP, RELS,
       then the usbmux-only configuration (`set_configuration` to the
       configuration whose interfaces have subclass 0xFE only).
  5. On any exception before the first FEED, or no FEED within 5 s, it logs
     `"DT Hub: HD picture unavailable: <reason>"` through
     `screen_stream.logger.warning` and un-patches. DisplayService's AUs
     then flow as today.
- Produces the pure, tested helpers `pick_device(devices, udid)`,
  `capture_configuration(configs)` and `should_fall_back(started_at, first_frame_at, now) -> bool`.

- [ ] **Step 1: Failing tests:**
  - `pick_device` with fake objects: matches `00008120-001A2B3C4D5E` to
    serial `00008120001A2B3C4D5E`, case-insensitive; returns None when
    only another serial is present, even if it is an Apple device;
  - `capture_configuration`: from fakes (one config with a 0xFE
    interface, one with 0xFE + 0x2A) returns the second; with no 0x2A,
    None;
  - `should_fall_back(0, None, 4.9)` is False, `(0, None, 5.0)` is True,
    `(0, 1.0, 60)` is False.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** Hook points in pymobiledevice3 11.49.5 (`remote/core_device/screen_stream.py`):
  - the AU fan-out, where `_saw_first_key` is set, near line 1090;
  - the `/codec` handler, near line 2256;
  - `_subscribers` (dict of queues);
  - the stream handler that sends framed messages, near line 2660.

  Patch by wrapping methods, as the bootstrap already does. Never edit the
  installed package.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(iphone): the HD picture replaces DisplayService's frames, which keep the touch gate open`.

### Task 6: Starting the tool in HD (C#)

**Files:**
- Modify: `src/DtHub.Infrastructure/IPhone/PyMobileDevice.cs` (`MirrorBootstrap`, `StartMirrorAsync` line 286, `MirrorRequest` line 375)
- Modify: `src/DtHub.Infrastructure/IPhone/IosGameControl.cs`
- Test: `tests/DtHub.Tests/IPhone/PyMobileDeviceTests.cs`, `tests/DtHub.Tests/IPhone/IosGameControlTests.cs`

**Interfaces:**
- Changes:
  - `MirrorRequest(string python, string udid, int port, bool audio, int controlPort, string? libusb0Path)`;
  - `StartMirrorAsync(IPhoneDevice device, int port, bool audio, int controlPort, string? libusb0Path, CancellationToken cancellationToken)`.
- Non-null `libusb0Path` sets two environment variables:
  - `DTHUB_LIBUSB0` (the path);
  - `DTHUB_QUICKTIME` (the module file written to
    `<tools>/iphone/dthub_quicktime.py` from the embedded resource before
    each start, only when the content differs).
- The bootstrap gains, right before `runpy.run_module`:
  `if os.environ.get("DTHUB_QUICKTIME"): load it by path (importlib.util.spec_from_file_location) and call install(screen_stream, os.environ["PYMOBILEDEVICE3_UDID"], os.environ["DTHUB_LIBUSB0"])`, wrapped so that any exception only logs the fallback line.
- Produces `IosGameControl.StopCaptureAsync(CancellationToken)`: `POST capture/stop`, no body.

- [ ] **Step 1: Failing tests:**
  - `Sans_image_HD_l_outil_ne_charge_pas_la_capture`: a null path gives no
    `DTHUB_LIBUSB0` and no `DTHUB_QUICKTIME`;
  - `En_image_HD_l_outil_recoit_la_dll_et_le_module`: both set, with
    `DTHUB_QUICKTIME` ending in `dthub_quicktime.py`;
  - `Le_lanceur_charge_la_capture_seulement_si_demandee`: the bootstrap
    contains `DTHUB_QUICKTIME` and `install(screen_stream`;
  - in `IosGameControlTests`, `L_arret_de_la_capture_est_un_POST_sans_corps`,
    on the existing fake listener pattern.

  Update the existing callers and tests for the new parameter.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the full suite, expect PASS.**
- [ ] **Step 5: Commit** `feat(iphone): start the tool with the HD capture when asked`.

### Task 7: The viewer page and the window (C#, bench)

**Files:**
- Modify: `src/DtHub.App/Windows/IPhoneMirrorWindow.xaml.cs` (`ScreenOnly`, constructor line 340, `Closed` handler line 369)
- Modify: `src/DtHub.App/Services/IPhoneScreens.cs` (`ShowAsync` line 341, `Show` line 154)
- Create: `src/DtHub.Core/IPhone/IosHdGrace.cs`
- Test: `tests/DtHub.Tests/IPhone/IosHdGraceTests.cs`
- Bench (not committed): `C:\Dev\iphone-essai\nettete\server.py`. It gains
  `avc1` serving: SPS/PPS from an Annex-B `.h264`, `/codec` with `size`,
  and a mid-stream framing-3 config change.

**Interfaces:**
- `IPhoneMirrorWindow(IPhoneDevice device, int port, IProcessSession session, WebViewEnvironment engine, bool dark, IosGameControl? capture)`.
  A non-null `capture` means HD. On `Closed`, before `_session.DisposeAsync()`,
  it calls `capture.StopCaptureAsync` with a 2 s token and swallows only
  `IPhoneException` and `OperationCanceledException`.
- The `ScreenOnly` changes:
  - `pictureSize(description)` runs only when `config.codec` starts with
    `hev1` or `hvc1`;
  - for `avc1`, the size comes from `/codec`'s `size`, which the page's
    existing `fetch` override stores (`window.dthubSize`) when `url === '/codec'`;
  - the 64-pixel guard applies to both;
  - a new placeholder `__HD__` (`true`/`false`) skips the `no-hevc` check
    when true.
- `IosHdGrace.Tolerates(DateTime startedUtc, DateTime nowUtc)`: true for
  less than 30 s (two re-enumerations of up to 10 s each, plus margin).
- `IPhoneScreens.ShowAsync`, when `preferences.IosHdPicture` and
  `filter.IsFiltered(parent of the device)` and `filter.LibUsb0Path()` is
  not null:
  - passes the path to `StartMirrorAsync`;
  - passes the entry's `IosGameControl` as `capture`;
  - stamps the entry's `HdStartedUtc`.

  In `Show`, an absent device whose entry has
  `IosHdGrace.Tolerates(HdStartedUtc, now)` is not unplugged.
- Consumes: `IUsbFilterDriver` (Task 3), Task 6's signatures, and the
  parent id of a UDID through `PresentAppleDevices()`. One Apple parent
  per device; with several Apple devices present, HD needs all of them
  filtered (*builder's call* if a serial-to-parent map is easy through
  `Win32_PnPEntity`).

- [ ] **Step 1: Failing tests** `IosHdGraceTests`: 29.9 s is tolerated, 30 s is not, and a negative span is tolerated.
- [ ] **Step 2: Run, expect FAIL. Implement. Run the full suite, expect PASS.**
- [ ] **Step 3: Wire** `ShowAsync`, `Show`, the window constructor and `Closed`.
- [ ] **Step 4: Bench check**, Edge, with ffmpeg's libx264:
  - a 1206x2622 portrait `.h264` and a 2622x1206 landscape one, with
    `-x264-params keyint=600`;
  - expected: `c.width x c.height` equals the picture size for both;
  - a framing-3 switch from portrait to landscape mid-stream redraws in
    the new shape without a decode error in `statusEl`;
  - an `hvc1` stream (`ipad.hevc`) is still 2420x1668.
- [ ] **Step 5: Commit** `feat(iphone): the HD picture in the window, with its size and a clean stop`.

### Task 8: The setting in the interface, and the documents

**Files:**
- Modify: `src/DtHub.App/ViewModels/ConfiguratorViewModel.cs` (beside `SharpImage`, lines 1038 and 1660)
- Modify: `src/DtHub.App/Windows/ConfiguratorWindow.xaml` (beside line 634; WPF only)
- Modify: `src/DtHub.Core/Localization/Strings.resx`, `Strings.fr.resx`, `Strings.es.resx`
- Modify: `AGENTS.md` (the D227 exception paragraph, line 215, and the elevation bullet)
- Modify: `docs/CONFIANCE.md` (line 164's promise), `docs/DECISIONS.md` (new `## D234 - ...` at the end), `THIRD-PARTY-NOTICES.md`
- Test: `tests/DtHub.Tests/Localization/` (the existing check that every key exists in all three files)

**Interfaces:**
- Strings, French source text given:
  - `IosHdPicture` = "Image HD (iPhone et iPad)";
  - `IosHdPictureTip` = "Image nette, au lieu de l'image actuelle limitée par l'iPhone. Installe un petit pilote USB, une fois.";
  - `IosHdExplain` = "Pour l'image HD, DT Hub installe le pilote libusb-win32 sur ton iPhone ou iPad branché, et seulement sur lui. Windows va te demander l'autorisation. Tu pourras le retirer en décochant la case.";
  - `IosHdNoDevice` = "Branche ton iPhone ou ton iPad, puis coche à nouveau.";
  - `IosHdRefused` = "Image HD non activée : Windows n'a pas eu l'autorisation.";
  - `IosHdFailed` = "Image HD non activée : le pilote ne s'est pas installé.";
  - `IosHdRemoveAsk` = "Retirer aussi le pilote USB installé pour l'image HD ? Windows te demandera l'autorisation.";
  - `IosHdNewDevices` = "{0} appareil(s) à activer";
  - `IosHdActivate` = "Activer".

  English and Spanish translate these.
- ViewModel:
  - `bool IosHdPicture` and `OnIosHdPictureChanged`;
  - on true: confirm `IosHdExplain`, then `PresentAppleDevices()` (none
    gives `IosHdNoDevice`, back to false), then `InstallAsync`. `Done`
    saves with the ids; `Refused` or `Failed` shows its message and goes
    back to false, nothing saved. That is Review Focus 2;
  - on false: save false; if ids are stored, ask `IosHdRemoveAsk`, then
    `UninstallAsync` and clear them;
  - `string? IosHdPending`: present minus filtered, formatted with
    `IosHdNewDevices`, null when none. `ActivateHdDevicesCommand` installs
    those.

- [ ] **Step 1: Strings first.** Run the localization test, expect PASS with all three files filled.
- [ ] **Step 2: ViewModel and XAML.** The checkbox goes under `SharpImage`, with the pending line and the button below it, shown only when `IosHdPending` is not null.
- [ ] **Step 3: Documents** as the spec's Documents section says. In AGENTS.md, the elevation bullet names the one exception and refers to D234. CONFIANCE.md states the promise holds for every player who leaves Image HD off. The iPhone help in `IPhoneWindow` gets one line, a new string `IosHdHelp` ("Image floue ? Essaie l'Image HD dans les réglages."), in all three languages.
- [ ] **Step 4: Build** (`rtk proxy dotnet build`): 0 warnings. Full suite PASS.
- [ ] **Step 5: Commit** `feat(iphone): the Image HD setting, and D234's bounded exception`.

### Task 9: Verification before any player

- [ ] **Step 1:**
  - full suite and build: 0 warnings, 0 failures;
  - Python tests: PASS.
- [ ] **Step 2: Bench** as in Task 7, Step 4, on the final branch.
- [ ] **Step 3: Owner-run safety check** (elevation, so the owner clicks):
  - with no Apple device plugged in, tick Image HD: `IosHdNoDevice`, box
    unticked, no Windows prompt;
  - a running DT Hub with an Android phone keeps working.
- [ ] **Step 4: Real Windows window** with the setting off: an iOS window
  opens exactly as before. Run it on the `C:\Dev\dthub-uia\ipadclic`
  harness with the fake server.
- [ ] **Step 5:** report to the owner. Nothing ships before a player's test
  build covers the spec's device list.
