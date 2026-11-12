"""Checks dthub_quicktime against messages recorded from a real iPhone.

The fixtures come from quicktime_video_hack (MIT), see fixtures/README.md.
Run with the iPhone tool's Python, from the repository root:
    python -I -m unittest discover -s tests/quicktime -v
"""

import asyncio
import base64
import itertools
import json
import struct
import sys
import types
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1] / "src" / "DtHub.Infrastructure" / "IPhone"))

import dthub_quicktime as qt  # noqa: E402


def fixture(name):
    return (HERE / "fixtures" / name).read_bytes()


def message(name):
    return qt.parse_message(fixture(name))


class Messages(unittest.TestCase):
    def test_chaque_requete_se_lit_avec_sa_magie(self):
        attendus = {
            "cwpa-request1": "CWPA", "afmt-request": "AFMT", "cvrp-request": "CVRP",
            "clok-request": "CLOK", "time-request1": "TIME", "skew-request": "SKEW",
            "og-request": "GO!", "stop-request": "STOP",
        }
        for name, magic in attendus.items():
            with self.subTest(name):
                msg = message(name)
                self.assertEqual("SYNC", msg.kind)
                self.assertEqual(magic, msg.magic)
                self.assertEqual(8, len(msg.correlation_id))

    def test_les_messages_asyn_se_lisent_avec_leur_horloge(self):
        feed = message("asyn-feed")
        self.assertEqual(("ASYN", "FEED", 0x7FFB5CC32F60), (feed.kind, feed.magic, feed.clock_ref))
        rels = message("asyn-rels")
        self.assertEqual(("ASYN", "RELS", 0x7FBA35608A00), (rels.kind, rels.magic, rels.clock_ref))
        # Recorded without its length prefix: put it back.
        raw = fixture("asyn-sprp")
        sprp = qt.parse_message(struct.pack("<I", len(raw) + 4) + raw)
        self.assertEqual(("ASYN", "SPRP", 0x11123BC18), (sprp.kind, sprp.magic, sprp.clock_ref))

    def test_une_longueur_fausse_est_refusee(self):
        with self.assertRaises(ValueError):
            qt.parse_message(fixture("clok-request")[:-1])

    def test_un_en_tete_trop_court_est_refuse(self):
        with self.assertRaises(ValueError):
            qt.parse_message(struct.pack("<I", 16) + fixture("clok-request")[4:16])


class Replies(unittest.TestCase):
    """The host's clock refs are the ones the device addresses its next
    request to, so each is read from the following request fixture."""

    def check(self, request, reply, local_clock_ref=0, now_ns=0, **kw):
        self.assertEqual(fixture(reply), qt.reply(message(request), local_clock_ref, now_ns, **kw))

    def test_cwpa_repond_l_horloge_que_la_requete_afmt_vise(self):
        self.check("cwpa-request1", "cwpa-reply1", message("afmt-request").clock_ref)

    def test_cvrp_repond_l_horloge_que_la_requete_clok_vise(self):
        self.check("cvrp-request", "cvrp-reply", message("clok-request").clock_ref)

    def test_clok_repond_l_horloge_que_la_requete_time_vise(self):
        self.check("clok-request", "clok-reply", message("time-request1").clock_ref)

    def test_time_repond_l_heure_donnee_en_nanosecondes(self):
        # The time is the host clock's own reading: taken from the recorded reply.
        now_ns = struct.unpack_from("<Q", fixture("time-reply1"), 20)[0]
        self.check("time-request1", "time-reply1", now_ns=now_ns)

    def test_skew_repond_le_decalage_donne(self):
        skew = struct.unpack_from("<d", fixture("skew-reply"), 20)[0]
        self.check("skew-request", "skew-reply", skew=skew)

    def test_afmt_og_et_stop_repondent_sans_rien_de_plus(self):
        for request, reply in [("afmt-request", "afmt-reply"), ("og-request", "og-reply"),
                               ("stop-request", "stop-reply")]:
            with self.subTest(request):
                self.check(request, reply)


class HostMessages(unittest.TestCase):
    def test_hpd1_hpd0_et_need_sont_ceux_enregistres(self):
        for name, build in [("asyn-hpd1", qt.hpd1), ("asyn-need", qt.need)]:
            with self.subTest(name):
                recorded = fixture(name)
                clock_ref = struct.unpack_from("<Q", recorded, 8)[0]
                self.assertEqual(recorded, build(clock_ref))
        self.assertEqual(fixture("asyn-hpd0"), qt.hpd0())


class Video(unittest.TestCase):
    def test_la_description_donne_sps_pps_et_taille(self):
        fmt = qt.format_description(message("cvrp-request"))
        # This iPhone's SPS NAL header is 0x27 (nal_ref_idc 1), not 0x67: type 7 is what counts.
        self.assertEqual(7, fmt.sps[0] & 0x1F)
        self.assertEqual(8, fmt.pps[0] & 0x1F)
        self.assertEqual((1126, 2436), (fmt.width, fmt.height))
        self.assertEqual("avc1.640033", qt.codec_string(fmt.sps))

    def test_l_avcc_de_l_appareil_est_donne_tel_quel(self):
        fmt = qt.format_description(message("cvrp-request"))
        self.assertEqual(1, fmt.avcc[0])
        self.assertEqual(fmt.sps[1], fmt.avcc[1])
        self.assertIn(fmt.pps, fmt.avcc)
        self.assertIn(fmt.avcc, fixture("cvrp-request"))

    def test_avcc_suit_le_format_standard(self):
        fmt = qt.format_description(message("cvrp-request"))
        record = qt.avcc(fmt.sps, fmt.pps)
        expected = (bytes([1]) + fmt.sps[1:4] + b"\xff\xe1"
                    + struct.pack(">H", len(fmt.sps)) + fmt.sps
                    + b"\x01" + struct.pack(">H", len(fmt.pps)) + fmt.pps)
        self.assertEqual(expected, record)
        # The device's own record starts the same way.
        self.assertIn(record[:6 + 2 + len(fmt.sps)], fixture("cvrp-request"))

    def test_le_feed_donne_des_nalus_dont_les_longueurs_tombent_juste(self):
        au = qt.sample_nalus(message("asyn-feed"))
        self.assertTrue(au)
        types, offset = [], 0
        while offset < len(au):
            size = int.from_bytes(au[offset:offset + 4], "big")
            types.append(au[offset + 4] & 0x1F)
            offset += 4 + size
        self.assertEqual(len(au), offset)
        self.assertEqual([6, 5], types)  # SEI then IDR, as quicktime_video_hack reads it

    def test_un_feed_sans_media_ne_donne_rien(self):
        feed = message("asyn-feed")
        empty = qt.Message(feed.kind, feed.magic, feed.correlation_id, feed.clock_ref,
                           struct.pack("<I", 8) + b"fubs")
        self.assertIsNone(qt.sample_nalus(empty))


def truncated(name):
    """The message cut in the middle, its length prefix rewritten to match."""
    raw = fixture(name)[:len(fixture(name)) // 2]
    return qt.parse_message(struct.pack("<I", len(raw)) + raw[4:])


class MalformedInput(unittest.TestCase):
    def test_un_cvrp_tronque_leve_value_error(self):
        with self.assertRaises(ValueError):
            qt.format_description(truncated("cvrp-request"))

    def test_un_feed_tronque_leve_value_error(self):
        with self.assertRaises(ValueError):
            qt.sample_nalus(truncated("asyn-feed"))

    def test_un_avcc_tronque_leve_value_error(self):
        raw = bytearray(fixture("cvrp-request"))
        record = raw.index(bytes.fromhex("01640033ffe1"))
        raw[record + 7] += 100  # SPS length now runs past the record
        with self.assertRaises(ValueError):
            qt.format_description(qt.parse_message(bytes(raw)))


class Framing(unittest.TestCase):
    def test_trame_de_configuration(self):
        self.assertEqual(b"\x00\x00\x00\x03\x03{}", qt.frame(3, b"{}"))

    def test_le_cache_rejoue_depuis_la_derniere_image_cle(self):
        cache = qt.KeyCache()
        cache.add(b"A", True)
        cache.add(b"B", False)
        cache.add(b"C", True)
        cache.add(b"D", False)
        self.assertEqual([b"C", b"D"], cache.replay())

    def test_le_cache_ignore_ce_qui_precede_la_premiere_image_cle(self):
        cache = qt.KeyCache()
        cache.add(b"B", False)
        self.assertEqual([], cache.replay())

    def test_le_cache_garde_tout_depuis_l_image_cle_quel_que_soit_le_nombre(self):
        # DOFUS animates without pause: a count cap would empty it 10 s after each IDR.
        cache = qt.KeyCache()
        cache.add(b"K", True)
        for _ in range(5000):
            cache.add(b"d", False)
        self.assertEqual(5001, len(cache.replay()))

    def test_le_cache_plein_d_octets_se_vide_jusqu_a_la_prochaine_image_cle(self):
        cache = qt.KeyCache(max_bytes=10)
        cache.add(b"K" * 6, True)
        cache.add(b"d" * 6, False)
        self.assertEqual([], cache.replay())
        cache.add(b"d", False)
        self.assertEqual([], cache.replay())
        cache.add(b"K2", True)
        cache.add(b"d5", False)
        self.assertEqual([b"K2", b"d5"], cache.replay())


class FakeDevice:
    def __init__(self, serial):
        self._serial = serial

    @property
    def serial_number(self):
        if isinstance(self._serial, Exception):
            raise self._serial
        return self._serial


def interface(cls, subclass, number=0):
    return SimpleNamespace(bInterfaceClass=cls, bInterfaceSubClass=subclass, bInterfaceNumber=number)


class Device(unittest.TestCase):
    def test_l_appareil_se_trouve_par_son_udid_sans_tiret_ni_casse(self):
        wanted = FakeDevice("00008120001a2b3c4d5e")
        devices = [FakeDevice("00008030000A1B2C3D4E"), wanted]
        self.assertIs(wanted, qt.pick_device(devices, "00008120-001A2B3C4D5E"))

    def test_un_autre_appareil_apple_ne_convient_pas(self):
        self.assertIsNone(qt.pick_device([FakeDevice("00008030000A1B2C3D4E")], "00008120-001A2B3C4D5E"))

    def test_un_numero_illisible_est_saute(self):
        wanted = FakeDevice("00008120001A2B3C4D5E")
        devices = [FakeDevice(ValueError("The device has no langid")), FakeDevice(None), wanted]
        self.assertIs(wanted, qt.pick_device(devices, "00008120-001A2B3C4D5E"))

    def test_un_numero_complete_de_zeros_nuls_convient(self):
        # quicktime_video_hack trims them: 24-character serials come padded.
        wanted = FakeDevice("00008120001A2B3C4D5E" + "\x00" * 16)
        self.assertIs(wanted, qt.pick_device([wanted], "00008120-001A2B3C4D5E"))

    def test_la_configuration_de_capture_a_l_interface_2a(self):
        usbmux = [interface(6, 1), interface(0xFF, 0xFE)]
        capture = [interface(6, 1), interface(0xFF, 0xFE), interface(0xFF, 0x2A)]
        self.assertIs(capture, qt.capture_configuration([usbmux, capture]))
        self.assertIsNone(qt.capture_configuration([usbmux]))

    def test_la_configuration_de_capture_garde_usbmux(self):
        self.assertIsNone(qt.capture_configuration([[interface(0xFF, 0x2A)]]))

    def test_la_configuration_usbmux_est_la_derniere_sans_capture(self):
        ptp = [interface(6, 1)]
        usbmux = [interface(6, 1), interface(0xFF, 0xFE)]
        usbmux_ncm = [interface(6, 1), interface(0xFF, 0xFE), interface(2, 0x0D)]
        capture = [interface(0xFF, 0xFE), interface(0xFF, 0x2A)]
        self.assertIs(usbmux_ncm, qt.usbmux_configuration([ptp, usbmux, usbmux_ncm, capture]))

    def test_repli_apres_cinq_secondes_sans_image(self):
        self.assertFalse(qt.should_fall_back(0, None, 4.9))
        self.assertTrue(qt.should_fall_back(0, None, 5.0))
        self.assertFalse(qt.should_fall_back(0, 1.0, 60))

    def test_une_lecture_vide_n_est_pas_une_erreur(self):
        USBTimeoutError = type("USBTimeoutError", (Exception,), {})
        USBError = type("USBError", (Exception,), {})
        timeout_116 = USBError("libusb0-dll:err [_usb_reap_async] timeout error")
        timeout_116.backend_error_code = -116
        timeout_138 = USBError("Unknown error")
        timeout_138.backend_error_code = -138
        gone = USBError("libusb0-dll:err [_usb_reap_async] reaping request failed")
        gone.backend_error_code = -5
        self.assertTrue(qt.is_timeout(USBTimeoutError("Operation timed out")))
        self.assertTrue(qt.is_timeout(timeout_116))
        self.assertTrue(qt.is_timeout(timeout_138))
        self.assertFalse(qt.is_timeout(gone))

    def test_la_reponse_au_ping_est_celle_de_quicktime_video_hack(self):
        self.assertEqual(bytes.fromhex("10000000676e69700000000001000000"), qt.ping())


class Samples(unittest.TestCase):
    def test_une_image_cle_se_reconnait_a_son_idr(self):
        self.assertTrue(qt.is_key(qt.sample_nalus(message("asyn-feed"))))
        delta = b"\x00\x00\x00\x03\x21\xaa\xbb"
        self.assertFalse(qt.is_key(delta))
        self.assertFalse(qt.is_key(b"\x00\x00\x00\x09\x21"))  # truncated: no crash

    def test_le_feed_porte_le_meme_format_que_le_cvrp(self):
        self.assertEqual(qt.format_description(message("cvrp-request")), qt.sample_format(message("asyn-feed")))

    def test_un_feed_sans_description_ne_donne_pas_de_format(self):
        feed = message("asyn-feed")
        empty = qt.Message(feed.kind, feed.magic, feed.correlation_id, feed.clock_ref,
                           struct.pack("<I", 8) + b"fubs")
        self.assertIsNone(qt.sample_format(empty))

    def test_codec_donne_le_codec_l_avcc_de_l_appareil_et_la_taille(self):
        fmt = qt.format_description(message("cvrp-request"))
        self.assertEqual({"codec": "avc1.640033", "description": base64.b64encode(fmt.avcc).decode(),
                          "size": {"width": 1126, "height": 2436}}, qt.codec_payload(fmt))

    def test_codec_sans_avcc_de_l_appareil_le_reconstruit(self):
        fmt = qt.format_description(message("cvrp-request"))
        bare = qt.VideoFormat(fmt.sps, fmt.pps, fmt.width, fmt.height)
        self.assertEqual(base64.b64encode(qt.avcc(fmt.sps, fmt.pps)).decode(), qt.codec_payload(bare)["description"])


def drain(queue):
    items = []
    while not queue.empty():
        items.append(queue.get_nowait())
    return items


def config(fmt):
    return qt.frame(3, json.dumps(qt.codec_payload(fmt)).encode())


class Subscribers(unittest.TestCase):
    """serve-web's subscriber queues, without the server: asyncio queues work without a loop."""

    def setUp(self):
        self.fmt = qt.format_description(message("cvrp-request"))
        self.picture = qt._Picture()
        self.subscribers = qt._Subscribers(self.picture)
        self.picture.subscribers = self.subscribers

    def subscribe(self, maxsize=64):
        # As serve-web's /stream.bin does: DisplayService's cached key frame first, then the state.
        queue = asyncio.Queue(maxsize=maxsize)
        queue.put_nowait(b"hevc-key")
        state = SimpleNamespace(needs_key=True, reset_on_key=True)
        self.subscribers[queue] = state
        return queue, state

    def test_avant_l_image_hd_displayservice_garde_ses_abonnes(self):
        queue, state = self.subscribe()
        self.assertEqual([b"hevc-key"], drain(queue))
        self.assertTrue(state.needs_key)
        self.assertEqual([(queue, state)], list(self.subscribers.items()))

    def test_en_hd_un_abonne_recoit_la_config_et_le_cache_au_lieu_de_displayservice(self):
        self.picture.set_format(self.fmt)
        self.picture.deliver(b"K", True)
        self.picture.deliver(b"D", False)
        queue, state = self.subscribe()
        self.assertEqual([config(self.fmt) + qt.frame(0, b"K") + qt.frame(1, b"D")], drain(queue))
        self.assertFalse(state.needs_key)  # DisplayService would otherwise restart its stream for it
        self.assertEqual([], list(self.subscribers.items()))
        self.assertEqual([], list(self.subscribers.values()))
        self.assertIn(queue, self.subscribers)
        self.assertEqual(1, len(self.subscribers))

    def test_la_premiere_image_hd_reprend_les_abonnes_deja_la(self):
        queue, state = self.subscribe()
        self.picture.set_format(self.fmt)
        self.picture.deliver(b"K", True)
        self.assertEqual([config(self.fmt) + qt.frame(0, b"K")], drain(queue))
        self.assertFalse(state.needs_key)

    def test_apres_un_changement_de_format_l_abonne_attend_une_image_cle(self):
        self.picture.set_format(self.fmt)
        self.picture.deliver(b"K", True)
        queue, _ = self.subscribe()
        drain(queue)
        self.picture.deliver(b"D", False)
        self.assertEqual([qt.frame(1, b"D")], drain(queue))
        turned = qt.VideoFormat(self.fmt.sps, self.fmt.pps, self.fmt.height, self.fmt.width, self.fmt.avcc)
        self.picture.set_format(turned)
        self.picture.deliver(b"D2", False)
        self.assertEqual([config(turned)], drain(queue))
        self.picture.deliver(b"K2", True)
        self.picture.deliver(b"D3", False)
        self.assertEqual([qt.frame(0, b"K2"), qt.frame(1, b"D3")], drain(queue))

    def test_une_file_pleine_repart_de_l_image_cle(self):
        self.picture.set_format(self.fmt)
        self.picture.deliver(b"K", True)
        queue, _ = self.subscribe(maxsize=2)
        self.picture.deliver(b"D1", False)
        self.picture.deliver(b"D2", False)
        self.assertEqual([config(self.fmt) + qt.frame(0, b"K") + qt.frame(1, b"D1") + qt.frame(1, b"D2")],
                         drain(queue))

    def test_apres_la_fin_de_la_capture_displayservice_reprend(self):
        self.picture.set_format(self.fmt)
        self.picture.deliver(b"K", True)
        queue, state = self.subscribe()
        drain(queue)
        self.picture.end()
        self.picture.deliver(b"D", False)
        self.assertEqual([], drain(queue))
        self.assertEqual([(queue, state)], list(self.subscribers.items()))


    def test_sans_image_cle_trois_secondes_l_image_hd_rend_displayservice(self):
        now = [0.0]
        self.picture = qt._Picture(clock=lambda: now[0])
        self.subscribers = qt._Subscribers(self.picture)
        self.picture.subscribers = self.subscribers
        self.picture.set_format(self.fmt)
        self.picture.deliver(b"K", True)
        self.picture.cache = qt.KeyCache()  # emptied at its byte cap, as long after the only key frame
        queue, _ = self.subscribe()
        self.assertEqual([config(self.fmt)], drain(queue))
        now[0] = 2.9
        self.picture.deliver(b"D1", False)
        self.assertEqual("hd", self.picture.mode)
        now[0] = 3.0
        with self.assertLogs(qt._log, "WARNING") as logs:
            self.picture.deliver(b"D2", False)
        self.assertEqual(["DT Hub: HD picture unavailable: no key frame for a viewer"],
                         [r.getMessage() for r in logs.records])
        self.assertEqual("off", self.picture.mode)

    def test_une_image_cle_a_temps_garde_l_image_hd(self):
        now = [0.0]
        self.picture = qt._Picture(clock=lambda: now[0])
        self.subscribers = qt._Subscribers(self.picture)
        self.picture.subscribers = self.subscribers
        self.picture.set_format(self.fmt)
        self.picture.deliver(b"K", True)
        self.picture.cache = qt.KeyCache()
        queue, _ = self.subscribe()
        now[0] = 2.0
        self.picture.deliver(b"K2", True)
        now[0] = 10.0
        self.picture.deliver(b"D", False)
        self.assertEqual("hd", self.picture.mode)
        self.assertEqual([config(self.fmt), qt.frame(0, b"K2"), qt.frame(1, b"D")], drain(queue))


class ServeWeb(unittest.IsolatedAsyncioTestCase):
    """The patches on serve-web's own server (the installed pymobiledevice3), on a local socket, without a device."""

    @classmethod
    def setUpClass(cls):
        from pymobiledevice3.remote.core_device import screen_stream  # slow first import, kept off the loop
        cls.screen_stream = screen_stream

    async def asyncSetUp(self):
        screen_stream = self.screen_stream
        server_class = screen_stream.ScreenStreamServer
        self.saved = (server_class.__init__, server_class._serve_connection)
        qt._ss, qt._picture, qt._loop = screen_stream, qt._Picture(), None
        qt._server_init, qt._serve_connection = self.saved
        server_class.__init__, server_class._serve_connection = qt._init_server, qt._serve_connection_hd
        self.server = server_class(SimpleNamespace(service=SimpleNamespace(address=("::1", 0))), http_port=0)
        self.listener = await asyncio.start_server(self.server._handle_http, "127.0.0.1", 0)
        self.port = self.listener.sockets[0].getsockname()[1]
        self.fmt = qt.format_description(message("cvrp-request"))
        qt._picture.set_format(self.fmt)
        qt._picture.deliver(b"\x00\x00\x00\x02\x65\x88", True)

    async def asyncTearDown(self):
        self.listener.close()
        server_class = self.screen_stream.ScreenStreamServer
        server_class.__init__, server_class._serve_connection = self.saved
        qt._picture, qt._loop = qt._Picture(), None

    async def test_codec_repond_l_image_hd_meme_apres_un_toucher_sur_la_connexion(self):
        reader, writer = await asyncio.open_connection("127.0.0.1", self.port)
        writer.write(b"POST /touch HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 2\r\n\r\n{}"
                     b"GET /codec HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n")
        answer = await asyncio.wait_for(reader.read(), 5)
        writer.close()
        self.assertTrue(answer.endswith(b"\r\n\r\n" + json.dumps(qt.codec_payload(self.fmt)).encode()))

    async def test_pli_en_hd_repart_du_cache(self):
        queue = asyncio.Queue()
        self.server._subscribers[queue] = SimpleNamespace(needs_key=True)
        drain(queue)
        qt._picture.deliver(b"\x00\x00\x00\x02\x41\x9a", False)
        reader, writer = await asyncio.open_connection("127.0.0.1", self.port)
        writer.write(b"POST /pli HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 0\r\n\r\n")
        answer = await asyncio.wait_for(reader.read(), 5)
        writer.close()
        self.assertTrue(answer.startswith(b"HTTP/1.1 202"))
        self.assertEqual([config(self.fmt) + qt.frame(0, b"\x00\x00\x00\x02\x65\x88")
                          + qt.frame(1, b"\x00\x00\x00\x02\x41\x9a")], drain(queue))

    async def test_trop_de_en_tetes_sont_refuses_sans_remplir_le_rejeu(self):
        reader, writer = await asyncio.open_connection("127.0.0.1", self.port)
        writer.write(b"POST /pli HTTP/1.1\r\nHost: 127.0.0.1\r\n" + b"X-A: 1\r\n" * 500 + b"\r\n")
        answer = await asyncio.wait_for(reader.read(), 5)
        writer.close()
        self.assertTrue(answer.startswith(b"HTTP/1.1 431"))

    async def test_une_pli_refusee_ne_repart_pas_du_cache(self):
        queue = asyncio.Queue()
        self.server._subscribers[queue] = SimpleNamespace(needs_key=True)
        drain(queue)
        reader, writer = await asyncio.open_connection("127.0.0.1", self.port)
        writer.write(b"POST /pli HTTP/1.1\r\nHost: 127.0.0.1\r\nOrigin: http://other.example\r\n"
                     b"Content-Length: 0\r\n\r\n")
        answer = await asyncio.wait_for(reader.read(), 5)
        writer.close()
        self.assertTrue(answer.startswith(b"HTTP/1.1 403"))
        self.assertEqual([], drain(queue))

    async def test_stream_bin_commence_par_la_config_et_le_cache(self):
        self.server._active_service, self.server._init_sequence = object(), b"hevc-key"
        self.server._stream_ready.set()
        reader, writer = await asyncio.open_connection("127.0.0.1", self.port)
        writer.write(b"GET /stream.bin HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n")
        await reader.readuntil(b"\r\n\r\n")
        size = int(await reader.readline(), 16)
        chunk = await reader.readexactly(size)
        writer.close()
        self.assertEqual(config(self.fmt) + qt.frame(0, b"\x00\x00\x00\x02\x65\x88"), chunk)
        self.assertEqual([], list(self.server._subscribers.items()))


USBTimeoutError = type("USBTimeoutError", (Exception,), {})


class FakeIn:
    """Gives its chunks one read each, then only timeouts, calling when_empty once."""

    def __init__(self, chunks, when_empty=lambda: None):
        self.chunks = list(chunks)
        self.when_empty = when_empty

    def read(self, size, timeout):
        if self.chunks:
            return self.chunks.pop(0)
        self.when_empty()
        raise USBTimeoutError("Operation timed out")


class FakeOut:
    def __init__(self, log=None):
        self.log = [] if log is None else log

    def write(self, data, timeout):
        self.log.append(bytes(data))


def device_clock(name):
    return struct.unpack_from("<Q", message(name).payload)[0]


class Session(unittest.TestCase):
    """The capture thread's work, run in the test's own thread with fake endpoints."""

    def setUp(self):
        qt._picture, qt._loop = qt._Picture(), None

    def tearDown(self):
        qt._picture = qt._Picture()

    def session(self, chunks, clock=lambda: 0.0):
        self.out = FakeOut()
        session = qt._Session(SimpleNamespace(), None, 0, FakeIn(chunks), self.out, clock=clock)
        session._in.when_empty = session._done.set
        return session

    def test_le_protocole_repond_et_livre_une_image_coupee_en_deux_lectures(self):
        feed = fixture("asyn-feed")
        session = self.session([qt.ping() + fixture("cwpa-request1"), fixture("cvrp-request"),
                                feed[:1000], feed[1000:]])
        session._main()
        audio, video = device_clock("cwpa-request1"), device_clock("cvrp-request")
        self.assertEqual([qt.ping(), qt.hpd1(1), qt.hpd1(1), qt.reply(message("cwpa-request1"), audio + 1000, 0),
                          qt.need(video), qt.reply(message("cvrp-request"), video + 0x1000AF, 0), qt.need(video)],
                         self.out.log)
        self.assertEqual("hd", qt._picture.mode)
        self.assertEqual([qt.sample_nalus(message("asyn-feed"))], qt._picture.cache.replay())

    def test_sans_image_en_cinq_secondes_l_image_hd_est_abandonnee(self):
        session = self.session([], clock=itertools.count(0.0, 0.5).__next__)
        session._in.when_empty = lambda: None
        with self.assertLogs(qt._log, "WARNING") as logs:
            session._main()
        self.assertEqual(["DT Hub: HD picture unavailable: no frame within 5 s"], [r.getMessage() for r in logs.records])
        self.assertEqual("off", qt._picture.mode)
        self.assertEqual([qt.hpd0()], self.out.log)

    def test_une_longueur_hors_bornes_abandonne_l_image_hd(self):
        for length in (4, 64 * 1024 * 1024 + 1):
            with self.subTest(length):
                qt._picture = qt._Picture()
                session = self.session([struct.pack("<I", length) + b"gnip" + bytes(8)])
                with self.assertLogs(qt._log, "WARNING") as logs:
                    session._main()
                self.assertEqual(["DT Hub: HD picture unavailable: ValueError: bad message length %d" % length],
                                 [r.getMessage() for r in logs.records])
                self.assertEqual("off", qt._picture.mode)

    def test_apres_la_premiere_image_une_panne_rend_displayservice(self):
        feed = fixture("asyn-feed")
        session = self.session([fixture("cvrp-request"), feed, struct.pack("<I", 4)])
        with self.assertLogs(qt._log, "WARNING") as logs:
            session._main()
        self.assertTrue(logs.records[-1].getMessage().startswith("DT Hub: HD picture ended"))
        self.assertEqual("off", qt._picture.mode)

    def test_apres_un_abandon_faute_d_image_cle_la_capture_s_arrete(self):
        log = []
        qt._picture = qt._Picture(clock=itertools.count(0.0, 3.0).__next__)
        qt._picture.subscribers = qt._Subscribers(qt._picture)
        qt._picture.subscribers[asyncio.Queue()] = SimpleNamespace(needs_key=True)
        feed = fixture("asyn-feed")
        device = SimpleNamespace()
        session = qt._Session(device, None, 0, FakeIn([fixture("cvrp-request"), feed, feed + feed]), FakeOut(log))
        qt._session = session
        try:
            # Deltas only: the viewer waits from the first one, and gives up at the next.
            with mock.patch.dict(sys.modules, fake_usb(log, [])), mock.patch.object(qt, "is_key", return_value=False), \
                    self.assertLogs(qt._log, "WARNING"):
                session.thread.start()
                session.thread.join(2)
        finally:
            qt._session = None
        self.assertFalse(session.thread.is_alive())
        self.assertEqual("off", qt._picture.mode)
        video = device_clock("cvrp-request")
        self.assertEqual([qt.need(video), qt.reply(message("cvrp-request"), video + 0x1000AF, 0), qt.need(video),
                          qt.hpd0(), ("release", device, 0)], log)

    def test_un_abandon_pendant_stop_n_ecrit_rien_apres(self):
        log = []
        session = qt._Session(SimpleNamespace(ctrl_transfer=lambda *a, **k: None), None, 0, FakeIn([]), FakeOut(log))
        with mock.patch.dict(sys.modules, fake_usb(log, [])):
            session.stop()
            log.clear()
            session.give_up()
            session._main()
        self.assertEqual([], log)


def fake_usb(log, devices, claim_error=None):
    """pyusb's modules as _open and stop use them, recording calls in log."""
    usb, core, util = types.ModuleType("usb"), types.ModuleType("usb.core"), types.ModuleType("usb.util")
    backend, libusb0 = types.ModuleType("usb.backend"), types.ModuleType("usb.backend.libusb0")
    usb.core, usb.util, usb.backend, backend.libusb0 = core, util, backend, libusb0
    core.USBError = type("USBError", (Exception,), {})
    core.find = lambda **_: iter(devices)
    libusb0.get_backend = lambda find_library: object()

    def claim_interface(device, number):
        log.append(("claim", device, number))
        if claim_error is not None:
            raise claim_error

    util.claim_interface = claim_interface
    util.release_interface = lambda device, number: log.append(("release", device, number))
    util.dispose_resources = lambda device: log.append(("dispose", device))
    return {"usb": usb, "usb.core": core, "usb.util": util, "usb.backend": backend, "usb.backend.libusb0": libusb0}


class Configuration(list):
    def __init__(self, value, interfaces):
        super().__init__(interfaces)
        self.bConfigurationValue = value


class UsbDevice(FakeDevice):
    def __init__(self, serial, configs, log):
        super().__init__(serial)
        self._configs = configs
        self.log = log

    def configurations(self):
        return self._configs

    def set_configuration(self, value):
        self.log.append(("set_configuration", value))

    def ctrl_transfer(self, *args, **_):
        self.log.append(("ctrl", args))


class UsbCleanup(unittest.TestCase):
    def test_un_echec_a_la_prise_de_l_interface_la_rend_et_libere_l_appareil(self):
        log = []
        usbmux = Configuration(4, [interface(0xFF, 0xFE, 1)])
        capture = Configuration(5, [interface(0xFF, 0xFE, 1), interface(0xFF, 0x2A, 2)])
        other = UsbDevice("00008030000A1B2C3D4E", [capture], log)
        wanted = SwitchingDevice("00008120001A2B3C4D5E", [usbmux, capture], log, shown=False)
        with mock.patch.dict(sys.modules, fake_usb(log, [other, wanted], claim_error=OSError("busy"))), \
                mock.patch.object(qt.time, "sleep"):
            with self.assertRaises(OSError):
                qt._open("00008120-001A2B3C4D5E", "libusb0.dll")
        self.assertIn(("dispose", other), log)
        self.assertEqual([("ctrl", (0x40, 0x52, 0x00, 0x02)), ("dispose", wanted), ("set_configuration", 5),
                          ("claim", wanted, 2), ("release", wanted, 2), ("dispose", wanted)],
                         [entry for entry in log if entry != ("dispose", other)])

    def test_l_arret_suit_l_ordre_de_quicktime_video_hack(self):
        log = []
        device = UsbDevice("00008120001A2B3C4D5E", [], log)
        session = qt._Session(device, Configuration(4, []), 2, FakeIn([]), FakeOut(log))
        session.thread = SimpleNamespace(is_alive=lambda: True, join=lambda timeout: None)
        session._released.set()  # the device's RELS
        with mock.patch.dict(sys.modules, fake_usb(log, [])):
            session.stop()
            session.stop()
        self.assertEqual([qt.hpd0(), ("ctrl", (0x40, 0x52, 0x00, 0x00)), ("release", device, 2),
                          ("set_configuration", 4), ("dispose", device)], log)

    def test_une_configuration_de_capture_restee_est_cachee_puis_reactivee(self):
        # A tool killed without its stop leaves the device mid-session in its capture configuration.
        log = []
        usbmux = Configuration(4, [interface(0xFF, 0xFE, 1)])
        capture = Configuration(5, [interface(0xFF, 0xFE, 1), interface(0xFF, 0x2A, 2)])
        device = SwitchingDevice("00008120001A2B3C4D5E", [usbmux, capture], log)
        with mock.patch.dict(sys.modules, fake_usb(log, [device], claim_error=OSError("stop here"))), \
                mock.patch.object(qt.time, "sleep"):
            with self.assertRaises(OSError):
                qt._open("00008120-001A2B3C4D5E", "libusb0.dll")
        self.assertEqual([("ctrl", (0x40, 0x52, 0x00, 0x00)), ("ctrl", (0x40, 0x52, 0x00, 0x02)),
                          ("set_configuration", 5)],
                         [entry for entry in log if entry[0] in ("ctrl", "set_configuration")])


class SwitchingDevice(UsbDevice):
    """Shows its capture configuration after the enable request, hides it after the hide request."""

    def __init__(self, serial, configs, log, shown=True):
        super().__init__(serial, configs, log)
        self._all = configs
        self._shown = shown

    def configurations(self):
        return self._all if self._shown else self._all[:1]

    def ctrl_transfer(self, *args, **kw):
        super().ctrl_transfer(*args, **kw)
        if args[:2] == (0x40, 0x52):
            self._shown = args[3] == 0x02


class EndOfCapture(unittest.TestCase):
    def test_une_config_displayservice_impossible_n_empeche_pas_le_retour(self):
        class State:
            needs_key = reset_on_key = False

            def mark_needs_key(self, now):
                self.needs_key = True

        def broken(*_):
            raise ValueError("bad SPS")

        asked = []
        picture = qt._Picture()
        picture.subscribers = qt._Subscribers(picture)
        picture.server = SimpleNamespace(_codec_string="hev1.1.6.L153.B0", _vps_nal=b"v", _sps_nal=b"s",
                                         _pps_nal=b"p", _request_recovery_idr=lambda reason: asked.append(reason))
        picture.set_format(qt.format_description(message("cvrp-request")))
        picture.deliver(b"K", True)
        states = [State(), State()]
        for state in states:
            picture.subscribers[asyncio.Queue()] = state

        async def end():
            picture.end()

        saved = qt._ss
        qt._ss = SimpleNamespace(hevc_decoder_configuration_record=broken)
        try:
            with self.assertLogs(qt._log, "WARNING"):
                asyncio.run(end())
        finally:
            qt._ss = saved
        self.assertEqual([(True, True), (True, True)], [(s.needs_key, s.reset_on_key) for s in states])
        self.assertEqual(["hd-ended"], asked)


if __name__ == "__main__":
    unittest.main()
