#!/usr/bin/env python3
"""Extracts from papycha.fr the "quest -> success" map embedded in DT Hub.

Why frozen data rather than a read at run time: a quest's success can only
be read from the intro block of its page, and reading the seven hundred
eighty-two pages costs thirteen megabytes. Doing this every week, on every
machine, would put a strain on their site for information that only
changes with the game's updates. So it is done once, here, and the result
is shipped: thirty-six kilobytes.

Two sources, and both are needed:

  - each quest's intro block, which covers 459 of them;
  - the category pages' subheadings, which the application already reads
    at every indexing pass, which cover 373 of them.

Their union covers 498. The site's own success list, however, announces
only 475 in total: the remaining 284 quests have no success, this is not
a gap in the measurement.

The two sources do not always agree on spelling: straight or curly
apostrophe, capitalization, accent. We therefore group on a reduced form
and keep the spelling from the official success list when it exists.

Usage:
    python3 build/extract-successes.py
"""

import gzip
import json
import re
import sys
import time
import unicodedata
import urllib.request
from html import unescape
from pathlib import Path

SITE = "https://papycha.fr"
API = f"{SITE}/wp-json/wp/v2"
CATEGORIE_QUETES = 7
SORTIE = Path(__file__).resolve().parent.parent / "assets" / "quest-successes.json"

EN_TETES = {
    "User-Agent": "DtHub/extraction (+https://github.com/Falcomfr/DtHub)",
}


def lire(url: str) -> bytes:
    return urllib.request.urlopen(
        urllib.request.Request(url, headers=EN_TETES), timeout=60
    ).read()


def json_de(url: str):
    return json.loads(lire(url).decode("utf-8"))


def texte(html: str) -> str:
    """Strips tags and escape sequences, including the ones the site
    leaves lying around: some pages carry an escaped apostrophe."""
    return unescape(re.sub("<[^>]+>", " ", html)).replace("\\'", "'").strip()


def reduire(nom: str) -> str:
    """Comparable form: without accent, without punctuation, without case."""
    sans = unicodedata.normalize("NFD", nom.lower())
    sans = "".join(c for c in sans if unicodedata.category(c) != "Mn")
    return re.sub(r"[^a-z0-9]+", " ", sans).strip()


def cle(url: str) -> str:
    return url.rstrip("/")


def fait(bloc: str, nom: str) -> str | None:
    trouve = re.search(
        rf"pqa-quest-intro__fact--{nom}\b.*?<dd[^>]*>(.*?)</dd>", bloc, re.S
    )
    return texte(trouve.group(1)) if trouve else None


def separer(brut: str, connus: dict[str, str]) -> list[str]:
    """Splits the successes of a quest that grants more than one.

    The site joins them with a comma and offers no structural landmark:
    "Agriculture et Alchimie, La maire dénie" names two, but "À l'ombre,
    depuis trop longtemps" and "Un piou, c'est tout !" name only one. Of
    the 459 blocks read, twenty-five carry a comma and only one actually
    joins two successes.

    So we only split if each piece is a success the site names elsewhere.
    Otherwise the comma is part of the name.
    """
    entier = brut.strip()

    if "," not in entier:
        return [entier] if entier else []

    morceaux = [part.strip() for part in entier.split(",") if part.strip()]

    if len(morceaux) > 1 and all(reduire(m) in connus for m in morceaux):
        return morceaux

    return [entier]


def noms_de(carte: dict[str, tuple[str, int]]) -> dict[str, str]:
    return {url: nom for url, (nom, _) in carte.items()}


def succes_officiels() -> dict[str, str]:
    """Successes named by the "Succès" ("Successes") page, to know which
    ones exist."""
    pages = json_de(f"{API}/pages?slug=succes&_fields=content")

    if not pages:
        return {}

    contenu = pages[0]["content"]["rendered"]
    officiels: dict[str, str] = {}

    for lien in re.finditer(
        r'<a class="pqt-success-list__item"[^>]*>(.*?)</a>', contenu, re.S
    ):
        brut = re.sub(r"<small>.*?</small>", "", lien.group(1), flags=re.S)
        nom = texte(brut)

        if nom:
            officiels.setdefault(reduire(nom), nom)

    return officiels


def sans_marque(titre: str) -> str:
    """Name of the quest that a prerequisite label designates.

    A milestone is not a quest but the state it leaves behind. The site
    used to write it "[FIN] L'essentiel est dans le Lac gelé"; it now
    writes "L'essentiel est dans le Lac gelé atteint" ("... reached"), and
    "Succès X réalisé" ("Success X achieved") for a success. Measured on
    the 584 items of the "précédents" ("previous") column: none carries
    brackets anymore, thirty-five end with "atteint" ("reached") and
    thirty-eight are of the form "Succès ... réalisé" ("Success ...
    achieved"). The prefix-based rule therefore no longer stripped
    anything, and twelve edges of the play-order graph were lost.
    """
    valeur = re.sub(r"^\[[^\]]*\]\s*", "", titre).strip()
    valeur = re.sub(r"^Succ[èe]s\s+(?P<nom>.+?)\s+r[ée]alis[ée]$", r"\g<nom>", valeur, flags=re.I)
    valeur = re.sub(r"\s+atteint(?:e)?$", "", valeur, flags=re.I)

    return valeur.strip()


def prerequis(contenu: str) -> list[tuple[str, str]]:
    """A quest's prerequisites: what must have been done before it.

    The site publishes them in the "précédents" ("previous") column of its
    progress block. Returns for each one the label as it is displayed and
    the name of the quest it designates, the latter used to order a
    success's quests and the former to show them to the user.
    """
    bloc = re.search(
        r'pqt-progress__column--previous\b(?P<corps>.*?)</section>', contenu, re.S
    )

    if not bloc:
        return []

    trouves = []

    for ancre in re.finditer(r"<a[^>]*>(?P<inner>.*?)</a>", bloc.group("corps"), re.S):
        fort = re.search(r"<strong[^>]*>(?P<t>.*?)</strong>", ancre.group("inner"), re.S)
        libelle = texte(fort.group("t") if fort else ancre.group("inner"))

        if libelle:
            trouves.append((libelle, sans_marque(libelle)))

    return trouves


def depuis_les_quetes(
    connus: dict[str, str],
    titres: dict[str, str],
    avant: dict[str, list[str]],
) -> dict[str, tuple[str, int]]:
    """Successes and chain rank, read from each quest's intro block.

    Along the way, fills <paramref name="titres"/> and <paramref
    name="avant"/>: each quest's title and the labels it depends on, which
    are used to order a success's quests.
    """
    carte: dict[str, tuple[str, int]] = {}

    for page in range(1, 20):
        adresse = (
            f"{API}/posts?categories={CATEGORIE_QUETES}&per_page=100&page={page}"
            "&_fields=link,title,content"
        )

        try:
            lot = json_de(adresse)
        except urllib.error.HTTPError as erreur:
            if erreur.code == 400:
                break
            raise

        if not lot:
            break

        for article in lot:
            contenu = article["content"]["rendered"]
            intro = re.search(r"pqa-quest-intro.*?</section>", contenu, re.S)

            if not intro:
                continue

            if brut := fait(intro.group(0), "successes"):
                if noms := separer(brut, connus):
                    rang = 0

                    # "Étape 6/7" ("Step 6/7") places the quest within its
                    # prerequisite chain, not within its success: the three
                    # quests of "De la caillasse plein les poches" are worth
                    # 1, 6 and 6 there. It is nonetheless the only play
                    # order the site publishes, and it is better than
                    # alphabetical order.
                    if etape := fait(intro.group(0), "step"):
                        if chiffre := re.search(r"(\d+)", etape):
                            rang = int(chiffre.group(1))

                    carte[cle(article["link"])] = (noms[0], rang)

            titres[cle(article["link"])] = texte(article["title"]["rendered"])
            avant[cle(article["link"])] = prerequis(contenu)

        print(f"  page {page} : {len(carte)} quêtes rattachées", file=sys.stderr)
        time.sleep(0.3)

    return carte


def depuis_les_rubriques() -> dict[str, str]:
    """Success read from the category pages' subheadings, the way the
    application does at every indexing pass. Used to fill gaps and
    cross-check."""
    racine = json_de(f"{API}/pages?slug=quetes&_fields=content")[0]["content"]["rendered"]
    tableau = re.search(r"<table.*?</table>", racine, re.S)

    if not tableau:
        return {}

    carte: dict[str, str] = {}

    for cellule in re.findall(r"<td[^>]*>.*?</td>", tableau.group(0), re.S):
        lien = re.search(r'href="([^"]+)"', cellule)

        if not lien:
            continue

        adresse = lien.group(1)

        if "page_id=" in adresse:
            identifiant = re.search(r"page_id=(\d+)", adresse).group(1)
            page = json_de(f"{API}/pages/{identifiant}?_fields=content")
        else:
            segment = [s for s in adresse.split("/") if s][-1]
            trouvees = json_de(f"{API}/pages?slug={segment}&_fields=content")
            page = trouvees[0] if trouvees else None

        if not page:
            continue

        contenu = page["content"]["rendered"]
        morceaux = re.split(r"<p[^>]*><strong>(.*?)</strong></p>", contenu, flags=re.S)

        for i in range(1, len(morceaux) - 1, 2):
            titre = texte(morceaux[i]).rstrip(": ").strip()
            succes = re.match(r"^\[\s*Succ[eè]s\s*\]\s*(?P<nom>.+)$", titre, re.I)

            if not succes:
                continue

            for url in re.findall(
                r'href="(https://papycha\.fr/[^"]+)"', morceaux[i + 1]
            ):
                carte.setdefault(cle(url), succes.group("nom").strip())

        time.sleep(0.15)

    return carte


def ordonner(
    carte: dict[str, dict],
    titres: dict[str, str],
    avant: dict[str, list[str]],
) -> dict[str, int]:
    """Sorts each success's quests into the order they are played in.

    The site publishes this order nowhere for most successes: neither the
    official list, nor the category pages when they do not head them. Only
    the prerequisites remain, which give a partial order: "Les rescapés de
    Frigost" requires "[FIN] L'essentiel est dans le Lac gelé", so the
    latter comes first. We complete it with the chain rank, then with the
    title, so that the order is total and always the same.
    """
    par_titre = {reduire(t): url for url, t in titres.items()}
    rangs: dict[str, int] = {}

    groupes: dict[str, list[str]] = {}

    for url, entree in carte.items():
        groupes.setdefault(entree["s"], []).append(url)

    for membres in groupes.values():
        dedans = set(membres)

        # Graph edges, restricted to the success: an outside prerequisite
        # says nothing about the internal order.
        requis = {
            url: {
                par_titre[reduire(nom)]
                for _, nom in avant.get(url, [])
                if reduire(nom) in par_titre and par_titre[reduire(nom)] in dedans
            }
            for url in membres
        }

        defaut = {
            url: (carte[url].get("n") or 10**6, titres.get(url, url))
            for url in membres
        }

        reste = set(membres)
        rang = 0

        while reste:
            # The quests whose internal prerequisites are all already
            # placed.
            prets = [u for u in reste if not (requis[u] & reste)]

            # A cycle must not block: we then take the best one remaining.
            if not prets:
                prets = list(reste)

            for url in sorted(prets, key=lambda u: defaut[u]):
                rang += 1
                rangs[url] = rang
                reste.discard(url)

    return rangs


def main() -> int:
    print("Orthographe officielle des succès…", file=sys.stderr)
    officiels = succes_officiels()
    print(f"  {len(officiels)} succès nommés", file=sys.stderr)

    print("Blocs d'intro des quêtes…", file=sys.stderr)
    titres: dict[str, str] = {}
    avant: dict[str, list[str]] = {}
    par_quete = depuis_les_quetes(officiels, titres, avant)

    print("Intertitres des pages de rubrique…", file=sys.stderr)
    par_rubrique = depuis_les_rubriques()

    # The intro block is authoritative: it is carried by the quest itself.
    # The subheadings fill in what it does not say.
    union = {url: (nom, 0) for url, nom in par_rubrique.items()}
    union.update(par_quete)

    # The two sources do not always write the same way: straight or curly
    # apostrophe, capitalization, accent. We group on the reduced form and
    # keep the spelling most common among the intro blocks, which is the
    # one the quest's page displays. The official list is used only to
    # break ties when the blocks do not settle it: it carries its own
    # typos, "Se mettre la Cité dor à dos" (missing the apostrophe in
    # "d'or") to name just one.
    graphies: dict[str, dict[str, int]] = {}

    for source, poids in ((noms_de(par_quete), 2), (par_rubrique, 1)):
        for nom in source.values():
            graphies.setdefault(reduire(nom), {})
            graphies[reduire(nom)][nom] = graphies[reduire(nom)].get(nom, 0) + poids

    def retenue(nom: str) -> str:
        variantes = graphies.get(reduire(nom))

        if not variantes:
            return officiels.get(reduire(nom), nom)

        return max(variantes.items(), key=lambda v: v[1])[0]

    carte = {
        url: {"s": retenue(nom), "n": rang}
        for url, (nom, rang) in sorted(union.items())
    }

    for url, rang in ordonner(carte, titres, avant).items():
        carte[url]["o"] = rang

    # The prerequisites, to show on hover in the list. They cover five
    # times more quests than the level the site provides: 527 through this
    # column against 117 levels. A quest can have some without belonging
    # to any success: it then enters the map for this reason alone.
    for url, items in sorted(avant.items()):
        libelles = [libelle for libelle, _ in items if libelle]

        if not libelles:
            continue

        carte.setdefault(url, {})["p"] = libelles

    SORTIE.parent.mkdir(parents=True, exist_ok=True)
    SORTIE.write_text(
        json.dumps(carte, ensure_ascii=False, indent=0, sort_keys=True) + "\n",
        encoding="utf-8",
    )

    distincts = len({reduire(v["s"]) for v in carte.values() if v.get("s")})
    avec_prerequis = sum(1 for v in carte.values() if v.get("p"))
    octets = SORTIE.stat().st_size

    print(file=sys.stderr)
    print(f"blocs d'intro      : {len(par_quete)}", file=sys.stderr)
    print(f"intertitres        : {len(par_rubrique)}", file=sys.stderr)
    print(f"union              : {len(carte)} quêtes, {distincts} succès", file=sys.stderr)
    print(f"avec prérequis     : {avec_prerequis}", file=sys.stderr)
    print(
        f"écrit              : {SORTIE} ({octets / 1024:.0f} Ko, "
        f"{len(gzip.compress(SORTIE.read_bytes())) / 1024:.0f} Ko compressé)",
        file=sys.stderr,
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
