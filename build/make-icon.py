#!/usr/bin/env python3
"""Genere assets/app.ico et assets/app.png.

Marque : un telephone a gauche, et deux cadres de fenetre qui en sortent vers
la droite. C'est litteralement ce que fait l'application : un telephone qui
alimente plusieurs fenetres sur le PC.

Aucune reference au jeu. Un oeuf serait l'embleme de DOFUS, donc le motif le
plus expose : le dessin ne doit rien evoquer d'officiel. Ici il n'y a que des
rectangles arrondis, calcules ci-dessous, et une teinte choisie ici.

Le dessin reste lisible a seize pixels : trois formes seulement, bien
separees, sans detail interieur.

Aucune dependance externe : PNG ecrit a la main via zlib.
Executer depuis la racine du depot :  python3 build/make-icon.py
"""
import math
import os
import struct
import zlib

SS = 4  # supersampling

# Le telephone occupe le tiers gauche, les fenetres les deux tiers droits.
# Les proportions sont genereuses : a seize pixels, un trait fin disparait.
PHONE = (0.285, 0.500, 0.105, 0.255)   # centre x, centre y, demi-largeur, demi-hauteur
PHONE_RADIUS = 0.045
PHONE_BORDER = 0.030

# L'ecouteur : sans lui, le telephone se lit comme une troisieme fenetre.
# Il disparait proprement a seize pixels, ou il ne resterait qu'une tache.
SPEAKER = (0.285, 0.318, 0.038, 0.010)
SPEAKER_RADIUS = 0.010

WINDOWS = (
    (0.660, 0.335, 0.150, 0.115),      # fenetre du haut
    (0.660, 0.665, 0.150, 0.115),      # fenetre du bas
)
WINDOW_RADIUS = 0.032
WINDOW_BORDER = 0.028

ACCENT = (0.10, 0.76, 0.70)            # turquoise
MUTED = (0.42, 0.52, 0.62)             # gris bleute, pour la fenetre d'arriere-plan


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


def frame(px, u, v, cx, cy, hw, hh, radius, border, color):
    """Cadre arrondi : un contour plein, un interieur vide."""
    outer = rounded_rect_sdf(u, v, cx, cy, hw, hh, radius)
    inner = rounded_rect_sdf(u, v, cx, cy, hw - border, hh - border, max(0.004, radius - border))

    # L'anneau est la ou l'on est dans le contour exterieur mais hors du vide.
    ring = min(coverage(outer), 1.0 - coverage(inner))

    if ring <= 0.0:
        return px

    return over(px, (color[0], color[1], color[2], ring))


def shade(u, v):
    """Couleur RGBA du pixel en coordonnees normalisees [0,1]."""
    px = (0.0, 0.0, 0.0, 0.0)

    # Fond : carre arrondi, degrade sombre en diagonale.
    d = rounded_rect_sdf(u, v, 0.5, 0.5, 0.5, 0.5, 0.235)
    if d >= 0.5:
        return px

    t = max(0.0, min(1.0, (u + v) / 2))
    r = 0.10 + (0.16 - 0.10) * t
    g = 0.12 + (0.20 - 0.12) * t
    b = 0.21 + (0.35 - 0.21) * t
    px = over(px, (r, g, b, coverage(d)))

    # La fenetre du bas passe derriere, en gris : elle donne la profondeur
    # sans encombrer le dessin.
    cx, cy, hw, hh = WINDOWS[1]
    px = frame(px, u, v, cx, cy, hw, hh, WINDOW_RADIUS, WINDOW_BORDER, MUTED)

    # La fenetre du haut est celle qui compte, donc en couleur d'accent.
    cx, cy, hw, hh = WINDOWS[0]
    px = frame(px, u, v, cx, cy, hw, hh, WINDOW_RADIUS, WINDOW_BORDER, ACCENT)

    # Le telephone, devant tout le reste.
    cx, cy, hw, hh = PHONE
    px = frame(px, u, v, cx, cy, hw, hh, PHONE_RADIUS, PHONE_BORDER, ACCENT)

    cx, cy, hw, hh = SPEAKER
    d = rounded_rect_sdf(u, v, cx, cy, hw, hh, SPEAKER_RADIUS)
    px = over(px, (ACCENT[0], ACCENT[1], ACCENT[2], coverage(d)))

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
