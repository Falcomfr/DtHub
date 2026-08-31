#!/usr/bin/env python3
"""Extrait de papycha.fr la carte « quête -> succès » embarquée dans DT Hub.

Pourquoi une donnée figée plutôt qu'une lecture à l'exécution : le succès d'une
quête n'est lisible que dans le bloc d'intro de sa page, et lire les sept cent
quatre-vingt-deux pages coûte treize mégaoctets. Le faire chaque semaine, sur
chaque poste, pèserait sur leur site pour une information qui ne bouge qu'aux
mises à jour du jeu. On le fait donc une fois, ici, et on livre le résultat :
trente-six kilooctets.

Deux sources, et il en faut deux :

  - le bloc d'intro de chaque quête, qui en couvre 459 ;
  - les intertitres des pages de rubrique, que l'application lit déjà à chaque
    indexation, qui en couvrent 373.

Leur union en couvre 498. La liste de succès du site, elle, n'en annonce que
475 au total : les 284 quêtes restantes n'ont pas de succès, ce n'est pas une
lacune de la mesure.

Les deux sources ne s'accordent pas toujours sur l'orthographe : apostrophe
droite ou courbe, majuscule, accent. On regroupe donc sur une forme réduite et
on retient l'orthographe de la liste officielle des succès quand elle existe.

Usage :
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
    "User-Agent": "DtHub/extraction (+https://github.com/falcom/dthub)",
}


def lire(url: str) -> bytes:
    return urllib.request.urlopen(
        urllib.request.Request(url, headers=EN_TETES), timeout=60
    ).read()


def json_de(url: str):
    return json.loads(lire(url).decode("utf-8"))


def texte(html: str) -> str:
    """Retire les balises et les échappements, y compris ceux que le site
    laisse traîner : certaines pages portent une apostrophe échappée."""
    return unescape(re.sub("<[^>]+>", " ", html)).replace("\\'", "'").strip()


def reduire(nom: str) -> str:
    """Forme comparable : sans accent, sans ponctuation, sans casse."""
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
    """Sépare les succès d'une quête qui en relève de plusieurs.

    Le site les joint par une virgule et n'offre aucun repère structurel :
    « Agriculture et Alchimie, La maire dénie » en désigne deux, mais
    « À l'ombre, depuis trop longtemps » et « Un piou, c'est tout ! » n'en
    désignent qu'un. Sur les 459 blocs lus, vingt-cinq portent une virgule et
    un seul joint réellement deux succès.

    On ne coupe donc que si chaque morceau est un succès que le site nomme
    ailleurs. Sinon la virgule fait partie du nom.
    """
    entier = brut.strip()

    if "," not in entier:
        return [entier] if entier else []

    morceaux = [part.strip() for part in entier.split(",") if part.strip()]

    if len(morceaux) > 1 and all(reduire(m) in connus for m in morceaux):
        return morceaux

    return [entier]


def succes_officiels() -> dict[str, str]:
    """Succès nommés par la page « Succès », pour savoir lesquels existent."""
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


def depuis_les_quetes(connus: dict[str, str]) -> dict[str, str]:
    """Succès lu dans le bloc d'intro de chaque quête."""
    carte: dict[str, str] = {}

    for page in range(1, 20):
        adresse = (
            f"{API}/posts?categories={CATEGORIE_QUETES}&per_page=100&page={page}"
            "&_fields=link,content"
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
                    carte[cle(article["link"])] = noms[0]

        print(f"  page {page} : {len(carte)} quêtes rattachées", file=sys.stderr)
        time.sleep(0.3)

    return carte


def depuis_les_rubriques() -> dict[str, str]:
    """Succès lu sur les intertitres des pages de rubrique, comme le fait
    l'application à chaque indexation. Sert à combler et à recouper."""
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


def main() -> int:
    print("Orthographe officielle des succès…", file=sys.stderr)
    officiels = succes_officiels()
    print(f"  {len(officiels)} succès nommés", file=sys.stderr)

    print("Blocs d'intro des quêtes…", file=sys.stderr)
    par_quete = depuis_les_quetes(officiels)

    print("Intertitres des pages de rubrique…", file=sys.stderr)
    par_rubrique = depuis_les_rubriques()

    # Le bloc d'intro fait foi : il est porté par la quête elle-même. Les
    # intertitres complètent ce qu'il ne dit pas.
    union = dict(par_rubrique)
    union.update(par_quete)

    # Les deux sources n'écrivent pas toujours pareil : apostrophe droite ou
    # courbe, majuscule, accent. On regroupe sur la forme réduite et on retient
    # l'orthographe la plus répandue parmi les blocs d'intro, qui est celle que
    # la page de la quête affiche. La liste officielle sert seulement à
    # départager quand les blocs ne tranchent pas : elle porte ses propres
    # coquilles, « Se mettre la Cité dor à dos » pour n'en citer qu'une.
    graphies: dict[str, dict[str, int]] = {}

    for source, poids in ((par_quete, 2), (par_rubrique, 1)):
        for nom in source.values():
            graphies.setdefault(reduire(nom), {})
            graphies[reduire(nom)][nom] = graphies[reduire(nom)].get(nom, 0) + poids

    def retenue(nom: str) -> str:
        variantes = graphies.get(reduire(nom))

        if not variantes:
            return officiels.get(reduire(nom), nom)

        return max(variantes.items(), key=lambda v: v[1])[0]

    carte = {url: retenue(nom) for url, nom in sorted(union.items())}

    SORTIE.parent.mkdir(parents=True, exist_ok=True)
    SORTIE.write_text(
        json.dumps(carte, ensure_ascii=False, indent=0, sort_keys=True) + "\n",
        encoding="utf-8",
    )

    distincts = len({reduire(n) for n in carte.values()})
    octets = SORTIE.stat().st_size

    print(file=sys.stderr)
    print(f"blocs d'intro      : {len(par_quete)}", file=sys.stderr)
    print(f"intertitres        : {len(par_rubrique)}", file=sys.stderr)
    print(f"union              : {len(carte)} quêtes, {distincts} succès", file=sys.stderr)
    print(
        f"écrit              : {SORTIE} ({octets / 1024:.0f} Ko, "
        f"{len(gzip.compress(SORTIE.read_bytes())) / 1024:.0f} Ko compressé)",
        file=sys.stderr,
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
