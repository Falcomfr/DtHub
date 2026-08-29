#!/usr/bin/env python3
"""Genere assets/app.ico et assets/app.png (marque abstraite DT Hub).

Aucune dependance externe : PNG ecrit a la main via zlib.
Executer depuis la racine du depot :  python3 build/make-icon.py
"""
import math
import os
import struct
import zlib

SS = 4  # supersampling


def rounded_rect_sdf(x, y, cx, cy, hw, hh, r):
    """Distance signee a un rectangle arrondi (negatif = interieur)."""
    dx = abs(x - cx) - (hw - r)
    dy = abs(y - cy) - (hh - r)
    outside = math.hypot(max(dx, 0.0), max(dy, 0.0))
    inside = min(max(dx, dy), 0.0)
    return outside + inside - r


def over(dst, src):
    """Compositing alpha 'source over' sur du RGBA non premultiplie."""
    sr, sg, sb, sa = src
    dr, dg, db, da = dst
    a = sa + da * (1 - sa)
    if a <= 0:
        return (0.0, 0.0, 0.0, 0.0)
    r = (sr * sa + dr * da * (1 - sa)) / a
    g = (sg * sa + dg * da * (1 - sa)) / a
    b = (sb * sa + db * da * (1 - sa)) / a
    return (r, g, b, a)


def shade(u, v):
    """Couleur RGBA du pixel en coordonnees normalisees [0,1]."""
    px = (0.0, 0.0, 0.0, 0.0)

    # Fond : carre arrondi, degrade indigo -> bleu en diagonale.
    d = rounded_rect_sdf(u, v, 0.5, 0.5, 0.5, 0.5, 0.235)
    if d < 0.5:
        t = max(0.0, min(1.0, (u + v) / 2))
        r = 0.29 + (0.20 - 0.29) * t
        g = 0.31 + (0.51 - 0.31) * t
        b = 0.85 + (0.96 - 0.85) * t
        px = over(px, (r, g, b, coverage(d)))

    # Deux ecrans empiles (metaphore STACK), decales en diagonale.
    for (ox, oy, alpha) in ((-0.075, -0.075, 0.45), (0.055, 0.055, 1.0)):
        cx, cy = 0.5 + ox, 0.5 + oy
        d = rounded_rect_sdf(u, v, cx, cy, 0.155, 0.255, 0.045)
        if d < 0.5:
            px = over(px, (1.0, 1.0, 1.0, alpha * coverage(d)))
            # Evidement de l'ecran du dessus pour lui donner du relief.
            if alpha == 1.0:
                di = rounded_rect_sdf(u, v, cx, cy, 0.115, 0.205, 0.025)
                if di < 0.5:
                    px = over(px, (0.22, 0.35, 0.90, coverage(di)))
    return px


def coverage(d):
    """Convertit une distance signee en couverture [0,1] (bord adouci)."""
    return max(0.0, min(1.0, 0.5 - d * 220))


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
