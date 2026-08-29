#!/usr/bin/env python3
"""Genere trois propositions d'icone dans build/icon-*.png.

Outil de choix uniquement : la variante retenue sera recopiee dans
build/make-icon.py, qui reste le generateur officiel.
"""
import math
import os
import struct
import sys
import zlib

SS = 4


def rounded_rect_sdf(x, y, cx, cy, hw, hh, r):
    dx = abs(x - cx) - (hw - r)
    dy = abs(y - cy) - (hh - r)
    return math.hypot(max(dx, 0.0), max(dy, 0.0)) + min(max(dx, dy), 0.0) - r


def egg_sdf(x, y, cx, cy, angle, hw, hh):
    ca, sa = math.cos(-angle), math.sin(-angle)
    dx = (x - cx) * ca - (y - cy) * sa
    dy = (x - cx) * sa + (y - cy) * ca
    taper = max(0.45, 1.0 - 0.22 * (-dy / hh))
    return (math.hypot(dx / (hw * taper), dy / hh) - 1.0) * min(hw, hh)


def over(dst, src):
    sr, sg, sb, sa = src
    dr, dg, db, da = dst
    a = sa + da * (1 - sa)
    if a <= 0:
        return (0.0, 0.0, 0.0, 0.0)
    return tuple((s * sa + d * da * (1 - sa)) / a for s, d in ((sr, dr), (sg, dg), (sb, db))) + (a,)


def cov(d, k=260.0):
    return max(0.0, min(1.0, 0.5 - d * k))


def egg_pixel(px, u, v, cx, cy, angle, hw, hh, hue, outline=0.011, gloss=True):
    d = egg_sdf(u, v, cx, cy, angle, hw, hh)
    if d > outline:
        return px

    if d > -outline * 0.30:
        return over(px, (0.07, 0.09, 0.16, cov(d - outline, 340.0)))

    ca, sa = math.cos(-angle), math.sin(-angle)
    lx = ((u - cx) * ca - (v - cy) * sa) / hw
    ly = ((u - cx) * sa + (v - cy) * ca) / hh

    top = tuple(hue[i] + (1.0 - hue[i]) * 0.34 for i in range(3))
    bottom = tuple(c * 0.78 for c in hue)
    t = max(0.0, min(1.0, (ly + 1) / 2))
    col = tuple(top[i] + (bottom[i] - top[i]) * t for i in range(3))

    px = over(px, col + (cov(d, 340.0),))

    if gloss:
        g = math.hypot((lx + 0.34) / 0.34, (ly + 0.42) / 0.30) - 1.0
        if g < 0:
            px = over(px, (1.0, 1.0, 1.0, 0.45 * min(1.0, -g * 2.4)))

    return px


def background(u, v, dark):
    d = rounded_rect_sdf(u, v, 0.5, 0.5, 0.5, 0.5, 0.235)
    if d >= 0.5:
        return (0.0, 0.0, 0.0, 0.0)
    t = max(0.0, min(1.0, (u + v) / 2))
    if dark:
        c = (0.10 + 0.06 * t, 0.12 + 0.08 * t, 0.21 + 0.14 * t)
    else:
        c = (0.24 - 0.07 * t, 0.27 + 0.18 * t, 0.78 + 0.14 * t)
    return over((0.0, 0.0, 0.0, 0.0), c + (cov(d, 220.0),))


TURQUOISE = (0.10, 0.76, 0.70)
AMBER = (0.98, 0.64, 0.13)
FAN = ((0.94, 0.30, 0.28), (0.98, 0.64, 0.13), (0.33, 0.80, 0.31),
       (0.10, 0.76, 0.70), (0.27, 0.45, 0.99), (0.63, 0.33, 0.92))


def variant_single(u, v):
    """Un seul oeuf, grand et centre."""
    px = background(u, v, dark=True)
    return egg_pixel(px, u, v, 0.5, 0.52, 0.0, 0.215, 0.315, TURQUOISE, outline=0.014)


def variant_trio(u, v):
    """Un oeuf en avant, deux en retrait : la multiplicite sans la surcharge."""
    px = background(u, v, dark=True)
    px = egg_pixel(px, u, v, 0.255, 0.545, math.radians(-24), 0.135, 0.200, (0.63, 0.33, 0.92))
    px = egg_pixel(px, u, v, 0.745, 0.545, math.radians(24), 0.135, 0.200, (0.33, 0.80, 0.31))
    return egg_pixel(px, u, v, 0.5, 0.495, 0.0, 0.170, 0.250, AMBER, outline=0.013)


def variant_fan(u, v):
    """L'eventail de six, resserre et plus grand."""
    px = background(u, v, dark=True)
    pivot, radius, spread = (0.5, 0.80), 0.395, 52.0
    items = []
    for i in range(6):
        t = (i - 2.5) / 2.5
        a = math.radians(spread) * t
        items.append((pivot[0] + radius * math.sin(a), pivot[1] - radius * math.cos(a), a, abs(t), FAN[i]))
    for cx, cy, a, _, hue in sorted(items, key=lambda e: -e[3]):
        px = egg_pixel(px, u, v, cx, cy, a, 0.079, 0.130, hue)
    return px


VARIANTS = {"single": variant_single, "trio": variant_trio, "fan": variant_fan}


def render(size, shade):
    n = size * SS
    rows = []
    for py in range(size):
        row = bytearray()
        for px_ in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(SS):
                for sx in range(SS):
                    r, g, b, a = shade((px_ * SS + sx + 0.5) / n, (py * SS + sy + 0.5) / n)
                    acc[0] += r * a
                    acc[1] += g * a
                    acc[2] += b * a
                    acc[3] += a
            k = SS * SS
            a = acc[3] / k
            r, g, b = (acc[i] / k / a for i in range(3)) if a > 0 else (0.0, 0.0, 0.0)
            row += bytes(int(round(max(0.0, min(1.0, c)) * 255)) for c in (r, g, b, a))
        rows.append(bytes(row))
    return rows


def png(size, rows):
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    for name, shade in VARIANTS.items():
        path = os.path.join(root, "build", f"icon-{name}.png")
        with open(path, "wb") as f:
            f.write(png(256, render(256, shade)))
        print(path)


if __name__ == "__main__":
    sys.exit(main())
