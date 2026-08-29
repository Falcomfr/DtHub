#!/usr/bin/env python3
"""Genere assets/app.ico et assets/app.png.

Marque : un oeuf unique, grand et centre, sur fond bleu nuit.

La composition est produite par ce script, a partir de formes geometriques et
d'une couleur choisie ici. Elle ne reprend aucune illustration existante :
ni motif, ni relief, ni ornement, ni teinte copiee. Un oeuf est une forme
generique ; c'est le dessin qui appartient a son auteur, et celui-ci est
entierement calcule ci-dessous.

Un seul oeuf plutot que six : l'icone reste lisible a seize pixels, dans la
barre des taches comme dans le menu Demarrer.

Aucune dependance externe : PNG ecrit a la main via zlib.
Executer depuis la racine du depot :  python3 build/make-icon.py
"""
import math
import os
import struct
import zlib

SS = 4  # supersampling

# L'oeuf occupe l'essentiel de l'image : c'est ce qui le rend reconnaissable
# une fois reduit a seize pixels.
EGG_CENTER = (0.5, 0.52)
EGG_HALF_WIDTH = 0.215
EGG_HALF_HEIGHT = 0.315
OUTLINE = 0.014

EGG_HUE = (0.10, 0.76, 0.70)  # turquoise


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


def egg_color(hue, local_y):
    """Teinte claire au sommet, pleine a la base."""
    top = tuple(hue[i] + (1.0 - hue[i]) * 0.34 for i in range(3))
    bottom = tuple(c * 0.78 for c in hue)

    t = max(0.0, min(1.0, local_y))
    return tuple(top[i] + (bottom[i] - top[i]) * t for i in range(3))


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

    cx, cy = EGG_CENTER
    de = egg_sdf(u, v, cx, cy, 0.0, EGG_HALF_WIDTH, EGG_HALF_HEIGHT)

    if de > OUTLINE:
        return px

    # Contour sombre : il detache l'oeuf du fond, y compris en tres petit.
    if de > -OUTLINE * 0.30:
        return over(px, (0.07, 0.09, 0.16, coverage(de - OUTLINE, 340.0)))

    local_x = (u - cx) / EGG_HALF_WIDTH
    local_y = (v - cy) / EGG_HALF_HEIGHT

    cr, cg, cb = egg_color(EGG_HUE, (local_y + 1) / 2)
    px = over(px, (cr, cg, cb, coverage(de, 340.0)))

    # Reflet : une tache claire en haut a gauche donne le poli d'une coquille.
    gloss = math.hypot((local_x + 0.34) / 0.34, (local_y + 0.42) / 0.30) - 1.0
    if gloss < 0:
        px = over(px, (1.0, 1.0, 1.0, 0.45 * min(1.0, -gloss * 2.4)))

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
