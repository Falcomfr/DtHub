#!/usr/bin/env python3
"""Generates assets/app.ico and assets/app.png.

Mark: the passage rune. A central ring, two smaller concentric circles,
and eight curved blades arranged in a crown. The motif is entirely
geometric, computed below from about fifteen numbers. No Ankama asset is
used, copied, or imitated stroke for stroke: only curves and circles, in
two shades of yellow.

The background is transparent. The icon therefore sits well both on a
dark taskbar and on a light file explorer.

No external dependency: rasterizer, gradients and PNG are written here,
zlib aside. Run from the repository root:

    python3 build/make-icon.py
"""
import math
import os
import struct
import zlib

# --- Geometry, in coordinates of a 256 square -------------------------------

CANVAS = 256.0
CENTER = 128.0

# The three circles, from largest to smallest. The first is dark, the
# second light, the third dark: it is this alternation that reads as
# three rings where there are only stacked discs.
C_OUT, C_MID, C_IN = 67.24, 32.76, 14.66

# The blades start beyond the large circle and reach the edge of the
# square.
R_IN, R_OUT = 70.69, 125.0
BLADE_HALF_ANGLE = 13.0     # half-width at the base, in degrees
BLADE_BEND = 13.0           # tilt of the tip toward the left
BLADE_HOOK = 1.1            # depth of the notch under the tip

INK = (0x33 / 255, 0x28 / 255, 0x0a / 255)
INK_ALPHA = 0.65
BLADE_STROKE = 2.4
CIRCLE_STROKE = 2.6
SHADOW_OFFSET = (1.4, 2.0)
SHADOW_ALPHA = 0.25

# Light yellow: diagonal linear gradient. Dark yellow: radial gradient
# offset toward the upper left, which gives the embossed look of struck
# coins.
LIGHT_STOPS = ((0.0, (0xff, 0xef, 0xb4)), (0.5, (0xef, 0xd5, 0x77)), (1.0, (0xc4, 0xa6, 0x3f)))
LIGHT_AXIS = (0.2, 0.1, 0.8, 0.95)
DARK_STOPS = ((0.0, (0xa6, 0x8e, 0x33)), (0.55, (0x83, 0x6d, 0x1e)), (1.0, (0x54, 0x43, 0x12)))
DARK_FOCUS = (0.38, 0.32, 0.76)

# Diagonal veil laid over the whole: light at the upper left, dark at the
# lower right. It is what gives the unity of lighting between blades and
# circles.
SHADE_AXIS = (0.2, 0.0, 0.85, 1.0)

GRAIN_ALPHA = 0.17          # ignored below GRAIN_MIN_SIZE
GRAIN_MIN_SIZE = 64


def polar(radius, angle_deg):
    """Point on the circle, angle counted from the top and increasing to
    the right."""
    angle = math.radians(angle_deg - 90.0)
    return CENTER + radius * math.cos(angle), CENTER + radius * math.sin(angle)


def blade(angle_deg):
    """A blade: two cubic curves back to back.

    The leading edge is convex, the underside concave, the tip points to
    the left. It is the profile of a beak, and it is what distinguishes
    the crown from a plain eight-pointed star.
    """
    length = R_OUT - R_IN
    half, bend = BLADE_HALF_ANGLE, BLADE_BEND

    base_left = polar(R_IN, angle_deg - half)
    base_right = polar(R_IN, angle_deg + half)
    tip = polar(R_OUT, angle_deg - bend)

    front_1 = polar(R_IN + length * 0.50, angle_deg - half * 1.7 - bend * 0.20)
    front_2 = polar(R_OUT - length * 0.10, angle_deg - bend * 0.80)
    back_1 = polar(R_OUT - length * 0.42, angle_deg - bend * 0.10 + half * 0.5 * BLADE_HOOK)
    back_2 = polar(R_IN + length * 0.32, angle_deg + half * 0.95)

    return [(base_left, front_1, front_2, tip), (tip, back_1, back_2, base_right)]


def flatten(curves, steps=28):
    """Turns a sequence of cubics into a closed polygon."""
    points = []
    for p0, p1, p2, p3 in curves:
        for i in range(steps):
            t = i / steps
            u = 1.0 - t
            a, b, c, d = u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t
            points.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0],
                           a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return points


BLADES = [flatten(blade(i * 45.0)) for i in range(8)]
# Every other blade is light: this is the alternation seen in the game.
BLADE_IS_LIGHT = [i % 2 == 0 for i in range(8)]


# --- Rasterizer ---------------------------------------------------------------

def bounds(points):
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return min(xs), min(ys), max(xs), max(ys)


def fill_polygon(width, polygon, scale, samples=4):
    """[0,1] coverage of a polygon, by scanline sweep.

    The horizontal is exact, the vertical sampled: it is the best
    quality-to-compute-time ratio when everything is written in Python.
    """
    cover = [0.0] * (width * width)
    pts = [(x * scale, y * scale) for x, y in polygon]
    edges = []
    for i, (x0, y0) in enumerate(pts):
        x1, y1 = pts[(i + 1) % len(pts)]
        if y0 != y1:
            edges.append((x0, y0, x1, y1))
    if not edges:
        return cover

    _, top, _, bottom = bounds(pts)
    first = max(0, int(math.floor(top)))
    last = min(width - 1, int(math.ceil(bottom)))
    weight = 1.0 / samples

    for row in range(first, last + 1):
        diff = [0.0] * (width + 2)
        line = cover[row * width:(row + 1) * width]
        touched = False
        for sub in range(samples):
            y = row + (sub + 0.5) / samples
            crossings = []
            for x0, y0, x1, y1 in edges:
                if (y0 <= y < y1) or (y1 <= y < y0):
                    crossings.append(x0 + (y - y0) * (x1 - x0) / (y1 - y0))
            if len(crossings) < 2:
                continue
            crossings.sort()
            for i in range(0, len(crossings) - 1, 2):
                left, right = crossings[i], crossings[i + 1]
                left = max(0.0, left)
                right = min(float(width), right)
                if right <= left:
                    continue
                touched = True
                i0, i1 = int(left), min(int(right), width - 1)
                if i0 == i1:
                    line[i0] += (right - left) * weight
                    continue
                line[i0] += (i0 + 1 - left) * weight
                line[i1] += (right - i1) * weight
                diff[i0 + 1] += weight
                diff[i1] -= weight
        if not touched:
            continue
        running = 0.0
        for i in range(width):
            running += diff[i]
            value = line[i] + running
            line[i] = value if value < 1.0 else 1.0
        cover[row * width:(row + 1) * width] = line
    return cover


def fill_disc(width, radius, scale):
    """Coverage of a disc, computed by distance: a perfectly smooth edge."""
    cover = [0.0] * (width * width)
    cx = cy = CENTER * scale
    r = radius * scale
    first = max(0, int(cy - r - 2))
    last = min(width - 1, int(cy + r + 2))
    for row in range(first, last + 1):
        dy = row + 0.5 - cy
        span = r * r - dy * dy
        if span <= 0:
            continue
        half = math.sqrt(span) + 1.5
        base = row * width
        for col in range(max(0, int(cx - half)), min(width, int(cx + half) + 1)):
            dx = col + 0.5 - cx
            distance = math.hypot(dx, dy)
            value = 0.5 + (r - distance)
            if value > 0.0:
                cover[base + col] = 1.0 if value > 1.0 else value
    return cover


def stroke_polyline(width, polygon, thickness, scale, closed=True):
    """Thick stroke: distance to each segment, within its own bounding box
    only.

    Joins come out rounded with no extra effort since the distance to a
    segment includes its endpoints.
    """
    cover = [0.0] * (width * width)
    half = max(0.45, thickness * scale / 2.0)
    pts = [(x * scale, y * scale) for x, y in polygon]
    count = len(pts) if closed else len(pts) - 1
    for i in range(count):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % len(pts)]
        vx, vy = bx - ax, by - ay
        length2 = vx * vx + vy * vy
        left = max(0, int(min(ax, bx) - half - 1))
        right = min(width - 1, int(max(ax, bx) + half + 1))
        top = max(0, int(min(ay, by) - half - 1))
        bottom = min(width - 1, int(max(ay, by) + half + 1))
        for row in range(top, bottom + 1):
            py = row + 0.5
            base = row * width
            for col in range(left, right + 1):
                px = col + 0.5
                if length2 <= 0.0:
                    distance = math.hypot(px - ax, py - ay)
                else:
                    t = ((px - ax) * vx + (py - ay) * vy) / length2
                    t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
                    distance = math.hypot(px - ax - t * vx, py - ay - t * vy)
                value = 0.5 + (half - distance)
                if value > 0.0:
                    value = 1.0 if value > 1.0 else value
                    if value > cover[base + col]:
                        cover[base + col] = value
    return cover


def stroke_circle(width, radius, thickness, scale):
    """Thin ring: same principle as the disc, applied to an annulus."""
    cover = [0.0] * (width * width)
    cx = cy = CENTER * scale
    r = radius * scale
    half = max(0.45, thickness * scale / 2.0)
    first = max(0, int(cy - r - half - 2))
    last = min(width - 1, int(cy + r + half + 2))
    for row in range(first, last + 1):
        dy = row + 0.5 - cy
        base = row * width
        reach = r + half + 2
        if abs(dy) > reach:
            continue
        span = math.sqrt(max(0.0, reach * reach - dy * dy))
        for col in range(max(0, int(cx - span)), min(width, int(cx + span) + 1)):
            dx = col + 0.5 - cx
            value = 0.5 + (half - abs(math.hypot(dx, dy) - r))
            if value > 0.0:
                cover[base + col] = 1.0 if value > 1.0 else value
    return cover


# --- Painting --------------------------------------------------------------

def sample_stops(stops, t):
    t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
    previous = stops[0]
    for stop in stops[1:]:
        if t <= stop[0]:
            span = stop[0] - previous[0]
            local = 0.0 if span <= 0 else (t - previous[0]) / span
            return tuple((previous[1][i] + (stop[1][i] - previous[1][i]) * local) / 255.0
                         for i in range(3))
        previous = stop
    return tuple(c / 255.0 for c in stops[-1][1])


def linear_paint(box, axis, stops):
    """Linear gradient expressed within the shape's box, as in SVG."""
    x0, y0, x1, y1 = box
    w = max(1e-6, x1 - x0)
    h = max(1e-6, y1 - y0)
    ax, ay, bx, by = axis
    sx, sy = x0 + ax * w, y0 + ay * h
    ex, ey = x0 + bx * w, y0 + by * h
    dx, dy = ex - sx, ey - sy
    length2 = max(1e-6, dx * dx + dy * dy)

    def paint(px, py):
        return sample_stops(stops, ((px - sx) * dx + (py - sy) * dy) / length2)

    return paint


def radial_paint(box, focus, stops):
    x0, y0, x1, y1 = box
    w = max(1e-6, x1 - x0)
    h = max(1e-6, y1 - y0)
    cx, cy, radius = focus
    fx, fy = x0 + cx * w, y0 + cy * h
    reach = max(1e-6, radius * max(w, h))

    def paint(px, py):
        return sample_stops(stops, math.hypot(px - fx, py - fy) / reach)

    return paint


def compose(dest, cover, paint, scale, alpha=1.0, flat=None):
    """Lays a coverage onto the RGBA buffer, in "source over" mode."""
    width = int(math.isqrt(len(cover)))
    for index, value in enumerate(cover):
        if value <= 0.0:
            continue
        a = value * alpha
        if flat is not None:
            r, g, b = flat
        else:
            row, col = divmod(index, width)
            r, g, b = paint((col + 0.5) / scale, (row + 0.5) / scale)
        base = index * 4
        da = dest[base + 3]
        out = a + da * (1.0 - a)
        if out <= 0.0:
            continue
        for k, channel in enumerate((r, g, b)):
            dest[base + k] = (channel * a + dest[base + k] * da * (1.0 - a)) / out
        dest[base + 3] = out


def noise(x, y):
    """Deterministic noise, no dependency: an integer mix is enough."""
    n = (x * 374761393 + y * 668265263) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def smooth_noise(x, y, cell):
    """Interpolated value noise: stone grain, not snow.

    Raw noise from one pixel to the next reads like sensor noise. By
    sampling it every `cell` pixels and interpolating, we get clumps of
    the desired size.
    """
    fx, fy = x / cell, y / cell
    ix, iy = int(fx), int(fy)
    tx, ty = fx - ix, fy - iy
    tx = tx * tx * (3 - 2 * tx)
    ty = ty * ty * (3 - 2 * ty)
    n00, n10 = noise(ix, iy), noise(ix + 1, iy)
    n01, n11 = noise(ix, iy + 1), noise(ix + 1, iy + 1)
    top = n00 + (n10 - n00) * tx
    bottom = n01 + (n11 - n01) * tx
    return top + (bottom - top) * ty


def apply_grain(dest, width, strength, scale):
    """Stone grain, blended in overlay mode, wherever there is material.

    Two octaves: a broad marbling that gives the zones, a fine grain that
    gives the surface. The sizes are expressed within the 256 square so
    that the look does not change from one icon size to another.
    """
    coarse = max(1.0, 26.0 * scale)
    fine = max(1.0, 3.0 * scale)
    for index in range(width * width):
        base = index * 4
        if dest[base + 3] <= 0.02:
            continue
        row, col = divmod(index, width)
        value = 0.30 * smooth_noise(col, row, coarse) + 0.70 * smooth_noise(col, row, fine)
        for k in range(3):
            a = dest[base + k]
            blended = 2 * a * value if a < 0.5 else 1 - 2 * (1 - a) * (1 - value)
            dest[base + k] = a + (blended - a) * strength


def apply_shade(dest, width, scale):
    """Diagonal veil: lightens one corner, darkens the other."""
    ax, ay, bx, by = SHADE_AXIS
    sx, sy = ax * CANVAS, ay * CANVAS
    dx, dy = (bx - ax) * CANVAS, (by - ay) * CANVAS
    length2 = max(1e-6, dx * dx + dy * dy)
    for index in range(width * width):
        base = index * 4
        if dest[base + 3] <= 0.02:
            continue
        row, col = divmod(index, width)
        px, py = (col + 0.5) / scale, (row + 0.5) / scale
        t = ((px - sx) * dx + (py - sy) * dy) / length2
        t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
        if t < 0.5:
            amount = (0.5 - t) / 0.5 * 0.10
            for k in range(3):
                dest[base + k] += (1.0 - dest[base + k]) * amount
        else:
            amount = (t - 0.5) / 0.5 * 0.28
            for k in range(3):
                dest[base + k] *= 1.0 - amount


# --- Rendering ---------------------------------------------------------------

def render(size):
    """Renders the rune on a `size`-pixel square, transparent background."""
    scale = size / CANVAS
    dest = [0.0] * (size * size * 4)

    circles = ((C_OUT, False), (C_MID, True), (C_IN, False))
    outline = size >= 24

    # The blades' cast shadows, first, so they stay underneath.
    if size >= 32:
        shift = (SHADOW_OFFSET[0], SHADOW_OFFSET[1])
        for polygon in BLADES:
            moved = [(x + shift[0], y + shift[1]) for x, y in polygon]
            compose(dest, fill_polygon(size, moved, scale), None, scale,
                    alpha=SHADOW_ALPHA, flat=(0.0, 0.0, 0.0))

    for polygon, is_light in zip(BLADES, BLADE_IS_LIGHT):
        box = bounds(polygon)
        paint = (linear_paint(box, LIGHT_AXIS, LIGHT_STOPS) if is_light
                 else radial_paint(box, DARK_FOCUS, DARK_STOPS))
        compose(dest, fill_polygon(size, polygon, scale), paint, scale)

    for radius, is_light in circles:
        box = (CENTER - radius, CENTER - radius, CENTER + radius, CENTER + radius)
        paint = (linear_paint(box, LIGHT_AXIS, LIGHT_STOPS) if is_light
                 else radial_paint(box, DARK_FOCUS, DARK_STOPS))
        compose(dest, fill_disc(size, radius, scale), paint, scale)

    if size >= GRAIN_MIN_SIZE:
        apply_grain(dest, size, GRAIN_ALPHA, scale)
    apply_shade(dest, size, scale)

    if outline:
        for polygon in BLADES:
            compose(dest, stroke_polyline(size, polygon, BLADE_STROKE, scale), None, scale,
                    alpha=INK_ALPHA, flat=INK)
        for radius, _ in circles:
            compose(dest, stroke_circle(size, radius, CIRCLE_STROKE, scale), None, scale,
                    alpha=INK_ALPHA, flat=INK)

    rows = []
    for row in range(size):
        line = bytearray()
        for col in range(size):
            base = (row * size + col) * 4
            for k in range(4):
                value = dest[base + k]
                value = 0.0 if value < 0.0 else (1.0 if value > 1.0 else value)
                line.append(int(round(value * 255)))
        rows.append(bytes(line))
    return rows


def png(size, rows):
    """Encodes RGBA rows into PNG (filter 0)."""
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
    images = {}
    for size in sizes:
        images[size] = png(size, render(size))
        print(f"  {size} px")

    with open(os.path.join(assets, "app.png"), "wb") as f:
        f.write(images[256])

    # ICONDIR + ICONDIRENTRY*n + PNG payloads.
    offset = 6 + 16 * len(sizes)
    header = struct.pack("<HHH", 0, 1, len(sizes))
    entries, blobs = b"", b""
    for size in sizes:
        data = images[size]
        entries += struct.pack(
            "<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset
        )
        blobs += data
        offset += len(data)
    with open(os.path.join(assets, "app.ico"), "wb") as f:
        f.write(header + entries + blobs)

    print(f"assets/app.ico ({len(header + entries + blobs)} octets), assets/app.png")


if __name__ == "__main__":
    main()
