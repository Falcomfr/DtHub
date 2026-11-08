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
# Named like the app's own requests, so Ankama sees who asks.
USER_AGENT = "DtHub-touch-ids (+https://github.com/Falcomfr/DtHub)"
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
    req = urllib.request.Request(URL, json.dumps(body).encode(), {"Content-Type": "application/json", "User-Agent": USER_AGENT})
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


def alternatives(objectives, quests, achievements):
    """Each objective of the table's achievements met by one quest among several ("Qf=556|Qf=566"): the Papycha
    pages of those quests, when they are two or more. Doing one rules the others out (rules A and B in the app)."""
    pages = collections.defaultdict(set)
    for page, ids in quests.items():
        for qid in ids:
            pages[qid].add(page)
    groups = set()
    for o in objectives.values():
        terms = (o["criterion"] or "").split("|")
        found = [re.fullmatch(r"Q[fF]=(\d+)", t.strip()) for t in terms]
        if o["achievementId"] not in achievements or len(terms) < 2 or not all(found):
            continue
        group = sorted({p for m in found for p in pages.get(int(m.group(1)), ())})
        if len(group) >= 2:
            groups.add(tuple(group))
    return [list(g) for g in sorted(groups)]


def objective_order(objectives):
    """Achievement -> game quest id -> the order of the first objective that names it."""
    order = collections.defaultdict(dict)
    for o in objectives.values():
        for qid in re.findall(r"Q[fF][=>](\d+)", o["criterion"] or ""):
            at = order[o["achievementId"]]
            at[int(qid)] = min(at.get(int(qid), o["order"]), o["order"])
    return order


def ranks(objectives, cat, quests, series, links):
    """Place of each quest in its achievement, 1 first: the game's objective order, which fills the links Papycha's
    prerequisites miss ("Le Fil d'Adrian", after "Dédale au pluriel", has none there). A quest no objective names
    keeps its place behind the one Papycha put before it. Achievements whose objectives name none are left out."""
    order = objective_order(objectives)
    members = collections.defaultdict(list)
    for r in sorted(cat["quests"], key=lambda r: (r["playOrder"] or 1e9, r["chainStep"] or 1e9, r["title"])):
        ach = series.get(r["successName"]) or links.get(r["id"])
        if ach:
            members[ach].append(r["id"])
    out = {}
    for ach, pages in members.items():
        game = [min((order[ach][q] for q in quests.get(p, []) if q in order[ach]), default=None) for p in pages]
        if all(g is None for g in game):
            continue
        keys, last = [], -1
        for i, g in enumerate(game):
            last = last if g is None else g
            keys.append((last, g is None, i))
        for n, i in enumerate(sorted(range(len(pages)), key=keys.__getitem__), 1):
            out[pages[i]] = n
    return dict(sorted(out.items()))


def needs(objectives, game_quests, cat, quests, series):
    """Prerequisites Papycha misses: a main-story quest (isMain) that follows another in its achievement's objectives,
    with no Papycha prerequisite in its series, needs that one. Side quests are left alone: an achievement lists them
    by level ("On recherche ..."), not as a chain."""
    order = objective_order(objectives)
    main = lambda p: any(game_quests.get(str(q), {}).get("isMain") for q in quests.get(p, []))
    out = {}
    for name, ach in series.items():
        rows = [r for r in cat["quests"] if r["successName"] == name]
        titles = {norm(r["title"]) for r in rows}
        rank = lambda r: min((order[ach][q] for q in quests.get(r["id"], []) if q in order[ach]), default=None)
        chain = sorted((r for r in rows if rank(r) is not None), key=rank)
        for before, r in zip(chain, chain[1:]):
            if (rank(before) < rank(r) and main(before["id"]) and main(r["id"])
                    and not any(norm(p) in titles for p in r["prerequisites"])):
                out[r["id"]] = [before["id"]]
    return dict(sorted(out.items()))


def whole(objectives, achievements, cat, quests, series, links):
    """Achievements of the series whose every objective is a quest (one of "Qf=A|Qf=B") paired with a page of that
    series: doing those quests earns them, so DT Hub may count their reward. Sorted ids."""
    pages = collections.defaultdict(set)
    for page, ids in quests.items():
        for qid in ids:
            pages[qid].add(page)
    members = collections.defaultdict(set)
    for r in cat["quests"]:
        ach = series.get(r["successName"]) or links.get(r["id"])
        if ach:
            members[ach].add(r["id"])
    out = []
    for ach in sorted(set(series.values())):
        objs = [objectives[str(i)] for i in achievements[str(ach)]["objectiveIds"] if str(i) in objectives]
        found = [[re.fullmatch(r"Q[fF]=(\d+)", t.strip()) for t in (o["criterion"] or "").split("|")] for o in objs]
        if objs and all(all(terms) and any(pages[int(m.group(1))] & members[ach] for m in terms) for terms in found):
            out.append(ach)
    return out


def links_of(objectives, cat, quests):
    """Game achievement of a quest Papycha files under no series."""
    q2a = collections.defaultdict(set)
    for o in objectives.values():
        for qid in re.findall(r"Q[fF][=>](\d+)", o["criterion"] or ""):
            q2a[int(qid)].add(o["achievementId"])
    links = {}
    for r in cat["quests"]:
        if r["successName"] or r["id"] not in quests:
            continue
        found = {a for qid in quests[r["id"]] for a in q2a.get(qid, ())}
        if found:
            links[r["id"]] = min(found)
    return dict(sorted(links.items()))


def derived(g, cat, hand, quests, dungeons, series):
    """The whole table once quests, dungeons and series are paired: everything else comes from the achievements."""
    # A hand pairing wins, and also covers a page the embedded catalog lacks (one Papycha's API added since).
    quests = {**quests, **{int(k): v for k, v in hand["quetes"].items()}}
    links = links_of(g["AchievementObjectives"], cat, quests)
    found = needs(g["AchievementObjectives"], g["Quests"], cat, quests, series)
    # Prerequisites Papycha misspells ("Chaklaplop" for "Chaklaplok l'Eau-qui-mord"), by hand.
    found.update({int(k): v for k, v in hand["besoins"].items()})
    return {
        "quests": dict(sorted(quests.items())),
        "dungeons": dict(sorted(dungeons.items())),
        "series": series,
        "links": links,
        "alternatives": alternatives(g["AchievementObjectives"], quests, set(series.values()) | set(links.values())),
        "ranks": ranks(g["AchievementObjectives"], cat, quests, series, links),
        "needs": dict(sorted(found.items())),
        "whole": whole(g["AchievementObjectives"], g["Achievements"], cat, quests, series, links),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dumps")
    ap.add_argument("--out", default=os.path.join(ROOT, "assets", "dofus-touch-ids.json"))
    # Keeps the table's quests, dungeons and series and recomputes the rest from touch-Quests-fr.json,
    # touch-Achievements-fr.json and touch-AchievementObjectives-fr.json: no Dungeons dump, no server.
    ap.add_argument("--series-only", action="store_true")
    args = ap.parse_args()

    cat = json.load(open(os.path.join(ROOT, "assets", "papycha-catalog.json"), encoding="utf-8"))
    hand = json.load(open(os.path.join(ROOT, "build", "touch-ids", "manuel.json"), encoding="utf-8"))
    if args.series_only:
        old = json.load(open(args.out, encoding="utf-8"))
        g = {c: json.load(open(f"{args.dumps}/touch-{c}-fr.json", encoding="utf-8")) for c in ["Quests", "Achievements", "AchievementObjectives"]}
        table = derived(g, cat, hand, {int(k): v for k, v in old["quests"].items()}, old["dungeons"], old["series"])
        with open(args.out, "w", encoding="utf-8", newline="\n") as f:
            json.dump(table, f, ensure_ascii=False, indent=1)
            f.write("\n")
        print(f"{len(table['quests'])} quests, {len(table['links'])} links, {len(table['ranks'])} ranks, "
              f"{len(table['needs'])} needs, {len(table['whole'])} whole")
        return
    refused = {norm_dungeon(t) for t in hand["refus"]}
    g = load_game(args.dumps)

    # Quests: hand file (in derived), else exact normalized name.
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
    series, missing_s, fuzzy = {}, [], []
    for name in sorted({r["successName"] for r in cat["quests"] if r["successName"]}):
        n = norm(name)
        if n in ach:
            series[name] = min(ach[n])
            continue
        best = max(ach, key=lambda k: difflib.SequenceMatcher(None, n, k).ratio())
        if difflib.SequenceMatcher(None, n, best).ratio() >= 0.75:
            series[name] = min(ach[best])
            fuzzy.append(f"{name} -> {series[name]}")
        else:
            missing_s.append(name)

    table = derived(g, cat, hand, quests, dungeons, series)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(table, f, ensure_ascii=False, indent=1)
        f.write("\n")

    print(f"{len(table['quests'])} quests, {len(dungeons)} dungeons, {len(series)} series, {len(table['links'])} links, "
          f"{len(table['alternatives'])} alternative groups")
    print("Unpaired quests:", missing_q)
    print("Unpaired dungeons:", missing_d)
    print("Unpaired series:", missing_s)
    # Approximate pairings, to check by hand against the achievement ids.
    print("Series paired by fuzzy match:", fuzzy)


if __name__ == "__main__":
    main()
