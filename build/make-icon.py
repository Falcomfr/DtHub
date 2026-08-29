#!/usr/bin/env python3
"""Genere assets/app.ico et assets/app.png.

Marque : un eventail de six oeufs colores, chacun d'une teinte differente.

La composition est produite par ce script, a partir de formes geometriques et
d'une palette choisie ici. Elle ne reprend aucune illustration existante :
ni motif, ni relief, ni ornement, ni teinte copiee. Six oeufs disposes en
eventail est une idee generique ; c'est le dessin qui appartient a son auteur,
et celui-ci est entierement calcule ci-dessous.

Aucune dependance externe : PNG ecrit a la main via zlib.
Executer depuis la racine du depot :  python3 build/make-icon.py
"""
import math
import os
import struct
import zlib

SS = 4  # supersampling

# Disposition de l'eventail : les oeufs rayonnent depuis un point situe sous
# l'image, chacun incline le long de son rayon. C'est cette inclinaison qui
# fait lire un eventail plutot qu'une rangee.
EGG_COUNT = 6
PIVOT = (0.5, 0.795)
RADIUS = 0.400
SPREAD_DEGREES = 52.0
EGG_HALF_WIDTH = 0.079
EGG_HALF_HEIGHT = 0.130
OUTLINE = 0.010

# Une teinte par oeuf, chaudes et froides alternees pour equilibrer
# l'eventail. Palette propre a ce projet.
EGG_HUES = (
    (0.94, 0.30, 0.28),  # corail
    (0.98, 0.64, 0.13),  # ambre
    (0.33, 0.80, 0.31),  # jade
    (0.10, 0.76, 0.70),  # turquoise
    (0.27, 0.45, 0.99),  # indigo
    (0.63, 0.33, 0.92),  # violet
)


def rounded_rect_sdf(x, y, cx, cy, hw, hh, r):
    """Distance signee a un rectangle arrondi (negatif = interieur)."""
    dx = abs(x - cx) - (hw - r)
    dy = abs(y - cy) - (hh - r)
    outside = math.hypot(max(dx, 0.0), max(dy, 0.0))
    inside = min(max(dx, dy), 0.0)
    return outside + inside - r


def egg_sdf(x, y, cx, cy, angle, hw, hh):
    """Distance approchee a un oeuf : une ellipse resserree vers le sommet."""
    # Repere local de l'oeuf.
    ca, sa = math.cos(-angle), math.sin(-angle)
    dx = (x - cx) * ca - (y - cy) * sa
    dy = (x - cx) * sa + (y - cy) * ca

    # Le haut de l'oeuf est plus etroit que le bas : c'est ce resserrement
    # qui distingue un oeuf d'une simple ellipse.
    taper = 1.0 - 0.22 * (-dy / hh)
    taper = max(0.45, taper)

    nx = dx / (hw * taper)
    ny = dy / hh

    distance = math.hypot(nx, ny) - 1.0

    # Remise a l'echelle approximative pour obtenir une distance en unites
    # d'image, suffisante pour un anticrenelage propre.
    return distance * min(hw, hh)


def over(dst, src):
    """Compositing alpha « source over » sur du RGBA non premultiplie."""
    sr, sg, sb, sa = src
    dr, dg, db, da = dst
    a = sa + da * (1 - sa)
    if a <= 0:
        return (0.0, 0.0, 0.0, 0.0)
    r = (sr * sa + dr * da * (1 - sa)) / a
    g = (sg * sa + dg * da * (1 - sa)) / a
    b = (sb * sa + db * da * (1 - sa)) / a
    return (r, g, b, a)


def coverage(d, softness=220.0):
    """Convertit une distance signee en couverture [0,1] (bord adouci)."""
    return max(0.0, min(1.0, 0.5 - d * softness))


def eggs():
    """Position, angle et teinte de chaque oeuf, du fond vers l'avant."""
    items = []
    half = (EGG_COUNT - 1) / 2.0

    for i in range(EGG_COUNT):
        # Angles repartis symetriquement de part et d'autre de la verticale.
        t = (i - half) / half
        angle = math.radians(SPREAD_DEGREES) * t

        cx = PIVOT[0] + RADIUS * math.sin(angle)
        cy = PIVOT[1] - RADIUS * math.cos(angle)

        # Les oeufs exterieurs sont legerement plus sombres : cela creuse
        # l'eventail sans avoir besoin d'ombre portee.
        depth = abs(t)
        items.append((cx, cy, angle, depth, EGG_HUES[i]))

    # Dessiner de l'exterieur vers le centre : l'oeuf central passe devant.
    items.sort(key=lambda e: -e[3])
    return items


def egg_color(hue, depth, local_y):
    """Teinte claire au sommet, pleine a la base, assombrie vers les bords."""
    top = tuple(hue[i] + (1.0 - hue[i]) * 0.34 for i in range(3))
    bottom = tuple(c * 0.78 for c in hue)

    t = max(0.0, min(1.0, local_y))
    base = tuple(top[i] + (bottom[i] - top[i]) * t for i in range(3))

    shade = 1.0 - 0.13 * depth
    return tuple(min(1.0, c * shade) for c in base)


EGGS = eggs()


def shade(u, v):
    """Couleur RGBA du pixel en coordonnees normalisees [0,1]."""
    px = (0.0, 0.0, 0.0, 0.0)

    # Fond : carre arrondi, degrade indigo vers bleu en diagonale.
    d = rounded_rect_sdf(u, v, 0.5, 0.5, 0.5, 0.5, 0.235)
    if d >= 0.5:
        return px

    # Fond sombre et neutre : ce sont les oeufs qui portent la couleur.
    t = max(0.0, min(1.0, (u + v) / 2))
    r = 0.10 + (0.16 - 0.10) * t
    g = 0.12 + (0.20 - 0.12) * t
    b = 0.21 + (0.35 - 0.21) * t
    px = over(px, (r, g, b, coverage(d)))

    for (cx, cy, angle, depth, hue) in EGGS:
        de = egg_sdf(u, v, cx, cy, angle, EGG_HALF_WIDTH, EGG_HALF_HEIGHT)

        if de > OUTLINE:
            continue

        # Contour sombre : il separe les oeufs qui se chevauchent.
        if de > -OUTLINE * 0.30:
            px = over(px, (0.07, 0.09, 0.16, coverage(de - OUTLINE, 340.0)))
            continue

        # Coordonnees locales, pour le degrade et le reflet.
        ca, sa = math.cos(-angle), math.sin(-angle)
        local_x = ((u - cx) * ca - (v - cy) * sa) / EGG_HALF_WIDTH
        local_y = ((u - cx) * sa + (v - cy) * ca) / EGG_HALF_HEIGHT

        cr, cg, cb = egg_color(hue, depth, (local_y + 1) / 2)
        px = over(px, (cr, cg, cb, coverage(de, 340.0)))

        # Reflet : une petite tache claire en haut a gauche suffit a donner
        # le poli d'une coquille.
        gx = (local_x + 0.34) / 0.34
        gy = (local_y + 0.42) / 0.30
        gloss = math.hypot(gx, gy) - 1.0
        if gloss < 0:
            px = over(px, (1.0, 1.0, 1.0, 0.42 * min(1.0, -gloss * 2.4)))

    return px


def render(size):
    """Rend une image RGBA de cote `size` avec supersampling."""
    n = size * SS
    rows = []
    for py in range(size):
        row = bytearray()
        for px_ in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(SS):
                for sx in range(SS):
                    u = (px_ * SS + sx + 0.5) / n
                    v = (py * SS + sy + 0.5) / n
                    r, g, b, a = shade(u, v)
                    acc[0] += r * a
                    acc[1] += g * a
                    acc[2] += b * a
                    acc[3] += a
            k = SS * SS
            a = acc[3] / k
            if a > 0:
                r, g, b = (acc[i] / k / a for i in range(3))
            else:
                r = g = b = 0.0
            row += bytes(
                (
                    int(round(max(0.0, min(1.0, r)) * 255)),
                    int(round(max(0.0, min(1.0, g)) * 255)),
                    int(round(max(0.0, min(1.0, b)) * 255)),
                    int(round(max(0.0, min(1.0, a)) * 255)),
                )
            )
        rows.append(bytes(row))
    return rows


def png(size, rows):
    """Encode des lignes RGBA en PNG (filtre 0)."""
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c))

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    assets = os.path.join(root, "assets")
    os.makedirs(assets, exist_ok=True)

    sizes = (16, 32, 48, 64, 128, 256)
    images = {s: png(s, render(s)) for s in sizes}

    with open(os.path.join(assets, "app.png"), "wb") as f:
        f.write(images[256])

    # ICONDIR + ICONDIRENTRY*n + charges utiles PNG.
    offset = 6 + 16 * len(sizes)
    header = struct.pack("<HHH", 0, 1, len(sizes))
    entries, blobs = b"", b""
    for s in sizes:
        data = images[s]
        entries += struct.pack(
            "<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(data), offset
        )
        blobs += data
        offset += len(data)
    with open(os.path.join(assets, "app.ico"), "wb") as f:
        f.write(header + entries + blobs)

    print(f"assets/app.ico ({len(header + entries + blobs)} octets), assets/app.png")


if __name__ == "__main__":
    main()
