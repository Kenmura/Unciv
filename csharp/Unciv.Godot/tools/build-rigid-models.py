#!/usr/bin/env python3
"""T256: generates the rigid unit models of the bundled pack `Unciv.Godot/Packs/units`: ships, submarines, siege, armour, aircraft, missiles and the helicopter.

    python3 -I Unciv.Godot/tools/build-rigid-models.py [<pack folder, default Unciv.Godot/Packs/units>]

Original work for this project (MPL-2.0), code-generated, no third-party art and no input files (stdlib only). The unit list is the `rigid` section of the pack's `attachments.json`:
`"<Unit>": {"build": "<archetype>", "args": {...}}`, an archetype being a function of this file. Every model is built from convex solids (boxes, lofted tubes, extruded
outlines, ellipsoids) with flat normals, faces +Z, stands on y = 0 and keeps to the unit model contract (design/UNIT-MODELS.md section 1, checked here and by `UnitModelContractTests`):

* materials: `TeamColor` (sails, flags, stripes, painted parts, tinted with the civ colour at load) and a few fixed ones. No skeleton and no clips: `RigidMotion` moves the whole model.
* Armour: the turret is the top-level node `Part1` and the gun `Part2` (`UnitNode` turns them on the target); both turn about the vertical axis through the model origin (the turret sits on it).
* Aircraft hover on a pole above a shadow disc (the contract wants feet at y = 0), the helicopter lands on skids.
* `manifest.json` gets (or keeps) the `unit/<name>` keys of the rigid units; `build-unit-models.py` keeps them too.
"""
import importlib.util
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("build_unit_models", os.path.join(HERE, "build-unit-models.py"))
bum = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(bum)

MATERIALS = {
    "TeamColor": [0.8, 0.8, 0.8, 1.0],
    "Wood": [0.45, 0.30, 0.18, 1.0],
    "DarkWood": [0.27, 0.18, 0.11, 1.0],
    "Sail": [0.90, 0.86, 0.74, 1.0],
    "Steel": [0.58, 0.60, 0.64, 1.0],
    "Grey": [0.42, 0.45, 0.49, 1.0],
    "Dark": [0.16, 0.17, 0.19, 1.0],
    "Olive": [0.30, 0.36, 0.23, 1.0],
    "Sand": [0.62, 0.55, 0.38, 1.0],
    "Brass": [0.72, 0.55, 0.20, 1.0],
    "Shadow": [0.05, 0.05, 0.06, 1.0],
    # T307: bolder fixed colours so each family reads at the default zoom (siege each has its own: red-brown, ash, stone, teal, black iron, hide)
    "RedWood": [0.66, 0.27, 0.13, 1.0],
    "Ash": [0.68, 0.58, 0.38, 1.0],
    "Stone": [0.40, 0.40, 0.42, 1.0],
    "Teal": [0.10, 0.50, 0.52, 1.0],
    "Iron": [0.17, 0.18, 0.20, 1.0],
    "Hide": [0.50, 0.30, 0.14, 1.0],
    "Thatch": [0.36, 0.26, 0.10, 1.0],
    "Yellow": [0.92, 0.74, 0.10, 1.0],
    "Crimson": [0.72, 0.10, 0.10, 1.0],
    "Cream": [0.90, 0.86, 0.72, 1.0],
    "Blue": [0.18, 0.30, 0.62, 1.0],
    "Green": [0.20, 0.50, 0.22, 1.0],
}


# T307: the build checks stay inside the unit model contract (UnitModelContract caps 1.35 and 1.1); a rigid unit is kept a little smaller so it fits its tile
MAX_HEIGHT, MAX_RADIUS = 1.1, 0.85

# ------------------------------------------------------------------------------------------------------------------------------------------ geometry

def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def mean(pts): return tuple(sum(p[i] for p in pts) / len(pts) for i in range(3))


def solid(polygons):
    """Triangles (a, b, c) of a convex solid given as convex polygons, each wound to face away from the solid's centre; degenerate ones are dropped."""
    centre = mean([p for poly in polygons for p in poly])
    tris = []
    for poly in polygons:
        for i in range(1, len(poly) - 1):
            a, b, c = poly[0], poly[i], poly[i + 1]
            n = cross(sub(b, a), sub(c, a))
            if dot(n, n) < 1e-14:
                continue
            tris.append((a, b, c) if dot(n, sub(mean([a, b, c]), centre)) >= 0 else (a, c, b))
    return tris


def loft(rings):
    """Rings are lists of points (a one-point ring is an apex) joined by quads; the end rings are capped."""
    faces = []
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        if len(a) == 1:
            faces += [[a[0], b[j], b[(j + 1) % len(b)]] for j in range(len(b))]
        elif len(b) == 1:
            faces += [[a[j], a[(j + 1) % len(a)], b[0]] for j in range(len(a))]
        else:
            faces += [[a[j], a[(j + 1) % len(a)], b[(j + 1) % len(b)], b[j]] for j in range(len(a))]
    for end in (rings[0], rings[-1]):
        if len(end) > 2:
            faces.append(list(end))
    return solid(faces)


def ring(centre, ra, rb, axis, n):
    """An ellipse of radii ra, rb in the two planes perpendicular to `axis` ('x': y, z; 'y': x, z; 'z': x, y)."""
    cx, cy, cz = centre
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        u, v = ra * math.cos(t), rb * math.sin(t)
        pts.append({"x": (cx, cy + u, cz + v), "y": (cx + u, cy, cz + v), "z": (cx + u, cy + v, cz)}[axis])
    return pts


def at(axis, c, d):
    return {"x": (c[0] + d, c[1], c[2]), "y": (c[0], c[1] + d, c[2]), "z": (c[0], c[1], c[2] + d)}[axis]


def tube(c, length, r, r2=None, axis="z", n=12):
    """A frustum along `axis` centred on c: radius r at the start (-length/2), r2 at the end (default r; 0 = a cone's apex). A radius may be a pair for an ellipse."""
    r2 = r if r2 is None else r2
    pair = lambda v: v if isinstance(v, tuple) else (v, v)
    rings = []
    for d, rad in ((-length / 2, pair(r)), (length / 2, pair(r2))):
        centre = at(axis, c, d)
        rings.append([centre] if rad[0] == 0 else ring(centre, rad[0], rad[1], axis, n))
    return loft(rings)


def block(c, size):
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    rect = lambda z, sx, sy: [(c[0] - sx, c[1] - sy, c[2] + z), (c[0] + sx, c[1] - sy, c[2] + z), (c[0] + sx, c[1] + sy, c[2] + z), (c[0] - sx, c[1] + sy, c[2] + z)]
    return loft([rect(-hz, hx, hy), rect(hz, hx, hy)])


def slab(outline, y0, y1):
    """A prism from a convex outline of (x, z) points between two heights."""
    return loft([[(x, y0, z) for x, z in outline], [(x, y1, z) for x, z in outline]])


def extrude_x(profile, x0, x1):
    """A prism from a convex side profile of (z, y) points between two x values."""
    return loft([[(x0, y, z) for z, y in profile], [(x1, y, z) for z, y in profile]])


def ellipsoid(c, rx, ry, rz, n=8, m=6):
    rings = []
    for k in range(m + 1):
        theta = math.pi * k / m
        r = math.sin(theta)
        centre = (c[0], c[1], c[2] - rz * math.cos(theta))
        rings.append([centre] if k in (0, m) else ring(centre, rx * r, ry * r, "z", n))
    return loft(rings)


def rot(tris, rx=0.0, ry=0.0, rz=0.0):
    """Rotates about the origin: x first, then y, then z (radians)."""
    def turn(p):
        x, y, z = p
        y, z = y * math.cos(rx) - z * math.sin(rx), y * math.sin(rx) + z * math.cos(rx)
        x, z = x * math.cos(ry) + z * math.sin(ry), -x * math.sin(ry) + z * math.cos(ry)
        x, y = x * math.cos(rz) - y * math.sin(rz), x * math.sin(rz) + y * math.cos(rz)
        return (x, y, z)
    return [tuple(turn(p) for p in t) for t in tris]


def mov(tris, dx=0.0, dy=0.0, dz=0.0):
    return [tuple((p[0] + dx, p[1] + dy, p[2] + dz) for p in t) for t in tris]


def mirror(tris):
    return [(tuple((-p[0], p[1], p[2]) for p in t))[::-1] for t in tris]


class Model:
    """Top-level nodes of the model (`Body`, and `Part1`/`Part2` of armour, each with its own translation) holding triangles per material."""

    def __init__(self):
        self.nodes = {"Body": ((0.0, 0.0, 0.0), {})}

    def node(self, name, translation):
        self.nodes[name] = (translation, {})

    def add(self, material, tris, node="Body"):
        self.nodes[node][1].setdefault(material, []).extend(tris)

    def both(self, material, tris, node="Body"):
        """The triangles and their mirror image across x = 0."""
        self.add(material, tris, node)
        self.add(material, mirror(tris), node)


def flag(m, x, z, y0, y1, w=0.11, h=0.07):
    """A pole with a trailing TeamColor pennant: the civ's mark on land units and ships."""
    m.add("Steel", tube((x, (y0 + y1) / 2, z), y1 - y0, 0.009, axis="y", n=6))
    m.add("TeamColor", block((x, y1 - h / 2, z - w / 2), (0.012, h, w)))


def wheel(m, x, z, r, material="DarkWood", width=0.04):
    m.add(material, tube((x, r, z), width, r, axis="x", n=12))


# ------------------------------------------------------------------------------------------------------------------------------------------ ships

def hull(length, beam, y0, y1, keel=0.7, bow_rise=0.04, stern=0.75):
    rings = []
    for t, wf, rise in ((-0.5, stern, 0.0), (-0.3, 1.0, 0.0), (0.1, 1.0, 0.0), (0.36, 0.7, 0.01), (0.5, 0.06, bow_rise)):
        z, w = t * length, beam / 2 * wf
        rings.append([(-w, y1 + rise, z), (w, y1 + rise, z), (w * keel, y0, z), (-w * keel, y0, z)])
    return loft(rings)


def oared(m, length=1.0, beam=0.24, oars=6, mast=0.34, sail=0.3, ram=False, castle=False, bow_castle=False, turtle=False, hull_colour="Wood", masts=1, sail_colour="TeamColor"):
    y1 = 0.12
    m.add(hull_colour, hull(length, beam, 0.0, y1, keel=0.6))
    if turtle:  # a closed iron shell with spikes (Turtle Ship) instead of a mast
        m.add("Grey", tube((0, y1 + 0.06, -0.02), length * 0.7, (beam * 0.46, 0.07), (beam * 0.3, 0.05), n=12))
        m.add("TeamColor", block((0, y1 + 0.1, -0.02), (beam * 0.4, 0.012, length * 0.5)))
        for i in range(5):
            z = -0.2 + i * 0.1
            for s in (-1, 1):
                m.add("Steel", tube((s * beam * 0.34, y1 + 0.1, z), 0.05, 0.012, 0.0, axis="y", n=6))
        m.add("Dark", block((0, y1 + 0.07, length * 0.5), (0.07, 0.07, 0.1)))  # the dragon head
        m.add("Steel", tube((0, y1 + 0.2, 0.0), 0.2, 0.008, axis="y", n=6))
        flag(m, 0.0, -0.05, y1 + 0.18, 0.44)
    else:
        top = y1 + mast
        for k in range(masts):  # T307: a galleass carries two masts, the others one
            z = length * 0.05 if masts == 1 else length * (0.22 - 0.4 * k)
            m.add("DarkWood", tube((0, y1 + mast / 2, z), mast, 0.014, axis="y", n=8))
            m.add("DarkWood", block((0, top - 0.03, z), (sail * 1.1, 0.014, 0.014)))
            if sail > 0:
                m.add(sail_colour if k == 0 else "Sail", block((0, top - 0.03 - sail * 0.45, z), (sail, sail * 0.9, 0.012)))
    for i in range(oars):
        z = -length * 0.28 + i * (length * 0.5 / max(1, oars - 1))
        for s in (-1, 1):
            m.add("DarkWood", mov(rot(block((0, 0, 0), (0.2, 0.012, 0.02)), rz=-s * 0.4), s * (beam / 2 + 0.07), y1 - 0.02, z))
    if ram:
        m.add("Steel", tube((0, 0.04, length / 2 + 0.03), 0.16, 0.035, 0.0, n=8))
    if castle:
        m.add("DarkWood", block((0, y1 + 0.05, -length * 0.34), (beam * 0.8, 0.1, 0.2)))
    if bow_castle:
        m.add("DarkWood", block((0, y1 + 0.04, length * 0.34), (beam * 0.7, 0.08, 0.14)))


def work_boat(m):
    m.add("Wood", hull(0.6, 0.26, 0.0, 0.1, keel=0.6))
    m.add("DarkWood", block((0, 0.15, -0.12), (0.16, 0.1, 0.18)))
    m.add("Sail", block((0, 0.13, 0.12), (0.14, 0.06, 0.14)))  # a heap of nets
    flag(m, 0.0, 0.0, 0.1, 0.43)


def sailer(m, length=1.0, beam=0.26, masts=3, y1=0.15, tiers=2, gun_rows=0, hull_colour="Wood", castle=True, big_flag=False, sail_scale=1.0, sail_colour="Sail", stern_lantern=False):
    m.add(hull_colour, hull(length, beam, 0.0, y1, keel=0.8, bow_rise=0.05))
    m.add("DarkWood", block((0, y1 - 0.012, 0.0), (beam * 0.98, 0.008, length * 0.7)))  # a dark wale
    for row in range(gun_rows):
        y = y1 - 0.045 - row * 0.045
        for i in range(5):
            z = -length * 0.22 + i * length * 0.11
            for s in (-1, 1):
                m.add("Dark", block((s * (beam / 2 * (0.8 + 0.2 * (y / y1)) + 0.002), y, z), (0.012, 0.022, 0.04)))
    if castle:
        m.add("DarkWood", block((0, y1 + 0.05, -length * 0.34), (beam * 0.82, 0.1, 0.24)))
        m.add("DarkWood", block((0, y1 + 0.03, length * 0.38), (beam * 0.6, 0.06, 0.12)))
    m.add("DarkWood", tube((0, y1 + 0.01, length * 0.5 + 0.02), 0.1, 0.014, axis="z", n=6))  # bowsprit
    for k in range(masts):
        z = -length * 0.26 + k * length * 0.46 / max(1, masts - 1) if masts > 1 else 0.0
        h = 0.42 if k != 0 or masts == 1 else 0.32
        m.add("DarkWood", tube((0, y1 + h / 2, z), h, 0.014, axis="y", n=8))
        for t in range(tiers):
            sw = (beam * 1.35 - t * 0.07) * (1.0 if k else 0.8) * sail_scale
            sh = (h - 0.06) / tiers - 0.01
            y = y1 + 0.04 + sh / 2 + t * (sh + 0.012)
            m.add("TeamColor" if t == 0 else sail_colour, block((0, y, z), (sw, sh, 0.012)))
            m.add("DarkWood", block((0, y + sh / 2, z), (sw * 1.1, 0.012, 0.012)))
    if big_flag:
        flag(m, 0.0, -length * 0.26, y1 + 0.42, y1 + 0.58, 0.16, 0.1)
    if stern_lantern:  # a gilded stern gallery: widest at the back, which tells the frigate from the privateer from above
        m.add("Brass", block((0, y1 + 0.02, -length * 0.5 + 0.02), (beam * 1.25, 0.05, 0.1)))


def ironclad(m):
    m.add("Dark", hull(0.9, 0.4, 0.0, 0.1, keel=0.7))
    m.add("Grey", loft([[(-0.2, 0.1, -0.25), (0.2, 0.1, -0.25), (0.2, 0.1, 0.25), (-0.2, 0.1, 0.25)], [(-0.15, 0.2, -0.2), (0.15, 0.2, -0.2), (0.15, 0.2, 0.2), (-0.15, 0.2, 0.2)]]))
    m.add("Dark", tube((0, 0.29, -0.1), 0.16, 0.045, axis="y"))
    m.add("Grey", tube((0, 0.14, 0.34), 0.06, 0.075, axis="y"))
    m.add("Dark", tube((0, 0.14, 0.42), 0.16, 0.014))
    m.add("RedWood", block((0, 0.205, 0.0), (0.12, 0.01, 0.4)))
    flag(m, 0.0, -0.4, 0.1, 0.45)


def warship(m, length=1.1, beam=0.2, y1=0.11, turrets=(), bridge=None, funnels=(), mast=(0.0, 0.42), missiles=(), stripe=0.3, hull_colour="Grey"):
    m.add(hull_colour, hull(length, beam, 0.0, y1, keel=0.55, bow_rise=0.05, stern=0.9))
    m.add("TeamColor", block((0, y1 + 0.003, 0.0), (beam * 0.55, 0.006, stripe)))
    for z, barrels in turrets:
        side = 1.0 if z > 0 else -1.0
        m.add("Grey", tube((0, y1 + 0.035, z), 0.07, beam * 0.3, beam * 0.26, axis="y", n=8))
        for b in range(barrels):
            x = (b - (barrels - 1) / 2) * 0.05
            m.add("Dark", tube((x, y1 + 0.045, z + side * 0.1), 0.2, 0.012))
    if bridge:
        z, depth, height = bridge
        m.add("Grey", block((0, y1 + height / 2, z), (beam * 0.62, height, depth)))
        m.add("Dark", block((0, y1 + height * 0.65, z + depth / 2 + 0.002), (beam * 0.5, 0.025, 0.006)))
    for z in funnels:
        m.add("Dark", tube((0, y1 + 0.1, z), 0.14, (0.03, 0.045), (0.026, 0.038), axis="y", n=8))
    for z in missiles:
        for s in (-1, 1):
            m.add("Steel", mov(rot(block((0, 0, 0), (0.03, 0.03, 0.16)), rx=-0.5), s * 0.04, y1 + 0.05, z))
    mz, mh = mast
    m.add("Steel", tube((0, y1 + mh / 2, mz), mh, 0.01, axis="y", n=6))
    m.add("Dark", block((0, y1 + mh - 0.02, mz), (0.12, 0.02, 0.02)))
    m.add("TeamColor", block((0, y1 + mh - 0.06, mz - 0.04), (0.012, 0.06, 0.09)))


def carrier(m):
    m.add("Grey", hull(1.3, 0.36, 0.0, 0.1, keel=0.5, bow_rise=0.02, stern=0.95))
    m.add("Dark", slab([(-0.24, -0.6), (0.24, -0.6), (0.3, 0.2), (0.17, 0.62), (-0.17, 0.62), (-0.3, 0.2)], 0.1, 0.125))
    m.add("Yellow", block((-0.1, 0.1275, 0.05), (0.012, 0.003, 0.8)))
    m.add("TeamColor", block((0, 0.1275, 0.05), (0.025, 0.003, 0.9)))
    m.add("Grey", block((0.24, 0.2, -0.05), (0.09, 0.15, 0.22)))
    m.add("Steel", tube((0.24, 0.37, -0.05), 0.2, 0.008, axis="y", n=6))
    m.add("Dark", block((0.24, 0.4, -0.05), (0.1, 0.02, 0.02)))
    flag(m, 0.24, -0.14, 0.275, 0.45, 0.1, 0.06)


def submarine(m, length=1.0, r=0.1, tower=0.1, nuclear=False):
    rings = []
    for t, f in ((-0.5, 0.0), (-0.46, 0.45), (-0.3, 0.9), (0.0, 1.0), (0.3, 0.95), (0.45, 0.55), (0.5, 0.0)):
        z = t * length
        rings.append([(0, r, z)] if f == 0 else ring((0, r, z), r * f * 0.9, r * f, "z", 12))
    m.add("Dark" if not nuclear else "Grey", loft(rings))
    m.add("Grey", block((0, r * 2 + 0.05, length * tower), (0.07, 0.14, 0.22)))
    if nuclear:  # missile hatches in a double row: a nuclear boat is bigger and studded
        for k in range(4):
            for s in (-1, 1):
                m.add("Yellow", block((s * r * 0.45, r * 1.93, -length * 0.1 - k * 0.09), (0.05, 0.012, 0.06)))
    m.add("TeamColor", block((0, r * 2 + 0.122, length * tower), (0.072, 0.012, 0.222)))
    m.add("Steel", tube((0, r * 2 + 0.17, length * tower + 0.04), 0.14, 0.008, axis="y", n=6))
    m.add("Steel", tube((0, r * 2 + 0.2, length * tower - 0.04), 0.1, 0.008, axis="y", n=6))
    m.add("Dark", block((0, r, -length * 0.42), (0.3, 0.014, 0.1)))
    m.add("Dark", block((0, r + 0.05, -length * 0.42), (0.014, 0.12, 0.1)))


def great_admiral(m):
    sailer(m, length=0.8, beam=0.24, masts=1, y1=0.13, tiers=2, hull_colour="DarkWood", castle=True, big_flag=True)


# ------------------------------------------------------------------------------------------------------------------------------------------ siege
# T307: every siege engine is big (about 0.9 long) and has its own colour and its own outline seen from above, where the default-zoom camera looks:
# ram = long hide-roofed shed with the log sticking out, catapult = red-brown frame with a stone-loaded arm, ballista = a wide bow across a pale stock,
# trebuchet = tall stone-weighted lever on spread legs, hwach'a = teal rocket box on two big wheels with a long pole, cannon = black iron barrel on a brown carriage.

def banner_side(m, x, z, w=0.3, h=0.14, y=0.3):
    """A TeamColor shield or banner hung on the side of an engine, standing upright (the civ's mark seen from the side, not a big coloured roof)."""
    m.add("TeamColor", block((x, y, z), (0.014, h, w)))


def ram(m):
    for s in (-1, 1):
        m.add("Wood", block((s * 0.17, 0.09, 0.0), (0.06, 0.05, 0.78)))  # base beams
        for z in (-0.27, 0.27):
            wheel(m, s * 0.22, z, 0.1, "Iron", 0.05)
            for dz in (-0.2, 0.2):
                m.add("Wood", block((s * 0.17, 0.3, z * 0.8 + dz * 0.0), (0.04, 0.4, 0.04)))  # posts
        m.add("RedWood", mov(rot(block((0, 0, 0), (0.24, 0.03, 0.8)), rz=-s * 0.55), s * 0.19, 0.5, -0.02))  # the two slopes of the roof, a slot between them shows the log
        banner_side(m, s * 0.25, 0.0, 0.46, 0.17, 0.29)
    m.add("TeamColor", block((0, 0.6, -0.02), (0.05, 0.02, 0.8)))  # a narrow stripe along the ridge
    m.add("Ash", tube((0, 0.4, 0.08), 1.0, 0.075, axis="z", n=8))  # the pale log hangs in the slot and sticks out far at both ends
    m.add("Steel", tube((0, 0.4, 0.62), 0.2, 0.12, 0.05, axis="z", n=8))  # the big bright iron head
    m.add("DarkWood", tube((0, 0.4, -0.42), 0.06, 0.09, axis="z", n=8))


def catapult(m):
    m.add("RedWood", block((0, 0.14, 0), (0.44, 0.05, 0.62)))
    for s in (-1, 1):
        for z in (-0.22, 0.22):
            wheel(m, s * 0.27, z, 0.11, "Iron", 0.06)
        m.add("RedWood", block((s * 0.17, 0.3, 0.02), (0.05, 0.32, 0.08)))  # the A-frames the arm turns in
        m.add("DarkWood", mov(rot(block((0, 0, 0), (0.05, 0.3, 0.05)), rz=s * 0.45), s * 0.19, 0.3, 0.1))
        banner_side(m, s * 0.225, -0.2, 0.26, 0.14, 0.2)
    m.add("DarkWood", tube((0, 0.33, 0.02), 0.4, 0.028, axis="x", n=8))  # the axle of the arm
    m.add("Ash", mov(rot(block((0, 0, 0), (0.07, 0.05, 0.84)), rx=-0.5), 0, 0.42, 0.18))  # the throwing arm reaches far forward and up
    m.add("Iron", mov(rot(block((0, 0, 0), (0.2, 0.05, 0.2)), rx=-0.5), 0, 0.58, 0.55))  # the sling cup
    m.add("Stone", ellipsoid((0, 0.66, 0.54), 0.1, 0.1, 0.1, n=8, m=6))  # the boulder, big enough to be seen
    m.add("Iron", tube((-0.0, 0.3, -0.34), 0.34, 0.05, axis="x", n=8))  # the windlass drum at the back


def ballista(m):
    m.add("Green", block((0, 0.13, 0.0), (0.26, 0.05, 0.64)))
    for x in (-0.18, 0.18):
        for z in (-0.2, 0.2):
            wheel(m, x, z, 0.1, "Wood", 0.06)
    m.add("DarkWood", block((0, 0.24, 0.05), (0.07, 0.18, 0.07)))
    m.add("Ash", block((0, 0.34, 0.0), (0.1, 0.07, 0.66)))  # the stock
    for s in (-1, 1):  # the bow: two thick dark arms swept forward and a dark cord, about as wide as the engine is long
        m.add("Iron", mov(rot(block((0, 0, 0), (0.4, 0.08, 0.09)), ry=s * 0.5), s * 0.2, 0.36, 0.18))
        m.add("Iron", block((s * 0.38, 0.36, 0.0), (0.08, 0.1, 0.1)))
        m.add("Dark", block((s * 0.19, 0.36, -0.14), (0.38, 0.02, 0.02)))
    m.add("Steel", block((0, 0.4, 0.34), (0.035, 0.035, 0.5)))  # the bolt
    m.add("Steel", tube((0, 0.4, 0.66), 0.14, 0.05, 0.0, n=6))
    for s in (-1, 1):
        banner_side(m, s * 0.135, -0.2, 0.22, 0.14, 0.28)


def trebuchet(m):
    m.add("Stone", block((0, 0.06, 0), (0.4, 0.05, 0.9)))  # a heavy plinth
    for s in (-1, 1):
        for z in (-0.38, 0.38):
            wheel(m, s * 0.24, z, 0.06, "Iron", 0.05)
        for dz in (-1, 1):  # four splayed legs
            m.add("DarkWood", mov(rot(block((0, 0, 0), (0.05, 0.6, 0.05)), rx=-dz * 0.4, rz=s * 0.15), s * 0.17, 0.36, dz * 0.12))
    m.add("DarkWood", tube((0, 0.64, 0.0), 0.4, 0.03, axis="x", n=8))  # axle at the top
    m.add("Ash", mov(rot(block((0, 0, 0), (0.06, 0.06, 1.12)), rx=-0.5), 0, 0.64, 0.0))  # the long lever, short end down at the back
    m.add("Stone", mov(rot(block((0, 0, 0), (0.28, 0.24, 0.28)), rx=0.0), 0, 0.28, -0.46))  # the counterweight box
    m.add("Iron", block((0, 0.28, -0.46), (0.3, 0.06, 0.3)))
    m.add("Dark", block((0, 0.84, 0.46), (0.14, 0.05, 0.14)))
    m.add("Cream", tube((0, 0.84, 0.46), 0.14, 0.05, axis="y", n=6))  # the sling pouch
    banner_side(m, 0.205, 0.0, 0.5, 0.2, 0.2)
    banner_side(m, -0.205, 0.0, 0.5, 0.2, 0.2)


def hwacha(m):
    m.add("Teal", block((0, 0.18, 0), (0.3, 0.06, 0.62)))
    for s in (-1, 1):
        wheel(m, s * 0.24, 0.0, 0.17, "RedWood", 0.06)
        m.add("RedWood", block((s * 0.1, 0.2, -0.56), (0.04, 0.04, 0.5)))  # the two poles to push or pull it
    m.add("DarkWood", block((0, 0.2, -0.8), (0.24, 0.04, 0.04)))
    box = block((0, 0, 0), (0.34, 0.26, 0.48))
    m.add("Teal", mov(rot(box, rx=-0.5), 0, 0.42, 0.04))
    tubes = []
    for ix in range(5):
        for iy in range(4):
            tubes += tube(((ix - 2) * 0.064, (iy - 1.5) * 0.058, 0.25), 0.05, 0.022, n=6)
    m.add("Dark", mov(rot(tubes, rx=-0.5), 0, 0.42, 0.04))
    m.add("Yellow", mov(rot(block((0, 0, 0), (0.36, 0.03, 0.1)), rx=-0.5), 0, 0.31, -0.13))  # a yellow band across the box
    banner_side(m, 0.325, 0.0, 0.3, 0.14, 0.2)
    banner_side(m, -0.325, 0.0, 0.3, 0.14, 0.2)


def cannon(m, long_barrel=False):
    m.add("Iron", mov(rot(tube((0, 0, 0), 0.92, 0.115, 0.078, n=12), rx=-0.12), 0, 0.3, 0.14))  # a long, fat black barrel
    m.add("Brass", mov(rot(tube((0, 0, 0), 0.08, 0.125, n=12), rx=-0.12), 0, 0.31, 0.16))
    m.add("Brass", mov(rot(tube((0, 0, 0), 0.07, 0.085, n=12), rx=-0.12), 0, 0.33, 0.6))
    m.add("Iron", ellipsoid((0, 0.28, -0.34), 0.09, 0.09, 0.09, n=8, m=6))  # the cascabel knob
    for s in (-1, 1):
        m.add("RedWood", block((s * 0.15, 0.22, 0.0), (0.06, 0.16, 0.62)))
        wheel(m, s * 0.24, 0.0, 0.22, "DarkWood", 0.07)
        banner_side(m, s * 0.185, -0.06, 0.2, 0.12, 0.25)
    m.add("DarkWood", tube((0, 0.2, 0.0), 0.5, 0.025, axis="x", n=8))
    m.add("RedWood", block((0, 0.07, -0.45), (0.09, 0.05, 0.3)))  # the trail


def artillery(m):
    m.add("Olive", mov(rot(tube((0, 0, 0), 0.8, 0.055, 0.04, n=10), rx=-0.2), 0, 0.3, 0.16))
    m.add("Steel", mov(rot(tube((0, 0, 0), 0.09, 0.07, n=10), rx=-0.2), 0, 0.3, 0.52))
    m.add("Olive", mov(rot(block((0, 0, 0), (0.12, 0.12, 0.3)), rx=-0.2), 0, 0.28, -0.12))
    m.add("Olive", block((0, 0.32, 0.12), (0.46, 0.3, 0.02)))  # a wide gun shield
    for s in (-1, 1):
        wheel(m, s * 0.24, 0.0, 0.14, "Dark", 0.07)
        m.add("Olive", mov(rot(block((0, 0, 0), (0.04, 0.04, 0.5)), ry=s * 0.35), s * 0.15, 0.06, -0.42))  # split trail legs
    m.add("Dark", tube((0, 0.14, 0.0), 0.46, 0.02, axis="x", n=8))
    flag(m, 0.0, -0.6, 0.05, 0.48)
    banner_side(m, 0.23, 0.12, 0.2, 0.16, 0.32)


def rocket_artillery(m):
    m.add("Olive", block((0, 0.18, 0), (0.34, 0.1, 0.9)))
    m.add("Olive", block((0, 0.34, 0.34), (0.34, 0.24, 0.24)))  # the cab
    m.add("Dark", block((0, 0.38, 0.465), (0.28, 0.1, 0.012)))
    for s in (-1, 1):
        for z in (-0.32, 0.0, 0.34):
            wheel(m, s * 0.2, z, 0.1, "Dark", 0.08)
    m.add("Grey", mov(rot(block((0, 0, 0), (0.3, 0.06, 0.62)), rx=-0.45), 0, 0.38, -0.12))
    for ix in range(3):
        for iy in range(2):
            x, y = (ix - 1) * 0.095, 0.05 + iy * 0.07
            m.add("Steel", mov(rot(tube((x, y, 0.0), 0.66, 0.028, n=8), rx=-0.45), 0, 0.38, -0.12))
            m.add("TeamColor", mov(rot(tube((x, y, 0.4), 0.13, 0.028, 0.0, n=8), rx=-0.45), 0, 0.38, -0.12))


# ------------------------------------------------------------------------------------------------------------------------------------------ armour

def tracks(m, length=0.82, x=0.21, width=0.1, height=0.17, colour="Dark"):
    h = length / 2
    for s in (-1, 1):
        m.add(colour, extrude_x([(-h, 0.03), (-h, height * 0.8), (-h * 0.75, height), (h * 0.75, height), (h, height * 0.7), (h, 0.03), (h * 0.85, 0.0), (-h * 0.85, 0.0)], s * x - width / 2, s * x + width / 2))


def tank_body(m, hull_colour, hull_top=0.29, glacis=0.42, rear=-0.38, length=0.82):
    m.add(hull_colour, extrude_x([(rear, 0.17), (rear, hull_top - 0.02), (rear * 0.3, hull_top), (glacis * 0.5, hull_top), (glacis, 0.2), (glacis, 0.17)], -0.19, 0.19))


def turret_node(m, colour, y, radius=0.16, height=0.11, n=8, flat=False):
    m.node("Part1", (0.0, y, 0.0))
    if flat:
        m.add(colour, slab([(-radius, -radius * 1.0), (radius, -radius), (radius * 0.8, radius * 1.1), (-radius * 0.8, radius * 1.1)], 0.0, height), "Part1")
    else:
        m.add(colour, tube((0, height / 2, 0.0), height, (radius * 1.1, radius), (radius * 0.85, radius * 0.8), axis="y", n=n), "Part1")
    m.add("Dark", tube((0.06, height + 0.015, -0.03), 0.03, 0.035, axis="y", n=8), "Part1")


def gun_node(m, y, z, length, radius, colour="Dark", sleeve=False):
    m.node("Part2", (0.0, y, z))
    m.add(colour, tube((0, 0, 0), length, radius, radius, n=8), "Part2")
    if sleeve:
        m.add(colour, tube((0, 0, -length * 0.3), length * 0.35, radius * 1.6, n=8), "Part2")
    m.add(colour, tube((0, 0, length / 2), 0.05, radius * 1.4, n=8), "Part2")


def tank(m, hull_colour="Blue", barrel=0.5, flat=False, stripe=False, length=0.82, x=0.21, skirts=0.0, deck=False, brake=False, turret=0.16, track_colour="Dark", turret_colour=None, gun=0.02):
    """T307: length, track spacing, side skirts (a slab over the tracks), a rear engine deck and a muzzle brake set the outline seen from above: Tank, Panzer and Modern Armor differ in all of them."""
    tracks(m, length, x, 0.1, 0.17, track_colour)
    tank_body(m, hull_colour, rear=-length * 0.46, glacis=length * 0.52, length=length)
    if skirts:
        for s in (-1, 1):
            m.add(hull_colour, block((s * (x + 0.07), 0.2, 0.0), (skirts, 0.09, length * 0.78)))
    if deck:
        m.add("Dark", block((0, 0.285, -length * 0.3), (0.3, 0.03, length * 0.24)))
        for k in range(4):
            m.add("Steel", block((0, 0.302, -length * 0.38 + k * 0.045), (0.26, 0.008, 0.012)))
    turret_node(m, turret_colour or hull_colour, 0.29, turret, 0.11, flat=flat)  # T307 review: a turret colour apart from the hull makes it read from above
    gun_node(m, 0.35, 0.05 + barrel / 2, barrel, gun)
    if brake:
        m.add("Dark", block((0, 0.35, 0.05 + barrel + 0.01), (0.1, 0.07, 0.08)))
    if stripe:
        m.add("TeamColor", block((0, 0.292, -0.16), (0.3, 0.004, 0.1)))
    flag(m, 0.14, -length * 0.42, 0.27, 0.46, 0.1, 0.06)


def landship(m):
    h = 0.5
    for s in (-1, 1):
        m.add("Dark", extrude_x([(-h, 0.04), (-h * 0.5, 0.34), (h * 0.5, 0.34), (h, 0.06), (h * 0.85, 0.0), (-h * 0.85, 0.0)], s * 0.2 - 0.045, s * 0.2 + 0.045))
        m.add("Olive", block((s * 0.31, 0.2, 0.05), (0.14, 0.1, 0.3)))  # T307: big side sponsons
        m.add("Dark", tube((s * 0.31, 0.2, 0.26), 0.2, 0.014))
    m.add("Olive", block((0, 0.22, 0), (0.34, 0.2, 0.6)))
    turret_node(m, "Olive", 0.32, 0.1, 0.1)
    gun_node(m, 0.37, 0.18, 0.3, 0.017)
    m.add("TeamColor", block((0, 0.325, -0.15), (0.3, 0.004, 0.1)))
    flag(m, 0.1, -0.28, 0.32, 0.46, 0.1, 0.06)


def modern_armor(m):
    tank(m, "Sand", barrel=0.66, flat=True, length=0.92, x=0.24, skirts=0.2, turret=0.17, track_colour="Dark")
    m.add("Dark", block((0, 0.33, 0.1), (0.1, 0.05, 0.1)))  # a sensor block on the roof
    m.add("TeamColor", block((0, 0.292, -0.2), (0.26, 0.004, 0.08)))


def death_robot(m):
    for s in (-1, 1):
        m.add("Grey", block((s * 0.14, 0.2, 0.0), (0.11, 0.3, 0.11)))
        m.add("Dark", block((s * 0.14, 0.03, 0.04), (0.15, 0.06, 0.26)))
        m.add("TeamColor", block((s * 0.14, 0.17, 0.06), (0.116, 0.07, 0.03)))
    m.add("Dark", block((0, 0.38, 0.0), (0.4, 0.1, 0.22)))
    m.node("Part1", (0.0, 0.62, 0.0))
    m.add("Grey", block((0, 0, 0), (0.36, 0.26, 0.24)), "Part1")
    m.add("Dark", block((0, 0.19, 0.01), (0.12, 0.1, 0.11)), "Part1")
    m.add("TeamColor", block((0, 0.2, 0.068), (0.1, 0.03, 0.012)), "Part1")
    for s in (-1, 1):
        m.add("TeamColor", block((s * 0.22, 0.08, 0.0), (0.08, 0.1, 0.18)), "Part1")
    gun_node(m, 0.6, 0.4, 0.4, 0.035)


# ------------------------------------------------------------------------------------------------------------------------------------------ aircraft

FLY = 0.34  # height of a hovering aircraft's centre


def stand(m, height=FLY):
    m.add("Shadow", tube((0, 0.005, 0), 0.01, 0.22, axis="y", n=16))
    m.add("Steel", tube((0, height / 2, 0), height, 0.012, axis="y", n=6))


def fuselage(m, y, length, radius, material="Grey", prop=0.0):
    rings = []
    for t, f in ((-0.5, 0.18), (-0.25, 0.7), (0.05, 1.0), (0.3, 0.85), (0.5, 0.0)):
        z = t * length
        rings.append([(0, y, z)] if f == 0 else ring((0, y, z), radius * f, radius * f, "z", 8))
    m.add(material, loft(rings))
    if prop:
        m.add("Dark", block((0, y, length / 2 + 0.012), (prop * 2, 0.02, 0.006)))
        m.add("Dark", block((0, y, length / 2 + 0.012), (0.02, prop * 2, 0.006)))


def wings(m, y, span, root, tip, sweep=0.0, z=0.0, thickness=0.015, material="Grey", roundels=True, shape="swept"):
    """T307: shape 'swept' (tapered, swept back), 'straight' (a plain rectangle) or 'ellipse' (rounded wing tips): the outline from above tells the fighters and bombers apart."""
    if shape == "straight":
        outline = [(0.0, z + root / 2), (span / 2, z + root / 2), (span / 2, z - root / 2), (0.0, z - root / 2)]
    elif shape == "ellipse":
        outline = [(span / 2 * math.sin(t * math.pi / 6), z + root / 2 * math.cos(t * math.pi / 6)) for t in range(7)]
    else:
        outline = [(0.0, z + root / 2), (span / 2, z + root / 2 - sweep), (span / 2, z + root / 2 - sweep - tip), (0.0, z - root / 2)]
    for s in (-1, 1):
        m.add(material, slab([(s * x, zz) for x, zz in outline], y - thickness / 2, y + thickness / 2))
        if roundels:
            m.add("TeamColor", tube((s * span * 0.32, y + thickness / 2 + 0.002, z - sweep * 0.5 - 0.01), 0.006, 0.04, axis="y", n=10))


def tail(m, y, length, size=0.12, material="Grey"):
    z = -length / 2
    for s in (-1, 1):
        m.add(material, slab([(0, z + 0.08), (s * size * 1.2, z + 0.02), (s * size * 1.2, z - 0.03), (0, z - 0.06)], y - 0.006, y + 0.006))
    m.add(material, extrude_x([(z + 0.07, y), (z + 0.01, y + size), (z - 0.04, y + size), (z - 0.06, y)], -0.006, 0.006))
    m.add("TeamColor", extrude_x([(z + 0.01, y + size * 0.55), (z + 0.005, y + size * 0.95), (z - 0.03, y + size * 0.95), (z - 0.045, y + size * 0.55)], -0.0075, 0.0075))


def nacelle(m, x, y, z, length=0.3, radius=0.04, prop=0.1):
    m.add("Dark", tube((x, y, z), length, radius * 0.8, radius, n=8))
    m.add("Steel", tube((x, y, z + length / 2 + 0.03), 0.06, radius * 0.5, 0.0, n=8))
    m.add("Dark", block((x, y, z + length / 2 + 0.006), (prop * 2, 0.018, 0.005)))
    m.add("Dark", block((x, y, z + length / 2 + 0.006), (0.018, prop * 2, 0.005)))


def propeller(m, wing_levels=(0.0,), length=0.9, span=0.9, engines=(), fuselage_radius=0.06, nose_prop=0.12, bomber=False, round_nose=False, shape="swept", body="Grey", root=0.26, wing_material="Grey"):
    stand(m)
    fuselage(m, FLY, length, fuselage_radius, body if not bomber else "Olive", prop=nose_prop if not engines else 0.0)
    for dy in wing_levels:
        wings(m, FLY + dy, span, root if not bomber else 0.3, 0.14, 0.02 if bomber else 0.06, z=0.02, shape=shape, material=wing_material)
    if len(wing_levels) > 1:  # struts between the wings of a biplane
        for s in (-1, 1):
            for x in (0.25, 0.5 * span * 0.8):
                m.add("DarkWood", block((s * x, FLY + (wing_levels[0] + wing_levels[-1]) / 2, 0.06), (0.01, abs(wing_levels[-1] - wing_levels[0]), 0.012)))
    tail(m, FLY + 0.01, length)
    m.add("Dark", ellipsoid((0, FLY + fuselage_radius * 0.8, length * 0.08), 0.035, 0.03, 0.09, n=8, m=4))
    for x, z in engines:
        nacelle(m, x, FLY - 0.01 + wing_levels[0], z, 0.3, 0.045, 0.11)


def jet(m, span=0.6, length=0.9, twin_tail=True):
    stand(m)
    fuselage(m, FLY, length, 0.06, "Grey")
    for s in (-1, 1):
        m.add("Grey", slab([(s * 0.05, 0.14), (s * span / 2, -0.2), (s * span / 2, -0.27), (s * 0.05, -0.24)], FLY - 0.01, FLY + 0.004))
        m.add("TeamColor", tube((s * span * 0.3, FLY + 0.006, -0.17), 0.006, 0.035, axis="y", n=10))
        m.add("Grey", extrude_x([(-0.3, FLY + 0.03), (-0.4, FLY + 0.17), (-0.46, FLY + 0.17), (-0.46, FLY + 0.03)], s * 0.07 - 0.005, s * 0.07 + 0.005))
        m.add("TeamColor", extrude_x([(-0.37, FLY + 0.1), (-0.405, FLY + 0.16), (-0.45, FLY + 0.16), (-0.45, FLY + 0.1)], s * 0.07 - 0.0065, s * 0.07 + 0.0065))
        m.add("Dark", block((s * 0.07, FLY - 0.015, -0.2), (0.04, 0.04, 0.12)))
    m.add("Dark", ellipsoid((0, FLY + 0.045, 0.12), 0.035, 0.03, 0.1, n=8, m=4))


def stealth(m):
    stand(m)
    m.add("Dark", slab([(0, 0.36), (0.52, -0.2), (0.4, -0.26), (0.2, -0.2), (0.0, -0.28), (-0.2, -0.2), (-0.4, -0.26), (-0.52, -0.2)], FLY - 0.03, FLY + 0.03))
    m.add("Dark", ellipsoid((0, FLY + 0.02, 0.1), 0.05, 0.05, 0.14, n=8, m=4))
    for s in (-1, 1):
        m.add("TeamColor", tube((s * 0.3, FLY + 0.022, -0.12), 0.005, 0.035, axis="y", n=10))
    m.add("TeamColor", block((0, FLY + 0.022, 0.2), (0.04, 0.004, 0.05)))


def bomb(m):
    stand(m)
    m.add("Dark", ellipsoid((0, FLY, 0), 0.15, 0.15, 0.3, n=8, m=6))
    m.add("Yellow", tube((0, FLY, 0.1), 0.06, 0.152, n=8))
    m.add("TeamColor", tube((0, FLY, -0.02), 0.04, 0.152, n=8))
    for rz in (0.0, math.pi / 2):
        m.add("Dark", mov(rot(block((0, 0, 0), (0.4, 0.012, 0.12)), rz=rz), 0, FLY, -0.25))
    m.add("Steel", tube((0, FLY, 0.28), 0.03, 0.02, axis="z", n=8))


def missile(m, scale=1.0, warhead="Steel"):
    stand(m)
    r, length = 0.045 * scale, 0.56 * scale
    m.add("Grey", tube((0, FLY, -0.04), length, r, n=8))
    m.add(warhead, tube((0, FLY, length / 2 - 0.04 + 0.08 * scale), 0.16 * scale, r, 0.0, n=8))
    m.add("TeamColor", tube((0, FLY, 0.04), 0.06 * scale, r * 1.06, n=8))
    for k in range(4):
        a = k * math.pi / 2
        fin = [(0, r * 0.9, -0.3 * scale), (0, r + 0.1 * scale, -0.36 * scale), (0, r + 0.1 * scale, -0.3 * scale), (0, r * 0.9, -0.18 * scale)]
        m.add("Dark", mov(rot(extrude_x([(z, y) for _, y, z in fin], -0.006, 0.006), rz=a), 0, FLY, 0))
    m.add("Dark", tube((0, FLY, -length / 2 - 0.03), 0.04, r * 0.7, r * 1.0, n=8))


def helicopter(m):
    y = 0.2
    m.add("Iron", ellipsoid((0, y, 0.03), 0.15, 0.12, 0.27, n=8, m=6))
    m.add("Teal", ellipsoid((0, y + 0.02, 0.15), 0.08, 0.06, 0.1, n=8, m=4))
    m.add("Iron", tube((0, y + 0.02, -0.3), 0.36, 0.045, 0.014, n=8))
    m.add("TeamColor", tube((0, y + 0.02, -0.24), 0.05, 0.034, n=8))
    m.add("Iron", extrude_x([(-0.44, y + 0.02), (-0.4, y + 0.14), (-0.46, y + 0.14), (-0.48, y + 0.02)], -0.006, 0.006))
    m.add("Dark", tube((0.012, y + 0.12, -0.46), 0.012, 0.06, axis="x", n=10))
    m.add("Steel", tube((0, y + 0.15, 0.0), 0.12, 0.012, axis="y", n=6))
    m.add("Steel", block((0, y + 0.215, 0.0), (0.9, 0.012, 0.09)))  # T307: broad rotor blades, visible from above
    m.add("Steel", block((0, y + 0.215, 0.0), (0.09, 0.012, 0.9)))
    m.add("Yellow", block((0.4, y + 0.222, 0.0), (0.1, 0.004, 0.09)))
    m.add("Yellow", block((0.0, y + 0.222, 0.4), (0.09, 0.004, 0.1)))
    for s in (-1, 1):
        m.add("Dark", block((s * 0.09, 0.012, 0.03), (0.014, 0.014, 0.4)))
        for z in (-0.06, 0.12):
            m.add("Dark", block((s * 0.085, 0.07, z), (0.012, 0.13, 0.012)))
        m.add("Steel", tube((s * 0.14, y - 0.01, 0.06), 0.14, 0.014, n=6))
        m.add("TeamColor", block((s * 0.108, y + 0.03, 0.0), (0.012, 0.04, 0.16)))


ARCHETYPES = {
    "oared": oared, "work_boat": work_boat, "sailer": sailer, "ironclad": ironclad, "warship": warship, "carrier": carrier, "submarine": submarine, "great_admiral": great_admiral,
    "ram": ram, "catapult": catapult, "ballista": ballista, "trebuchet": trebuchet, "hwacha": hwacha, "cannon": cannon, "artillery": artillery, "rocket_artillery": rocket_artillery,
    "tank": tank, "landship": landship, "modern_armor": modern_armor, "death_robot": death_robot,
    "propeller": propeller, "jet": jet, "stealth": stealth, "bomb": bomb, "missile": missile, "helicopter": helicopter,
}


# ------------------------------------------------------------------------------------------------------------------------------------------ glTF

def grit(tri, normal, seed):
    """T307: the gritty look of the T295 humanoids in vertex colours (a tiny cost: 4 bytes a vertex, no texture, the pack stays small): every triangle gets its own brightness (worn, patchy surfaces),
    the lowest parts are darkened like mud and soot, faces pointing up are a little lighter (the default view looks down on them). Deterministic: a hash of the triangle's rounded corners, never a random generator."""
    h = seed
    for p in tri:
        for v in p:
            h = (h * 1000003 + int(round(v * 200))) & 0xFFFFFFFF
    patch = 0.80 + 0.20 * (((h >> 7) & 255) / 255.0)
    out = []
    for p in tri:
        dirt = 0.62 + 0.38 * min(1.0, max(0.0, p[1] / 0.25))
        light = 1.0 + 0.10 * max(0.0, normal[1])
        k = min(1.0, patch * dirt * light)
        out.append([int(round(k * 255))] * 3 + [255])
    return out


def encode(model):
    """The glTF document and binary chunk: one scene root node per top-level node (Godot wraps them in a root named after the file), one mesh primitive per material."""
    b = bum.Builder()
    doc = b.doc
    doc["asset"]["generator"] = "build-rigid-models.py (T256, T307)"
    used = sorted({mat for _, mats in model.nodes.values() for mat in mats}, key=lambda it: (it != "TeamColor", it))
    doc["materials"] = [{"name": name, "pbrMetallicRoughness": {"baseColorFactor": MATERIALS[name], "metallicFactor": 0.0, "roughnessFactor": 1.0}} for name in used]
    top = []
    for name, (translation, mats) in model.nodes.items():
        primitives = []
        for mat in used:
            tris = mats.get(mat)
            if not tris:
                continue
            positions, normals, indices, colours = [], [], [], []
            gritty = mat not in ("TeamColor", "Shadow")  # the civ's colour must stay clean, the shadow disc is flat
            for a, b2, c in tris:
                n = cross(sub(b2, a), sub(c, a))
                length = math.sqrt(dot(n, n))
                n = tuple(round(v / length, 5) for v in n)
                if gritty:
                    colours += grit((a, b2, c), n, len(positions))
                for p in (a, b2, c):
                    indices.append(len(positions))
                    positions.append([round(v, 5) for v in p])
                    normals.append(list(n))
            attributes = {"POSITION": b.accessor(positions, "VEC3", 5126, 34962, bounds=True), "NORMAL": b.accessor(normals, "VEC3", 5126, 34962)}
            if gritty:
                attributes["COLOR_0"] = b.accessor(colours, "VEC4", 5121, 34962)
                doc["accessors"][attributes["COLOR_0"]]["normalized"] = True
            primitives.append({"attributes": attributes, "indices": b.accessor(indices, "SCALAR", 5123, 34963), "material": used.index(mat)})
        doc["meshes"].append({"name": name, "primitives": primitives})
        doc["nodes"].append({"name": name, "mesh": len(doc["meshes"]) - 1, "translation": [round(v, 5) for v in translation]})
        top.append(len(doc["nodes"]) - 1)
    doc["scenes"][0]["nodes"] = top
    del doc["skins"], doc["animations"]
    return b.finish()


def extent(model):
    pts = [(p[0] + t[0], p[1] + t[1], p[2] + t[2]) for t, mats in model.nodes.values() for tris in mats.values() for tri in tris for p in tri]
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]


def build(name, spec):
    model = Model()
    ARCHETYPES[spec["build"]](model, **spec.get("args", {}))
    lo, hi = extent(model)
    height = hi[1] - lo[1]
    radius = max(abs(lo[0]), abs(hi[0]), abs(lo[2]), abs(hi[2]))
    if abs(lo[1]) > 0.02 or not 0.4 <= height <= MAX_HEIGHT or radius > MAX_RADIUS:
        raise SystemExit(f"{name}: outside the unit model contract (feet {lo[1]:.3f}, height {height:.3f}, radius {radius:.3f})")
    if "TeamColor" not in {mat for _, mats in model.nodes.values() for mat in mats}:
        raise SystemExit(f"{name}: no TeamColor surface")
    return model


def main(pack):
    data = json.load(open(os.path.join(pack, "attachments.json")))
    models = os.path.join(pack, "models", "unit")
    os.makedirs(models, exist_ok=True)
    manifest_path = os.path.join(pack, "manifest.json")
    manifest = json.load(open(manifest_path))
    failed = False
    for name, spec in data["rigid"]["units"].items():
        if "build" not in spec:  # an entry without an archetype has no generated model (none today; T307 moved the ram here)
            continue
        try:
            doc, binary = encode(build(name, spec))
        except SystemExit as e:
            print(e)
            failed = True
            continue
        size = bum.write_glb(os.path.join(models, name + ".glb"), doc, binary)
        print(f"unit/{name}: {size} bytes (rigid)")
        manifest["keys"][f"unit/{name}"] = f"models/unit/{name}.glb"
    if failed:
        sys.exit(1)
    manifest["keys"] = dict(sorted(manifest["keys"].items()))
    with open(manifest_path, "w") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    if len(sys.argv) > 2:
        print(__doc__)
        sys.exit(64)
    main(sys.argv[1] if len(sys.argv) == 2 else os.path.join(HERE, "..", "Packs", "units"))
