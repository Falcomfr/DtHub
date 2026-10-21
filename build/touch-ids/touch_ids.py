#!/usr/bin/env python3
"""Regenerates assets/dofus-touch-ids.json: Papycha pages -> DOFUS Touch ids.

Ids only (plus Papycha's own series names as keys): no game text leaves this
script. Run before a release, never from the app.

  python3 -I build/touch-ids/touch_ids.py --dumps <dir>   # files touch-<Class>-fr.json
  python3 -I build/touch-ids/touch_ids.py                 # reads the server

Server mode: one request at a time, 60 s apart, stopping at the first non-200
(Ankama blocks the IP otherwise). Papycha's public API is not read: the
embedded catalog is the base, a page it lacks stays French until it is added.
"""
import argparse, collections, difflib, json, os, re, sys, time, unicodedata, urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
URL = "https://dt-proxy-production-login.ankama-games.com/data/map?lang=fr"
CLASSES = ["Quests", "Dungeons", "Achievements", "AchievementObjectives"]


def norm(s):
    s = unicodedata.normalize("NFKD", (s or "").replace("’", "'").replace("œ", "oe").replace("Œ", "oe"))
    s = s.encode("ascii", "ignore").decode().lower()
    s = re.sub(r"\[[^\]]*\]|\([^)]*\)", " ", s)
    s = re.sub(r"^\s*(quete|quetes)\s*:?\s*", "", s)
    return re.sub(r"[^a-z0-9]+", " ", s).strip()


def norm_dungeon(s):
    s = unicodedata.normalize("NFKD", (s or "").replace("’", "'").replace("œ", "oe").replace("Œ", "oe"))
    s = s.encode("ascii", "ignore").decode().lower()
    s = re.sub(r"^(la |le |l')", "", re.sub(r"[^a-z0-9']+", " ", s).strip())
    return re.sub(r"[^a-z0-9]+", " ", s).strip()


def post(cls, ids=None):
    body = {"class": cls}
    if ids is not None:
        body["ids"] = ids
    req = urllib.request.Request(URL, json.dumps(body).encode(), {"Content-Type": "application/json"})
    with urllib.request.urlopen(req) as r:  # a non-200 raises: the run stops there
        return json.load(r)


def load_game(dumps):
    if dumps:
        g = {c: json.load(open(f"{dumps}/touch-{c}-fr.json", encoding="utf-8")) for c in CLASSES}
        g["keys"] = json.load(open(f"{dumps}/touch-keys-fr.json", encoding="utf-8"))
        return g
    g = {}
    for i, c in enumerate(CLASSES):
        if i:
            time.sleep(60)
        g[c] = post(c)
    time.sleep(60)
    g["keys"] = post("Items", sorted({d["dungeonKey"] for d in g["Dungeons"].values() if d.get("dungeonKey")}))
    return g


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dumps")
    ap.add_argument("--out", default=os.path.join(ROOT, "assets", "dofus-touch-ids.json"))
    args = ap.parse_args()

    cat = json.load(open(os.path.join(ROOT, "assets", "papycha-catalog.json"), encoding="utf-8"))
    hand = json.load(open(os.path.join(ROOT, "build", "touch-ids", "manuel.json"), encoding="utf-8"))
    refused = {norm_dungeon(t) for t in hand["refus"]}
    g = load_game(args.dumps)

    # Quests: hand file, else exact normalized name.
    byname = collections.defaultdict(list)
    for i, q in g["Quests"].items():
        byname[norm(q["nameId"])].append(int(i))
    quests, missing_q = {}, []
    for r in cat["quests"]:
        ids = hand["quetes"].get(str(r["id"])) or sorted(byname.get(norm(r["title"]), []))
        if ids:
            quests[r["id"]] = ids
        else:
            missing_q.append(r["title"])

    # Dungeons: hand file, else name (with "Donjon de..." stripped), else a key
    # that opens exactly one game dungeon. Refused titles never fall back on the key.
    dname, dkey = {}, collections.defaultdict(list)
    for i, d in g["Dungeons"].items():
        n = norm_dungeon(d["nameId"])
        dname.setdefault(n, int(i))
        for p in ("donjon des ", "donjon du ", "donjon de la ", "donjon de ", "donjon "):
            if n.startswith(p):
                dname.setdefault(n[len(p):], int(i))
        k = g["keys"].get(str(d.get("dungeonKey")))
        if k:
            dkey[norm_dungeon(k["nameId"])].append(int(i))
    dungeons, missing_d = {}, []
    for r in cat["dungeons"]:
        t = norm_dungeon(r["title"])
        if str(r["id"]) in hand["donjons"]:
            h = hand["donjons"][str(r["id"])]
            dungeons[r["id"]] = {"dungeons": h["dungeons"], "monster": h.get("monster", 0)}
            continue
        i = None
        if t not in refused:
            i = dname.get(t)
            if not i and len(dkey.get(norm_dungeon(r.get("key")), [])) == 1:
                i = dkey[norm_dungeon(r["key"])][0]
        if i:
            dungeons[r["id"]] = {"dungeons": [i], "monster": 0}
        else:
            missing_d.append(r["title"])

    # Series: exact normalized name, else the closest at ratio >= 0.75.
    ach = collections.defaultdict(list)
    for i, a in g["Achievements"].items():
        ach[norm(a["nameId"])].append(int(i))
    series, missing_s = {}, []
    for name in sorted({r["successName"] for r in cat["quests"] if r["successName"]}):
        n = norm(name)
        if n in ach:
            series[name] = min(ach[n])
            continue
        best = max(ach, key=lambda k: difflib.SequenceMatcher(None, n, k).ratio())
        if difflib.SequenceMatcher(None, n, best).ratio() >= 0.75:
            series[name] = min(ach[best])
        else:
            missing_s.append(name)

    # Links: game achievement of a quest Papycha files under no series.
    q2a = collections.defaultdict(set)
    for o in g["AchievementObjectives"].values():
        for qid in re.findall(r"Q[fF][=>](\d+)", o["criterion"] or ""):
            q2a[int(qid)].add(o["achievementId"])
    links = {}
    for r in cat["quests"]:
        if r["successName"] or r["id"] not in quests:
            continue
        found = {a for qid in quests[r["id"]] for a in q2a.get(qid, ())}
        if found:
            links[r["id"]] = min(found)

    table = {
        "quests": dict(sorted(quests.items())),
        "dungeons": dict(sorted(dungeons.items())),
        "series": series,
        "links": dict(sorted(links.items())),
    }
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(table, f, ensure_ascii=False, indent=1)
        f.write("\n")

    print(f"{len(quests)} quests, {len(dungeons)} dungeons, {len(series)} series, {len(links)} links")
    print("Unpaired quests:", missing_q)
    print("Unpaired dungeons:", missing_d)
    print("Unpaired series:", missing_s)


main()
