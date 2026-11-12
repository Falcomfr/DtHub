"""Checks dthub_quicktime against messages recorded from a real iPhone.

The fixtures come from quicktime_video_hack (MIT), see fixtures/README.md.
Run with the iPhone tool's Python, from the repository root:
    python -I -m unittest discover -s tests/quicktime -v
"""

import struct
import sys
import unittest
from pathlib import Path

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


if __name__ == "__main__":
    unittest.main()
