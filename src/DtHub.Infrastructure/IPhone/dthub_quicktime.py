"""Apple's QuickTime screen capture protocol over USB, and H.264 helpers.

Ported from quicktime_video_hack by Daniel Paulus (MIT License,
Copyright (c) 2019 danielpaulus): doc/technical_documentation.md,
screencapture/packet, screencapture/coremedia, and for the USB session
screencapture/discovery.go, activator.go, usbadapter.go and
messageprocessor.go. The protocol functions are pure; install() at the end
runs the capture over pyusb and puts its picture into serve-web, in the
bootstrap's process.

Wire format, all integers little-endian:
- every message starts with its 4-byte length, the length included, then a
  4-byte kind ("sync", "asyn", "ping", "rply"), each magic stored reversed;
- SYNC: length, kind, clock ref (8), magic (4), correlation id (8), payload;
- ASYN: length, kind, clock ref (8), magic (4), payload;
- RPLY: length, kind, correlation id (8), 0 (4), payload.

parse_message takes one whole message, length prefix included, exactly as
read from the device, and checks that prefix. Message.magic is the magic in
reading order, upper case, trailing space removed: "CWPA", "AFMT", "CVRP",
"CLOK", "TIME", "SKEW", "GO!" (quicktime_video_hack's OG), "STOP" for SYNC;
"FEED", "EAT!", "SPRP", "SRAT", "TBAS", "TJMP", "RELS" for ASYN; "PING".

Device data is untrusted: parse_message, format_description and
sample_nalus raise ValueError, and only ValueError, on malformed or
truncated input.
"""

import asyncio
import atexit
import base64
import contextlib
import functools
import json
import logging
import struct
import threading
import time
from dataclasses import dataclass

_TIME_SCALE_NS = 1_000_000_000
_CMTIME_FLAGS_VALID = 1


@dataclass
class Message:
    kind: str  # "PING", "SYNC" or "ASYN"
    magic: str
    correlation_id: bytes  # 8 bytes for SYNC, empty otherwise
    clock_ref: int  # the clock the message is addressed to, 0 for PING
    payload: bytes


@dataclass
class VideoFormat:
    sps: bytes  # NAL unit, header byte included
    pps: bytes
    width: int
    height: int
    avcc: bytes | None = None  # the device's own AVCDecoderConfigurationRecord, unmodified


def _device_data(parse):
    """Turns any parsing accident on device bytes into ValueError."""

    @functools.wraps(parse)
    def checked(*args):
        try:
            return parse(*args)
        except (struct.error, IndexError, TypeError, KeyError) as error:
            raise ValueError("malformed %s input: %r" % (parse.__name__, error)) from error

    return checked


def _wire(magic):
    return magic.encode("ascii")[::-1]


def _readable(raw):
    return raw[::-1].decode("ascii").upper().rstrip()


def _element(magic, content):
    return struct.pack("<I", 8 + len(content)) + _wire(magic) + content


def _elements(data):
    """Yields (magic, content) for each length-prefixed element in data."""
    offset = 0
    while offset < len(data):
        (length,) = struct.unpack_from("<I", data, offset)
        if length < 8 or offset + length > len(data):
            raise ValueError("bad element length %d at %d" % (length, offset))
        yield data[offset + 4:offset + 8][::-1].decode("ascii"), data[offset + 8:offset + length]
        offset += length


def _child(data, magic):
    return next((content for m, content in _elements(data) if m == magic), None)


def _index_value(dict_content, key):
    """The value element of an index-keyed dict ("keyv" holding "idxk" + value)."""
    for magic, pair in _elements(dict_content):
        if magic != "keyv":
            continue
        items = list(_elements(pair))
        key_magic, key_bytes = items[0]
        if key_magic == "idxk" and struct.unpack_from("<H", key_bytes)[0] == key:
            return items[1]
    return None


def _string_value(dict_content, key):
    """The value element of a string-keyed dict ("keyv" holding "strk" + value)."""
    for magic, pair in _elements(dict_content):
        if magic != "keyv":
            continue
        items = list(_elements(pair))
        if items[0] == ("strk", key.encode()):
            return items[1]
    return None


@_device_data
def parse_message(data):
    if len(data) < 8 or struct.unpack_from("<I", data)[0] != len(data):
        raise ValueError("message length prefix does not match its %d bytes" % len(data))
    kind = _readable(data[4:8])
    if kind == "PING":
        return Message(kind, kind, b"", 0, data[8:])
    if kind not in ("SYNC", "ASYN") or len(data) < 20:
        raise ValueError("unexpected %s message of %d bytes" % (kind, len(data)))
    (clock_ref,) = struct.unpack_from("<Q", data, 8)
    magic = _readable(data[16:20])
    if kind == "ASYN":
        return Message(kind, magic, b"", clock_ref, data[20:])
    if len(data) < 28:
        raise ValueError("SYNC %s too short" % magic)
    return Message(kind, magic, data[20:28], clock_ref, data[28:])


def _reply(correlation_id, body):
    return _element("rply", correlation_id + b"\0\0\0\0" + body)


def reply(message, local_clock_ref, now_ns, *, skew=48000.0):
    """The RPLY a SYNC request needs.

    local_clock_ref: the host clock the device will address later requests
    to; used by CWPA (audio clock), CVRP (video clock) and CLOK (new clock).
    now_ns: the host clock's time in nanoseconds; used by TIME.
    skew: host audio clock ticks per device tick at 48 kHz, 48000.0 when the
    clocks agree, which is all a video-only capture can say; used by SKEW.
    """
    magic = message.magic
    if magic in ("CWPA", "CVRP", "CLOK"):
        body = struct.pack("<Q", local_clock_ref)
    elif magic == "AFMT":
        body = _dict(_keyv("Error", _element("nmbv", b"\x03" + struct.pack("<I", 0))))
    elif magic == "TIME":
        body = struct.pack("<QIIQ", now_ns, _TIME_SCALE_NS, _CMTIME_FLAGS_VALID, 0)
    elif magic == "SKEW":
        body = struct.pack("<d", skew)
    elif magic in ("GO!", "STOP"):
        body = b"\0\0\0\0"
    else:
        raise ValueError("no reply for %s %s" % (message.kind, magic))
    return _reply(message.correlation_id, body)


def _dict(*pairs):
    return _element("dict", b"".join(pairs))


def _keyv(key, value_element):
    return _element("keyv", _element("strk", key.encode()) + value_element)


def _bool(value):
    return _element("bulv", b"\x01" if value else b"\x00")


def _float(value):
    return _element("nmbv", b"\x06" + struct.pack("<d", value))


def _asyn(clock_ref, magic, body=b""):
    return _element("asyn", struct.pack("<Q", clock_ref) + _wire(magic) + body)


def hpd1(device_clock_ref):
    """Asks for the video stream. quicktime_video_hack always sends 1 here
    (the empty CFTypeID), not a clock the device gave."""
    info = _dict(
        _keyv("Valeria", _bool(True)),
        _keyv("HEVCDecoderSupports444", _bool(True)),
        _keyv("DisplaySize", _dict(_keyv("Width", _float(1920)), _keyv("Height", _float(1200)))),
    )
    return _asyn(device_clock_ref, "hpd1", info)


def hpd0():
    """Stops the video stream."""
    return _asyn(1, "hpd0")


def need(device_clock_ref):
    """Asks for the next FEED; device_clock_ref is the one CVRP carried."""
    return _asyn(device_clock_ref, "need")


def ping():
    """The host's answer to the device's PING, as quicktime_video_hack sends it."""
    return struct.pack("<I", 16) + _wire("ping") + struct.pack("<II", 0, 1)


@_device_data
def format_description(cvrp):
    """SPS, PPS and size from the format description a CVRP carries."""
    # CVRP payload: the device video clock ref (8), then a string-keyed dict.
    value = _string_value(_child(cvrp.payload[8:], "dict"), "FormatDescription")
    if value is None or value[0] != "fdsc":
        raise ValueError("CVRP without a format description")
    return _video_format(value[1])


@_device_data
def sample_format(feed):
    """The format description a FEED's sample buffer carries (key frames do), or None."""
    sbuf = _child(feed.payload, "sbuf")
    fdsc = _child(sbuf, "fdsc") if sbuf is not None else None
    return _video_format(fdsc) if fdsc is not None else None


def _video_format(fdsc):
    width, height = struct.unpack("<II", _child(fdsc, "vdim"))
    # Extensions: key 49 (sample description atoms), then 105 (avcC).
    atoms = _index_value(_child(fdsc, "extn"), 49)
    record = _index_value(atoms[1], 105)[1]
    sps_len = int.from_bytes(record[6:8], "big")
    pps_at = 8 + sps_len + 1
    pps_len = int.from_bytes(record[pps_at:pps_at + 2], "big")
    if record[0] != 1 or len(record) < pps_at + 2 + pps_len or not sps_len or not pps_len:
        raise ValueError("truncated avcC record")
    sps = record[8:8 + sps_len]
    return VideoFormat(sps, record[pps_at + 2:pps_at + 2 + pps_len], width, height, record)


@_device_data
def sample_nalus(feed):
    """The access unit of a FEED as 4-byte length-prefixed NALUs, or None
    when the sample buffer carries no media."""
    sbuf = _child(feed.payload, "sbuf")
    data = _child(sbuf, "sdat") if sbuf is not None else None
    return data or None


def avcc(sps, pps):
    """AVCDecoderConfigurationRecord: one SPS, one PPS, 4-byte NALU lengths."""
    return (bytes([1]) + sps[1:4] + b"\xff\xe1" + len(sps).to_bytes(2, "big") + sps
            + b"\x01" + len(pps).to_bytes(2, "big") + pps)


def codec_string(sps):
    return "avc1.%02X%02X%02X" % (sps[1], sps[2], sps[3])


def codec_payload(fmt):
    """serve-web's /codec answer for an H.264 picture, with its size for Edge."""
    record = fmt.avcc or avcc(fmt.sps, fmt.pps)
    return {"codec": codec_string(fmt.sps), "description": base64.b64encode(record).decode(),
            "size": {"width": fmt.width, "height": fmt.height}}


def is_key(au):
    """True when the access unit (4-byte length-prefixed NALUs) holds an IDR slice."""
    offset = 0
    while offset + 4 < len(au):
        if au[offset + 4] & 0x1F == 5:
            return True
        offset += 4 + int.from_bytes(au[offset:offset + 4], "big")
    return False


def frame(kind, payload):
    """serve-web's framing. Kinds: 0 key frame, 1 delta, 3 decoder config (JSON)."""
    return (len(payload) + 1).to_bytes(4, "big") + bytes([kind]) + payload


class KeyCache:
    """The last IDR and every AU since, so a new viewer can start decoding.

    Capped at max_aus AUs (10 s at 60 fps) and max_bytes: past either, the
    cache empties and stays empty until the next IDR, since a replay without
    its IDR cannot be decoded.
    """

    def __init__(self, max_aus=600, max_bytes=64 * 1024 * 1024):
        self._max_aus = max_aus
        self._max_bytes = max_bytes
        self._aus = []
        self._bytes = 0

    def add(self, au, key):
        if key:
            self._aus, self._bytes = [], 0
        elif not self._aus:
            return
        self._aus.append(au)
        self._bytes += len(au)
        if len(self._aus) > self._max_aus or self._bytes > self._max_bytes:
            self._aus, self._bytes = [], 0

    def replay(self):
        return list(self._aus)


# ----- The capture session: USB device, protocol thread, serve-web patches -----

_APPLE_VENDOR = 0x05AC
_VENDOR_CLASS = 0xFF
_USBMUX_SUBCLASS = 0xFE
_CAPTURE_SUBCLASS = 0x2A
_FIRST_FRAME_SECONDS = 5.0
_SWITCH_SECONDS = 10.0
# shortcut: one 4 KiB read at a time where quicktime_video_hack keeps five in flight; raise it if FEEDs lag.
_READ_SIZE = 4096
_READ_TIMEOUT_MS = 500
_WRITE_TIMEOUT_MS = 1000
_STOP_TIMEOUT_MS = 300  # stop_capture has about 2 s in all
_MAX_MESSAGE = 64 * 1024 * 1024


class _Unavailable(Exception):
    """Why the HD picture cannot start, written to the log as it is."""


def pick_device(devices, udid):
    """The USB device whose serial number is the UDID, dashes and case aside."""
    wanted = udid.replace("-", "").upper()
    for device in devices:
        try:
            serial = device.serial_number
        except Exception:
            continue  # a device libusb0 cannot open is not the one asked for
        # quicktime_video_hack trims NULs: 24-character serials come padded with them.
        if (serial or "").strip("\x00").replace("-", "").upper() == wanted:
            return device
    return None


def _has(config, subclass):
    return any(i.bInterfaceClass == _VENDOR_CLASS and i.bInterfaceSubClass == subclass for i in config)


def capture_configuration(configs):
    """The configuration with QuickTime's interface (0xFF/0x2A) beside usbmux's (0xFF/0xFE),
    so the tunnel serve-web opens over usbmux keeps working."""
    return next((c for c in configs if _has(c, _CAPTURE_SUBCLASS) and _has(c, _USBMUX_SUBCLASS)), None)


def usbmux_configuration(configs):
    """The last configuration with usbmux and no capture, as quicktime_video_hack picks it."""
    found = None
    for config in configs:
        if _has(config, _USBMUX_SUBCLASS) and not _has(config, _CAPTURE_SUBCLASS):
            found = config
    return found


def should_fall_back(started_at, first_frame_at, now):
    return first_frame_at is None and now - started_at >= _FIRST_FRAME_SECONDS


def is_timeout(error):
    """A read that found nothing to read. pyusb raises USBTimeoutError only when libusb0's
    code is the C library's ETIMEDOUT: libusb-win32 says 116 (or MSVC's 138) where
    Windows' Python says 10060, so a plain USBError carries it there."""
    return (type(error).__name__ == "USBTimeoutError"
            or getattr(error, "backend_error_code", None) in (-116, -138)
            or "timeout" in str(error).lower())


def _reason(error):
    return str(error) if isinstance(error, _Unavailable) else "%s: %s" % (type(error).__name__, error)


class _Picture:
    """What serve-web's video subscribers receive while the capture runs.

    Mode "pending" until the first frame, "hd" while QuickTime's frames go to
    the subscribers, "off" once the capture failed or ended. In "pending" and
    "off" every patch below passes through: that is the un-patching, and
    DisplayService's frames flow as without this module.

    Its methods run on the server's event loop once the server exists, and on
    the capture thread before, when nothing else reads it (see _on_picture).
    """

    def __init__(self):
        self.mode = "pending"
        self.format = None
        self.cache = KeyCache()
        self.server = None
        self.subscribers = None  # the server's _Subscribers once it exists
        self.waiting = set()  # queues that must get a key frame before any delta

    def _queues(self):
        return list(self.subscribers or ())

    def _restart(self, queue):
        """Starts a queue over: the decoder config, then the last key frame and every AU since."""
        while not queue.empty():
            queue.get_nowait()
        aus = self.cache.replay()
        config = frame(3, json.dumps(codec_payload(self.format)).encode())
        queue.put_nowait(config + b"".join(frame(0 if i == 0 else 1, au) for i, au in enumerate(aus)))
        if aus:
            self.waiting.discard(queue)
        else:
            self.waiting.add(queue)

    def subscribe(self, queue, state):
        # Unmarked, DisplayService's escalation would restart its stream for a key this queue never gets.
        state.needs_key = False
        self._restart(queue)

    def set_format(self, fmt):
        if fmt == self.format:
            return
        turned = self.format is not None
        self.format = fmt
        if not turned:
            return
        # The device turned: a new decoder config, and nothing until the next key frame.
        self.cache = KeyCache()
        if self.mode == "hd":
            for queue in self._queues():
                self._restart(queue)

    def deliver(self, au, key):
        self.cache.add(au, key)
        if self.mode == "pending":
            self._start()
            return
        if self.mode != "hd":
            return
        message = frame(0 if key else 1, au)
        for queue in self._queues():
            if queue in self.waiting and not key:
                continue
            if queue.full():
                self._restart(queue)  # the replay carries this AU
            else:
                queue.put_nowait(message)
                self.waiting.discard(queue)

    def _start(self):
        self.mode = "hd"
        _log.info("DT Hub: HD picture on, %dx%d %s", self.format.width, self.format.height,
                  codec_string(self.format.sps))
        for queue, state in dict.items(self.subscribers or {}):
            self.subscribe(queue, state)

    def end(self):
        """The capture stopped: DisplayService's picture takes over again, its own
        decoder config first, from its next key frame."""
        was_hd = self.mode == "hd"
        self.mode = "off"
        self.waiting.clear()
        if not was_hd or self.server is None:
            return
        server = self.server
        config = None
        try:
            if server._codec_string and server._vps_nal and server._sps_nal and server._pps_nal:
                hvcc = _ss.hevc_decoder_configuration_record(server._vps_nal, server._sps_nal, server._pps_nal)
                config = frame(3, json.dumps({"codec": server._codec_string,
                                              "description": base64.b64encode(hvcc).decode()}).encode())
        except Exception:
            # The subscribers still go back to DisplayService's key frames below.
            _log.warning("DT Hub: DisplayService's decoder config not rebuilt", exc_info=True)
        now = asyncio.get_running_loop().time()
        for queue, state in dict.items(self.subscribers):
            while not queue.empty():
                queue.get_nowait()
            if config is not None:
                queue.put_nowait(config)
            state.mark_needs_key(now)
            state.reset_on_key = True
        server._request_recovery_idr(reason="hd-ended")


class _Subscribers(dict):
    """serve-web's video subscribers, queue -> state.

    While the HD picture feeds, DisplayService's fan-out and its restart flush,
    which walk items() and values(), see no subscriber. len(), `in` and the
    keys stay true, so its stall watchdog, refresh loop and PLIs keep its
    hidden stream alive: backboardd drops touches without it.
    """

    def __init__(self, picture):
        super().__init__()
        self._picture = picture

    def __setitem__(self, queue, state):
        if self._picture.mode == "hd":
            self._picture.subscribe(queue, state)  # drops DisplayService's cached key frame
        super().__setitem__(queue, state)

    def pop(self, queue, *default):
        self._picture.waiting.discard(queue)
        return super().pop(queue, *default)

    def items(self):
        return {}.items() if self._picture.mode == "hd" else super().items()

    def values(self):
        return {}.values() if self._picture.mode == "hd" else super().values()


_ss = None  # pymobiledevice3's screen_stream module
_log = logging.getLogger(__name__)  # install() takes serve-web's: DT Hub copies its lines
_lock = threading.Lock()
_loop = None
_picture = _Picture()
_session = None
_server_init = None
_serve_connection = None


def _on_picture(method, *args):
    """Runs a _Picture method where its state lives: the server's loop once it
    exists (asyncio queues are not thread-safe), here before."""
    with _lock:
        loop = _loop
        if loop is None:
            method(*args)
            return
    with contextlib.suppress(RuntimeError):  # the loop is closed: serve-web is ending
        loop.call_soon_threadsafe(method, *args)


def _init_server(self, *args, **kwargs):
    global _loop
    _server_init(self, *args, **kwargs)
    with _lock:
        _picture.server = self
        _picture.subscribers = self._subscribers = _Subscribers(_picture)
        _loop = asyncio.get_running_loop()


class _CodecRoute:
    """A connection's reader that answers GET /codec itself while the HD picture
    feeds. Every request line goes through readline (bodies through readexactly),
    so a /codec on a kept-alive connection is caught too."""

    def __init__(self, server, reader, writer):
        self._server = server
        self._reader = reader
        self._writer = writer

    def __getattr__(self, name):
        return getattr(self._reader, name)

    async def readline(self):
        line = await self._reader.readline()
        parts = line.split()
        if (_picture.mode != "hd" or len(parts) < 2 or parts[0] != b"GET"
                or parts[1].split(b"?", 1)[0] != b"/codec"):
            return line
        headers = {}
        while (header := await self._reader.readline()) not in (b"\r\n", b""):
            name, _, value = header.decode("latin-1").partition(":")
            headers[name.strip().lower()] = value.strip()
        # The stream's own check, as for every other request.
        refusal = self._server._refuse_request(headers)
        if refusal is not None:
            self._writer.write(refusal)
        else:
            body = json.dumps(codec_payload(_picture.format)).encode()
            _ss._respond(self._writer, b"200 OK", body, b"application/json", no_store=True)
        await self._writer.drain()
        return b""  # serve-web reads an empty request line as the end of the connection


async def _serve_connection_hd(self, reader, writer):
    await _serve_connection(self, _CodecRoute(self, reader, writer), writer)


class _Session:
    """QuickTime's capture on the claimed interface, run by one daemon thread."""

    def __init__(self, device, usbmux, interface, ep_in, ep_out, clock=time.monotonic):
        self._device = device
        self._clock = clock  # seconds; tests pass their own
        self._usbmux = usbmux  # the configuration to go back to, None if the device has none
        self._interface = interface
        self._in = ep_in
        self._out = ep_out
        self._write_lock = threading.Lock()
        self._done = threading.Event()
        self._released = threading.Event()
        self._stop_lock = threading.Lock()
        self._stopped = False
        self._need = None
        self._clock_start = time.monotonic_ns()
        self._first_frame_at = None
        self.thread = threading.Thread(target=self._main, name="DT Hub HD picture", daemon=True)

    def _write(self, data, timeout=_WRITE_TIMEOUT_MS):
        with self._write_lock:
            self._out.write(data, timeout)

    def _main(self):
        try:
            self._run(self._clock())
        except Exception as error:
            # Nothing here may take serve-web down. Before the first frame this is the
            # fallback; after it, DisplayService's picture comes back so the window
            # keeps one, as when its own stream ends and restarts.
            if self._done.is_set():
                return  # stop_capture ended it
            if self._first_frame_at is None:
                _log.warning("DT Hub: HD picture unavailable: %s", _reason(error))
            else:
                _log.warning("DT Hub: HD picture ended, DisplayService's picture is back: %s", _reason(error))
            _on_picture(_picture.end)
            # The configuration stays: usbmux works in it and serve-web's tunnel rides on it.
            with contextlib.suppress(Exception):
                self._write(hpd0())
            with contextlib.suppress(Exception):
                import usb.util
                usb.util.release_interface(self._device, self._interface)

    def _run(self, started):
        buffer = bytearray()
        while not self._done.is_set():
            if should_fall_back(started, self._first_frame_at, self._clock()):
                raise _Unavailable("no frame within %d s" % _FIRST_FRAME_SECONDS)
            try:
                buffer += self._in.read(_READ_SIZE, _READ_TIMEOUT_MS)
            except Exception as error:
                if is_timeout(error):
                    continue
                raise
            while len(buffer) >= 4:
                length = int.from_bytes(buffer[:4], "little")
                if not 8 <= length <= _MAX_MESSAGE:
                    raise ValueError("bad message length %d" % length)
                if len(buffer) < length:
                    break
                message = parse_message(bytes(buffer[:length]))
                del buffer[:length]
                self._handle(message)

    def _handle(self, message):
        if message.kind == "PING":
            self._write(ping())
        elif message.kind == "SYNC":
            self._sync(message)
        elif message.magic == "FEED":
            self._feed(message)
        elif message.magic == "RELS":
            self._released.set()
        # EAT!, SPRP, SRAT, TBAS, TJMP: nothing to answer.

    def _sync(self, message):
        magic = message.magic
        if magic == "CWPA":
            # quicktime_video_hack asks for video here, twice, then replies. HPA1, the
            # audio request, is left out: the sound stays DisplayService's.
            self._write(hpd1(1))
            self._write(hpd1(1))
            self._write(reply(message, _device_clock(message) + 1000, 0))
        elif magic == "CVRP":
            fmt = format_description(message)
            self._need = need(_device_clock(message))
            self._write(self._need)
            self._write(reply(message, _device_clock(message) + 0x1000AF, 0))
            _on_picture(_picture.set_format, fmt)
        elif magic == "CLOK":
            self._clock_start = time.monotonic_ns()
            self._write(reply(message, message.clock_ref + 0x10000, 0))
        elif magic == "TIME":
            self._write(reply(message, 0, time.monotonic_ns() - self._clock_start))
        else:
            # AFMT, SKEW, GO!, STOP. Any other raises ValueError and ends the
            # capture, as quicktime_video_hack stops on it.
            self._write(reply(message, 0, 0))

    def _feed(self, message):
        if self._need is None:
            raise ValueError("FEED before CVRP")
        au = sample_nalus(message)
        fmt = None
        with contextlib.suppress(ValueError):  # the CVRP's format stands when a sample's own cannot be read
            fmt = sample_format(message)
        if fmt is not None:
            _on_picture(_picture.set_format, fmt)
        if au is not None:
            if self._first_frame_at is None:
                self._first_frame_at = self._clock()
            _on_picture(_picture.deliver, au, is_key(au))
        self._write(self._need)

    def stop(self):
        with self._stop_lock:
            if self._stopped:
                return
            self._stopped = True
        import usb.util
        if self.thread.is_alive():
            with contextlib.suppress(Exception):
                self._write(hpd0(), _STOP_TIMEOUT_MS)
                self._released.wait(0.7)  # the thread answers the device's STOP meanwhile
        self._done.set()
        self.thread.join(_READ_TIMEOUT_MS / 1000 + 0.1)
        steps = [
            lambda: usb.util.release_interface(self._device, self._interface),
            # Then as quicktime_video_hack: hide the capture configuration, back to usbmux's.
            lambda: self._device.ctrl_transfer(0x40, 0x52, 0x00, 0x00, timeout=_STOP_TIMEOUT_MS),
        ]
        if self._usbmux is not None:
            steps.append(lambda: self._device.set_configuration(self._usbmux.bConfigurationValue))
        steps.append(lambda: usb.util.dispose_resources(self._device))
        for step in steps:
            with contextlib.suppress(Exception):  # the device may already have left the bus
                step()


def _device_clock(message):
    if len(message.payload) < 8:
        raise ValueError("%s without its clock" % message.magic)
    return struct.unpack_from("<Q", message.payload)[0]


def _open(udid, libusb0_path):
    """Finds the device, shows its capture configuration and claims QuickTime's interface."""
    try:
        import usb.backend.libusb0
        import usb.core
        import usb.util
    except ImportError as error:
        raise _Unavailable("pyusb not installed") from error
    backend = usb.backend.libusb0.get_backend(find_library=lambda _: libusb0_path)
    if backend is None:
        raise _Unavailable("libusb0 backend unavailable")

    def dispose(device):
        with contextlib.suppress(Exception):
            usb.util.dispose_resources(device)

    def find():
        devices = list(usb.core.find(find_all=True, idVendor=_APPLE_VENDOR, backend=backend))
        found = pick_device(devices, udid)
        for other in devices:
            if other is not found:
                dispose(other)  # reading its serial opened it
        return found

    device = find()
    if device is None:
        raise _Unavailable("device not found by libusb0")
    config = capture_configuration(device.configurations())
    if config is None:
        try:
            device.ctrl_transfer(0x40, 0x52, 0x00, 0x02)
        except usb.core.USBError:
            pass  # quicktime_video_hack: "happens sometimes but it still works usually"
        # The device leaves the bus and comes back with the hidden configuration.
        dispose(device)
        deadline = time.monotonic() + _SWITCH_SECONDS
        while config is None:
            if time.monotonic() >= deadline:
                raise _Unavailable("the capture configuration did not come back within %d s" % _SWITCH_SECONDS)
            time.sleep(0.5)
            try:
                device = find()
                config = capture_configuration(device.configurations()) if device is not None else None
            except usb.core.USBError:
                config = None  # still re-enumerating
            if config is None and device is not None:
                dispose(device)
    configs = device.configurations()
    usbmux = usbmux_configuration(configs)
    interface = next(i for i in config
                     if i.bInterfaceClass == _VENDOR_CLASS and i.bInterfaceSubClass == _CAPTURE_SUBCLASS)

    def endpoint(direction):
        return usb.util.find_descriptor(
            interface, custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress) == direction)

    try:
        device.set_configuration(config.bConfigurationValue)
        usb.util.claim_interface(device, interface.bInterfaceNumber)
        ep_in, ep_out = endpoint(usb.util.ENDPOINT_IN), endpoint(usb.util.ENDPOINT_OUT)
        if ep_in is None or ep_out is None:
            raise _Unavailable("capture interface without its two bulk endpoints")
        for ep in (ep_in, ep_out):
            device.ctrl_transfer(0x02, 0x01, 0x00, ep.bEndpointAddress)  # CLEAR_FEATURE(ENDPOINT_HALT), as qvh
        return _Session(device, usbmux, interface.bInterfaceNumber, ep_in, ep_out)
    except Exception:
        with contextlib.suppress(Exception):
            usb.util.release_interface(device, interface.bInterfaceNumber)
        dispose(device)
        raise


def install(screen_stream_module, udid, libusb0_path):
    """Called by the bootstrap before serve-web starts. Switches the device to its
    capture configuration here (up to 10 s: serve-web's tunnel must open after the
    device came back), patches the server, starts the capture thread and returns.
    Never raises: a failure logs "DT Hub: HD picture unavailable: <reason>" and
    leaves serve-web as it is."""
    global _ss, _log, _session, _server_init, _serve_connection
    _ss = screen_stream_module
    _log = screen_stream_module.logger
    try:
        session = _open(udid, libusb0_path)
    except Exception as error:
        _log.warning("DT Hub: HD picture unavailable: %s", _reason(error))
        return
    server = screen_stream_module.ScreenStreamServer
    _server_init, _serve_connection = server.__init__, server._serve_connection
    server.__init__ = _init_server
    server._serve_connection = _serve_connection_hd
    _session = session
    atexit.register(stop_capture)
    session.thread.start()


def stop_capture():
    """Ends the capture cleanly: HPD0, the device's STOP answered, RELS awaited
    briefly, then usbmux's configuration back. For closing only: going back
    re-enumerates the device and resets usbmux, which ends serve-web's tunnel;
    the caller kills the tool right after. Idempotent, never raises, about 2 s
    at most. Called by the bootstrap's POST /capture/stop, and at exit."""
    session = _session
    if session is None:
        return
    try:
        session.stop()
    except Exception:
        _log.warning("DT Hub: HD capture not stopped cleanly", exc_info=True)
