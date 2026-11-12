"""Apple's QuickTime screen capture protocol over USB, and H.264 helpers.

Ported from quicktime_video_hack by Daniel Paulus (MIT License,
Copyright (c) 2019 danielpaulus): doc/technical_documentation.md,
screencapture/packet and screencapture/coremedia. Pure functions only:
no USB here, the bootstrap that runs serve-web does the reading and writing.

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
"""

import struct
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


def format_description(cvrp):
    """SPS, PPS and size from the format description a CVRP carries."""
    # CVRP payload: the device video clock ref (8), then a string-keyed dict.
    value = _string_value(_child(cvrp.payload[8:], "dict"), "FormatDescription")
    if value is None or value[0] != "fdsc":
        raise ValueError("CVRP without a format description")
    fdsc = value[1]
    width, height = struct.unpack("<II", _child(fdsc, "vdim"))
    # Extensions: key 49 (sample description atoms), then 105 (avcC).
    atoms = _index_value(_child(fdsc, "extn"), 49)
    record = _index_value(atoms[1], 105)[1]
    sps_len = int.from_bytes(record[6:8], "big")
    sps = record[8:8 + sps_len]
    pps_at = 8 + sps_len + 1
    pps_len = int.from_bytes(record[pps_at:pps_at + 2], "big")
    return VideoFormat(sps, record[pps_at + 2:pps_at + 2 + pps_len], width, height)


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


def frame(kind, payload):
    """serve-web's framing. Kinds: 0 key frame, 1 delta, 3 decoder config (JSON)."""
    return (len(payload) + 1).to_bytes(4, "big") + bytes([kind]) + payload


class KeyCache:
    """The last IDR and every AU since, so a new viewer can start decoding."""

    def __init__(self):
        self._aus = []

    def add(self, au, key):
        if key:
            self._aus = [au]
        elif self._aus:
            self._aus.append(au)

    def replay(self):
        return list(self._aus)
