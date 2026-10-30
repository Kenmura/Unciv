#!/usr/bin/env python3
"""T294: builds the Warrior design OPTIONS for the owner's choice of a gritty, realistic unit look (design/UNIT-STYLE.md). None of them is used by the game: the files go to
`design/models/warrior-options/` (not a pack, never shipped, never read by a contract test or a golden) and the current `unit/Warrior` stays the chibi KayKit Barbarian.

    python3 -I Unciv.Godot/tools/build-warrior-options.py <KayKit Characters/gltf folder> [<out folder, default design/models/warrior-options>]

Stdlib only, deterministic. Every option starts from the pipeline's own reduced KayKit body (CC0 1.0, Kay Lousberg; `build-unit-models.py` `reduce_body`: six contract clips, one
skeleton, TeamColor material) and changes it only through levers the pipeline already has or can have as data:

* proportions: every bone gets a world-axis scale of its own segment and of the mesh it owns (`reproportion`: joints, inverse bind matrices, skinned vertices, translation keys of the
  clips and the attachment nodes are all rewritten consistently, so the six clips play unchanged on the new body);
* materials: the surfaces are split into skin, leather, metal, cloth and fur materials with their own roughness, metalness and tiling normal maps, and only the cloth the unit
  wears in its civ's colour keeps the `TeamColor` name (the tint is not smeared over skin and iron any more);
* colour: a muted palette and vertex colours (dirt gradients, soot, bruises, scars, baked shading), multiplied with tiling grain and grime textures;
* extra geometry generated here from lathes, lofts and tubes (cuirass, greaves, helmet, hoplon, spear, fur, beard, bandages): original work for this project, MPL-2.0.

Nothing from Firaxis or any other third party is used. KayKit's meshes and animation library are CC0 1.0.
"""
import importlib.util
import json
import math
import os
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("bum", os.path.join(HERE, "build-unit-models.py"))
bum = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bum)

CT = {5126: ("f", 4), 5123: ("H", 2), 5121: ("B", 1), 5125: ("I", 4)}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


# ------------------------------------------------------------------------------------------------------------------------------------------------- vectors

def vadd(a, b): return [a[0] + b[0], a[1] + b[1], a[2] + b[2]]
def vsub(a, b): return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
def vmul(a, b): return [a[0] * b[0], a[1] * b[1], a[2] * b[2]]
def vscale(a, s): return [a[0] * s, a[1] * s, a[2] * s]
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]
def norm(a):
    n = math.sqrt(dot(a, a)) or 1.0
    return [a[0] / n, a[1] / n, a[2] / n]


def mat_from_quat(q):
    x, y, z, w = q
    return [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]


def mat_mul(a, b): return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
def mat_vec(m, v): return [dot(m[0], v), dot(m[1], v), dot(m[2], v)]
def mat_t(m): return [[m[j][i] for j in range(3)] for i in range(3)]


def mat_inv(m):
    a, b, c = m[0]
    d, e, f = m[1]
    g, h, i = m[2]
    det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g)
    return [[(e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det],
            [(f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det],
            [(d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det]]


# ---------------------------------------------------------------------------------------------------------------------------------------------- accessors

class Model:
    """A reduced glb (json doc plus one binary buffer) with typed accessor access; edits are written back in place."""

    def __init__(self, doc, binary):
        self.doc, self.bin = doc, bytearray(binary)

    def read(self, i):
        a = self.doc["accessors"][i]
        v = self.doc["bufferViews"][a["bufferView"]]
        fmt, size = CT[a["componentType"]]
        n = NC[a["type"]]
        off, stride = v.get("byteOffset", 0) + a.get("byteOffset", 0), v.get("byteStride", size * n)
        return [list(struct.unpack_from("<%d%s" % (n, fmt), self.bin, off + k * stride)) for k in range(a["count"])]

    def write(self, i, rows):
        a = self.doc["accessors"][i]
        v = self.doc["bufferViews"][a["bufferView"]]
        fmt, size = CT[a["componentType"]]
        n = NC[a["type"]]
        off = v.get("byteOffset", 0) + a.get("byteOffset", 0)
        for k, row in enumerate(rows):
            struct.pack_into("<%d%s" % (n, fmt), self.bin, off + k * size * n, *row)
        if "min" in a:
            cols = list(zip(*rows))
            a["min"], a["max"] = [min(c) for c in cols], [max(c) for c in cols]


def node_index(doc, name):
    return next(i for i, n in enumerate(doc["nodes"]) if n.get("name") == name)


# ----------------------------------------------------------------------------------------------------------------------------------------- proportions

def reproportion(m, scales, lift=None):
    """Re-proportions the skinned body in place. `scales` maps a bone name to the world-axis scale (sx, sy, sz) of the segment from that bone to its children and of the mesh it owns;
    a bone without an entry keeps 1. `lift` maps a bone to an extra world-space offset of that bone's subtree (a neck). Rewrites joint translations, the inverse bind matrices, the skinned
    vertices (each vertex follows its weighted bones: v' = sum w (P'_j + S_j * (v - P_j))), the normals and every translation key of every clip, so the clips fit the new body."""
    doc = m.doc
    nodes = doc["nodes"]
    parent = {}
    for i, n in enumerate(nodes):
        for c in n.get("children", []):
            parent[c] = i
    skin = doc["skins"][0]
    world = {}

    def local(i):
        n = nodes[i]
        r = mat_from_quat(n.get("rotation", [0, 0, 0, 1]))
        s = n.get("scale", [1, 1, 1])
        return [[r[a][b] * s[b] for b in range(3)] for a in range(3)], n.get("translation", [0, 0, 0])

    def world_of(i):
        if i not in world:
            lm, lt = local(i)
            if i in parent:
                pm, pt = world_of(parent[i])
                world[i] = (mat_mul(pm, lm), vadd(pt, mat_vec(pm, lt)))
            else:
                world[i] = (lm, list(lt))
        return world[i]

    for i in range(len(nodes)):
        world_of(i)
    name = lambda i: nodes[i].get("name", "")
    S = lambda i: scales.get(name(i), (1.0, 1.0, 1.0))
    newpos = {}

    def new_position(i):
        if i not in newpos:
            if i not in parent:
                newpos[i] = list(world[i][1])
            else:
                p = parent[i]
                edge = vsub(world[i][1], world[p][1])
                newpos[i] = vadd(new_position(p), vmul(S(p), edge))
                if name(i) in (lift or {}):
                    newpos[i] = vadd(newpos[i], lift[name(i)])
        return newpos[i]

    for i in range(len(nodes)):
        new_position(i)
    # the translation-key map of a joint: its parent's world frame, the parent's segment scale
    keymap = {}
    for i in range(len(nodes)):
        if i in parent:
            pm = world[parent[i]][0]
            keymap[i] = (pm, mat_inv(pm), S(parent[i]))
    for i in range(len(nodes)):
        if i in parent:
            pm = world[parent[i]][0]
            nodes[i]["translation"] = mat_vec(mat_inv(pm), vsub(newpos[i], newpos[parent[i]]))
            if name(i) in (lift or {}):
                pass
    joints = skin["joints"]
    ibm = m.read(skin["inverseBindMatrices"])
    newibm = []
    for j in joints:
        # glTF matrices are column-major: the translation sits in the last column (elements 12..14)
        rot = world[j][0]
        pos = newpos[j]
        inv = mat_inv(rot)
        t = mat_vec(inv, pos)
        newibm.append([inv[0][0], inv[1][0], inv[2][0], 0, inv[0][1], inv[1][1], inv[2][1], 0, inv[0][2], inv[1][2], inv[2][2], 0, -t[0], -t[1], -t[2], 1])
    m.write(skin["inverseBindMatrices"], newibm)
    for mesh_node in nodes:
        if "skin" not in mesh_node or "mesh" not in mesh_node:
            continue
        prim = doc["meshes"][mesh_node["mesh"]]["primitives"][0]
        pos = m.read(prim["attributes"]["POSITION"])
        nrm = m.read(prim["attributes"]["NORMAL"])
        jn = m.read(prim["attributes"]["JOINTS_0"])
        wt = m.read(prim["attributes"]["WEIGHTS_0"])
        outp, outn = [], []
        for v, n, js, ws in zip(pos, nrm, jn, wt):
            nv, nn = [0.0, 0.0, 0.0], [0.0, 0.0, 0.0]
            total = sum(ws) or 1.0
            for jj, w in zip(js, ws):
                if w <= 0:
                    continue
                b = joints[int(jj)]
                s = S(b)
                nv = vadd(nv, vscale(vadd(newpos[b], vmul(s, vsub(v, world[b][1]))), w / total))
                nn = vadd(nn, vscale(vmul(n, [1 / s[0], 1 / s[1], 1 / s[2]]), w / total))
            outp.append(nv)
            outn.append(norm(nn))
        m.write(prim["attributes"]["POSITION"], outp)
        m.write(prim["attributes"]["NORMAL"], outn)
    for anim in doc["animations"]:
        done = set()
        for ch in anim["channels"]:
            if ch["target"]["path"] != "translation" or ch["target"]["node"] not in keymap:
                continue
            sampler = anim["samplers"][ch["sampler"]]
            if sampler["output"] in done:
                continue
            done.add(sampler["output"])
            pm, pinv, s = keymap[ch["target"]["node"]]
            m.write(sampler["output"], [mat_vec(pinv, vmul(s, mat_vec(pm, v))) for v in m.read(sampler["output"])])
    return newpos, world


# ----------------------------------------------------------------------------------------------------------------------------------------------- driver

def load_source(folder, body):
    return bum.read_glb(os.path.join(folder, body + ".glb"))


def bounds(m):
    """Bind-pose bounds of every mesh in the scene (skinned: POSITION as stored; static: through the node chain), like UnitModelContract.Bounds."""
    doc = m.doc
    nodes = doc["nodes"]
    lo, hi = [1e9] * 3, [-1e9] * 3

    def walk(i, rot, pos):
        n = nodes[i]
        r = mat_from_quat(n.get("rotation", [0, 0, 0, 1]))
        s = n.get("scale", [1, 1, 1])
        lm = [[r[a][b] * s[b] for b in range(3)] for a in range(3)]
        rot2, pos2 = mat_mul(rot, lm), vadd(pos, mat_vec(rot, n.get("translation", [0, 0, 0])))
        if "mesh" in n:
            for p in doc["meshes"][n["mesh"]]["primitives"]:
                a = doc["accessors"][p["attributes"]["POSITION"]]
                for cx in (a["min"][0], a["max"][0]):
                    for cy in (a["min"][1], a["max"][1]):
                        for cz in (a["min"][2], a["max"][2]):
                            q = vadd(pos2, mat_vec(rot2, [cx, cy, cz]) if "skin" not in n else [cx, cy, cz])
                            for k in range(3):
                                lo[k], hi[k] = min(lo[k], q[k]), max(hi[k], q[k])
        for c in n.get("children", []):
            walk(c, rot2, pos2)

    for r in doc["scenes"][0]["nodes"]:
        walk(r, [[1, 0, 0], [0, 1, 0], [0, 0, 1]], [0, 0, 0])
    return lo, hi


ATT = json.load(open(os.path.join(HERE, "..", "Packs", "units", "attachments.json")))


def base_model(folder, body="Barbarian", keep=("1H_Axe", "Barbarian_Round_Shield", "Barbarian_Hat", "Barbarian_Cape")):
    doc, binary = load_source(folder, body)
    doc, binary = bum.reduce_body(doc, binary, list(keep), ATT["clips"], ATT["height"])
    m = Model(doc, binary)
    next(n for n in m.doc["nodes"] if n.get("name") == "Rig").pop("scale", None)  # the source units (a 2.2 tall body); `finish` sets the final scale
    return m


def finish(m, height, path):
    """Feet on y = 0 and `height` tile units tall (the whole scene, attachments included), then the glb."""
    lo, hi = bounds(m)
    rig = next(n for n in m.doc["nodes"] if n.get("name") == "Rig")
    rig["scale"] = [height / (hi[1] - lo[1])] * 3
    m.doc["asset"] = {"version": "2.0", "generator": "build-warrior-options.py (T294)"}
    m.doc["buffers"] = [{"byteLength": len(m.bin)}]
    return bum.write_glb(path, m.doc, bytes(m.bin)), (lo, hi)


# ------------------------------------------------------------------------------------------------------------------------------------------- png and noise

def read_png(path):
    """8-bit RGB or RGBA, non-interlaced PNG -> (w, h, bytes of RGB rows)."""
    data = open(path, "rb").read()
    pos, idat, w = 8, b"", 0
    while pos < len(data):
        ln, kind = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + ln]
        if kind == b"IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", body[:10])
            assert depth == 8 and ctype in (2, 6), "only 8-bit RGB/RGBA"
        elif kind == b"IDAT":
            idat += body
        pos += 12 + ln
    bpp = 3 if ctype == 2 else 4
    raw = zlib.decompress(idat)
    stride = w * bpp
    rows, prev = [], bytearray(stride)
    for y in range(h):
        f = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a = line[i - bpp] if i >= bpp else 0
            b = prev[i]
            c = prev[i - bpp] if i >= bpp else 0
            if f == 1: line[i] = (line[i] + a) & 255
            elif f == 2: line[i] = (line[i] + b) & 255
            elif f == 3: line[i] = (line[i] + ((a + b) >> 1)) & 255
            elif f == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    return w, h, [bytes(r[k] for x in range(w) for k in range(x * bpp, x * bpp + 3)) for r in rows]


def png_bytes(w, h, rgb_rows):
    raw = b"".join(b"\0" + bytes(r) for r in rgb_rows)

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def hash01(*ints):
    h = 2166136261
    for v in ints:
        h = ((h ^ (v & 0xFFFFFFFF)) * 16777619) & 0xFFFFFFFF
        h ^= h >> 13
        h = (h * 1274126177) & 0xFFFFFFFF
    return (h & 0xFFFFFF) / float(0xFFFFFF)


def smooth(t): return t * t * (3 - 2 * t)


def tile_noise(n, cx, cy, seed):
    """Tileable value noise on an n x n grid with cx x cy lattice cells; values 0..1."""
    lat = [[hash01(i, j, seed) for j in range(cy)] for i in range(cx)]
    out = []
    for y in range(n):
        fy = y * cy / n
        j0 = int(fy)
        ty = smooth(fy - j0)
        row = []
        for x in range(n):
            fx = x * cx / n
            i0 = int(fx)
            tx = smooth(fx - i0)
            a, b = lat[i0 % cx][j0 % cy], lat[(i0 + 1) % cx][j0 % cy]
            c, d = lat[i0 % cx][(j0 + 1) % cy], lat[(i0 + 1) % cx][(j0 + 1) % cy]
            row.append((a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty)
        out.append(row)
    return out


def fbm_tile(n, base, octaves, seed, aniso=(1, 1)):
    acc, amp, tot = [[0.0] * n for _ in range(n)], 1.0, 0.0
    for o in range(octaves):
        f = tile_noise(n, max(1, base * aniso[0] * 2 ** o), max(1, base * aniso[1] * 2 ** o), seed + o)
        for y in range(n):
            ay, fy = acc[y], f[y]
            for x in range(n):
                ay[x] += fy[x] * amp
        tot += amp
        amp *= 0.5
    return [[v / tot for v in r] for r in acc]


def texture_pair(kind, n=128, seed=1):
    """(albedo detail rows, normal map rows) of a tiling surface: grey multipliers around 1 for the albedo, a tangent-space normal map."""
    if kind == "leather":
        a, b = fbm_tile(n, 4, 2, seed), fbm_tile(n, 10, 2, seed + 9)
        h = [[0.6 * a[y][x] + 0.4 * abs(b[y][x] - 0.5) * 2 for x in range(n)] for y in range(n)]
        alb = [[0.88 + 0.2 * a[y][x] for x in range(n)] for y in range(n)]
        strength = 1.5
    elif kind == "metal":
        s = fbm_tile(n, 3, 3, seed, aniso=(1, 8))
        d = fbm_tile(n, 4, 2, seed + 5)
        h = [[0.5 * s[y][x] + 0.5 * d[y][x] for x in range(n)] for y in range(n)]
        alb = [[0.9 + 0.16 * (0.5 * s[y][x] + 0.5 * d[y][x]) for x in range(n)] for y in range(n)]
        strength = 0.5
    elif kind == "cloth":
        a = fbm_tile(n, 5, 3, seed)
        h = [[0.5 + 0.28 * math.sin(x * 2 * math.pi / 4) * math.sin(y * 2 * math.pi / 4) + 0.25 * (a[y][x] - 0.5) for x in range(n)] for y in range(n)]
        alb = [[0.9 + 0.16 * a[y][x] + 0.04 * math.sin(x * 2 * math.pi / 4) for x in range(n)] for y in range(n)]
        strength = 1.2
    elif kind == "grime":
        a, b = fbm_tile(n, 3, 4, seed), fbm_tile(n, 10, 3, seed + 3)
        h = [[0.7 * a[y][x] + 0.3 * b[y][x] for x in range(n)] for y in range(n)]
        alb = [[max(0.35, min(1.05, 0.45 + 0.9 * (0.6 * a[y][x] + 0.4 * b[y][x]))) for x in range(n)] for y in range(n)]
        strength = 2.0
    else:  # skin, fur: pores and fibres
        a = fbm_tile(n, 10, 3, seed)
        h = a
        alb = [[0.94 + 0.1 * a[y][x] for x in range(n)] for y in range(n)]
        strength = 0.6
    alb_rows = [bytes(min(255, int(255 * v * 0.92)) for v in row for _ in range(3)) for row in alb]
    nrm_rows = []
    for y in range(n):
        row = bytearray()
        for x in range(n):
            dx = h[y][(x + 1) % n] - h[y][(x - 1) % n]
            dy = h[(y + 1) % n][x] - h[(y - 1) % n][x]
            v = norm([-dx * strength * 8, -dy * strength * 8, 1.0])
            row += bytes([int((v[0] * 0.5 + 0.5) * 255), int((v[1] * 0.5 + 0.5) * 255), int((v[2] * 0.5 + 0.5) * 255)])
        nrm_rows.append(row)
    return alb_rows, nrm_rows


# ------------------------------------------------------------------------------------------------------------------------------------------- primitives

class Prim:
    """A triangle mesh in the figure's bind space: positions, normals, vertex colours (rgb), skin weights [(joint name, weight)...] per vertex, triangles, one material key."""

    def __init__(self, mat, node="skinned"):
        self.mat, self.node = mat, node
        self.pos, self.nrm, self.col, self.jw, self.tri = [], [], [], [], []

    def vert(self, p, n, c, jw):
        self.pos.append(list(p)), self.nrm.append(list(n)), self.col.append(list(c)), self.jw.append(jw)
        return len(self.pos) - 1

    def smooth_normals(self):
        acc = [[0.0, 0.0, 0.0] for _ in self.pos]
        for a, b, c in self.tri:
            fn = cross(vsub(self.pos[b], self.pos[a]), vsub(self.pos[c], self.pos[a]))
            for i in (a, b, c):
                acc[i] = vadd(acc[i], fn)
        self.nrm = [norm(v) for v in acc]
        return self


def loft(rings, mat, colour, jw_fn, close=True, cap_top=False, cap_bottom=False, node="skinned", flip=False, colour_fn=None):
    """A surface through rings of equal vertex count (counter-clockwise seen from above gives outward normals for a ring stack that goes up)."""
    pr = Prim(mat, node)
    n = len(rings[0])
    idx = []
    for ring in rings:
        idx.append([pr.vert(p, [0, 1, 0], colour_fn(p) if colour_fn else colour, jw_fn(p)) for p in ring])
    for r in range(len(rings) - 1):
        for i in range(n if close else n - 1):
            j = (i + 1) % n
            a, b, c, d = idx[r][i], idx[r][j], idx[r + 1][j], idx[r + 1][i]
            pr.tri += [(a, c, b), (a, d, c)] if not flip else [(a, b, c), (a, c, d)]
    for flag, ring, up in ((cap_top, idx[-1], True), (cap_bottom, idx[0], False)):
        if flag:
            centre = [sum(pr.pos[i][k] for i in ring) / n for k in range(3)]
            ci = pr.vert(centre, [0, 1, 0], colour, jw_fn(centre))
            for i in range(n):
                j = (i + 1) % n
                pr.tri.append((ci, ring[i], ring[j]) if (up != flip) else (ci, ring[j], ring[i]))
    pr.smooth_normals()
    centre = [sum(p[k] for p in pr.pos) / len(pr.pos) for k in range(3)]
    if sum(dot(n, vsub(p, centre)) for p, n in zip(pr.pos, pr.nrm)) < 0:  # wound inside out: flip (the generators are convex-ish)
        pr.tri = [(a, c, b) for a, b, c in pr.tri]
        pr.smooth_normals()
    return pr


def ring(centre, rx, rz, n, fn=None, rot=0.0):
    pts = []
    for i in range(n):
        a = rot + 2 * math.pi * i / n
        k = fn(a) if fn else 1.0
        pts.append([centre[0] + math.sin(a) * rx * k, centre[1], centre[2] + math.cos(a) * rz * k])
    return pts


def lathe(profile, seg, origin, mat, colour, jw_fn, basis=None, node="skinned", colour_fn=None, radial=None):
    """Surface of revolution about the y axis; profile = [(radius, height)] bottom to top; `basis` (3x3) then `origin` place it."""
    basis = basis or [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
    rings = []
    for r, y in profile:
        pts = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            rr = r * (radial(a, y) if radial else 1.0)
            pts.append(vadd(origin, mat_vec(basis, [math.sin(a) * rr, y, math.cos(a) * rr])))
        rings.append(pts)
    return loft(rings, mat, colour, jw_fn, cap_top=profile[-1][0] > 0, cap_bottom=profile[0][0] > 0, node=node, colour_fn=colour_fn)


def tube(path, radii, seg, mat, colour, jw_fn, node="skinned", colour_fn=None, cap=True, rot=0.0):
    rings = []
    for k, p in enumerate(path):
        t = norm(vsub(path[min(k + 1, len(path) - 1)], path[max(k - 1, 0)]))
        ref = [0, 0, 1] if abs(t[2]) < 0.9 else [1, 0, 0]
        u = norm(cross(t, ref))
        v = cross(t, u)
        r = radii[k] if isinstance(radii[k], (tuple, list)) else (radii[k], radii[k])
        rings.append([vadd(p, vadd(vscale(u, math.cos(rot + 2 * math.pi * i / seg) * r[0]), vscale(v, math.sin(rot + 2 * math.pi * i / seg) * r[1]))) for i in range(seg)])
    return loft(rings, mat, colour, jw_fn, cap_top=cap, cap_bottom=cap, node=node, colour_fn=colour_fn)


def box_prim(centre, half, mat, colour, jw_fn, node="skinned", basis=None):
    basis = basis or [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
    pr = Prim(mat, node)
    faces = [([1, 0, 0], [0, 1, 0], [0, 0, 1]), ([-1, 0, 0], [0, 0, 1], [0, 1, 0]), ([0, 1, 0], [0, 0, 1], [1, 0, 0]),
             ([0, -1, 0], [1, 0, 0], [0, 0, 1]), ([0, 0, 1], [1, 0, 0], [0, 1, 0]), ([0, 0, -1], [0, 1, 0], [1, 0, 0])]
    for nrm, u, v in faces:
        quad = []
        for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            loc = [nrm[k] * half[k] + u[k] * su * half[k] + v[k] * sv * half[k] for k in range(3)]
            p = vadd(centre, mat_vec(basis, loc))
            quad.append(pr.vert(p, mat_vec(basis, nrm), colour, jw_fn(p)))
        pr.tri += [(quad[0], quad[1], quad[2]), (quad[0], quad[2], quad[3])]
    return pr


def transform(prims, basis, shift):
    for pr in prims:
        pr.pos = [vadd(shift, mat_vec(basis, p)) for p in pr.pos]
        pr.nrm = [mat_vec(basis, n) for n in pr.nrm]
    return prims


def rot_x(a): return [[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]]
def rot_y(a): return [[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]]
def rot_z(a): return [[math.cos(a), -math.sin(a), 0], [math.sin(a), math.cos(a), 0], [0, 0, 1]]


def vnoise3(p, scale, seed):
    x, y, z = p[0] * scale, p[1] * scale, p[2] * scale
    i, j, k = math.floor(x), math.floor(y), math.floor(z)
    fx, fy, fz = smooth(x - i), smooth(y - j), smooth(z - k)
    out = 0.0
    for di in (0, 1):
        for dj in (0, 1):
            for dk in (0, 1):
                w = (fx if di else 1 - fx) * (fy if dj else 1 - fy) * (fz if dk else 1 - fz)
                out += w * hash01(i + di, j + dj, k + dk, seed)
    return out


# --------------------------------------------------------------------------------------------------------------------------- source surfaces to primitives

CELL_CLASS = {(0, 0): "skin", (1, 0): "hair", (2, 0): "dark", (3, 0): "metal", (4, 0): "metal", (5, 0): "leather", (6, 0): "leather", (7, 0): "hide",
              (0, 1): "team", (1, 1): "team", (2, 1): "wool", (3, 1): "metal", (4, 1): "leather", (5, 1): "wood", (6, 1): "leather", (7, 1): "cloth",
              (0, 2): "metal", (2, 2): "team", (3, 2): "leather", (7, 2): "cloth", (1, 3): "metal"}


def source_prims(m, cfg):
    """Every mesh of the reduced KayKit body as Prims: one per (node, surface class), the class from the palette cell of the triangle, vertex colours from the design's palette,
    cavity shading (concave = darker, convex edges of metal = worn bright) and height-based dirt."""
    doc = m.doc
    out = []
    for node in doc["nodes"]:
        if "mesh" not in node or node.get("name") in cfg.get("drop", ()):
            continue
        prim = doc["meshes"][node["mesh"]]["primitives"][0]
        pos, nrm, uv = m.read(prim["attributes"]["POSITION"]), m.read(prim["attributes"]["NORMAL"]), m.read(prim["attributes"]["TEXCOORD_0"])
        idx = [i[0] for i in m.read(prim["indices"])]
        skinned = "JOINTS_0" in prim["attributes"]
        jn, wt = (m.read(prim["attributes"]["JOINTS_0"]), m.read(prim["attributes"]["WEIGHTS_0"])) if skinned else (None, None)
        neigh = [[] for _ in pos]
        for k in range(0, len(idx), 3):
            for a in range(3):
                neigh[idx[k + a]] += [idx[k + (a + 1) % 3], idx[k + (a + 2) % 3]]
        # concave surface bends away from its normal: cavity > 0
        cav = []
        for v, n, nb in zip(pos, nrm, neigh):
            if not nb:
                cav.append(0.0)
                continue
            avg = [sum(pos[j][c] for j in nb) / len(nb) for c in range(3)]
            size = sum(math.sqrt(dot(vsub(pos[j], v), vsub(pos[j], v))) for j in nb) / len(nb) or 1.0
            cav.append(max(-1.0, min(1.0, dot(vsub(avg, v), n) / size * 2.0)))
        per = {}
        for k in range(0, len(idx), 3):
            tri = idx[k:k + 3]
            u = sum(uv[i][0] for i in tri) / 3
            v = sum(uv[i][1] for i in tri) / 3
            cell = (min(7, int(u * 8)), min(3, int(v * 4)))
            cls = cfg["classify"](node.get("name", ""), CELL_CLASS.get(cell, "cloth"), cell)
            cls = "leather" if cls == "wood" else cls  # hafts: leather material (a separate wood material rendered white in Godot 4.7, cause not found)
            if cls is None:
                continue
            pr = per.setdefault(cls, Prim(cls, "skinned" if skinned else node["name"]))
            remap = {}
            for i in tri:
                if i not in remap:
                    p = pos[i]
                    jw = [(int(j), w) for j, w in zip(jn[i], wt[i]) if w > 0] if skinned else []
                    col = cfg["colour"](cls, p, nrm[i], cav[i], skinned, node.get("name", ""))
                    remap[i] = pr.vert(p, nrm[i], col, jw)
            pr.tri.append(tuple(remap[i] for i in tri))
        out += list(per.values())
    return out


# ------------------------------------------------------------------------------------------------------------------------------------------------ emission

def add_accessor(m, rows, kind, comp, target=None, bounds=False, normalized=False):
    width = NC[kind]
    fmt = CT[comp][0]
    flat = [x for r in rows for x in (r if isinstance(r, (list, tuple)) else [r])]
    chunk = struct.pack("<%d%s" % (len(flat), fmt), *flat)
    while len(m.bin) % 4:
        m.bin.append(0)
    view = {"buffer": 0, "byteOffset": len(m.bin), "byteLength": len(chunk)}
    if target:
        view["target"] = target
    m.bin += chunk
    m.doc["bufferViews"].append(view)
    acc = {"bufferView": len(m.doc["bufferViews"]) - 1, "componentType": comp, "count": len(rows), "type": kind}
    if normalized:
        acc["normalized"] = True
    if bounds:
        cols = list(zip(*rows))
        acc["min"], acc["max"] = [min(c) for c in cols], [max(c) for c in cols]
    m.doc["accessors"].append(acc)
    return len(m.doc["accessors"]) - 1


def add_image(m, rows):
    png = png_bytes(len(rows[0]) // 3, len(rows), rows)
    while len(m.bin) % 4:
        m.bin.append(0)
    m.doc["bufferViews"].append({"buffer": 0, "byteOffset": len(m.bin), "byteLength": len(png)})
    m.bin += png
    m.doc.setdefault("images", []).append({"bufferView": len(m.doc["bufferViews"]) - 1, "mimeType": "image/png"})
    return len(m.doc["images"]) - 1


def emit(m, prims, materials, uv_scale=5.0, fix_nodes=None):
    """Replaces every mesh of the model by the prims: skinned ones on one node, the others on the node they name; one glTF primitive per material (named in `materials`).
    Box-projected UVs (the plane of each triangle's dominant normal axis, uv_scale tiles per source unit) for the tiling detail textures; vertices are split per axis."""
    doc = m.doc
    nodes = doc["nodes"]
    doc["images"], doc["textures"], doc["samplers"], doc["materials"], doc["meshes"] = [], [], [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}], [], []
    mat_index, tex_cache = {}, {}

    def texture(kind, which, seed):
        key = (kind, which, seed)
        if key not in tex_cache:
            alb, nrm = texture_pair(kind, 128, seed)
            img = add_image(m, alb if which == "alb" else nrm)
            doc["textures"].append({"sampler": 0, "source": img})
            tex_cache[key] = len(doc["textures"]) - 1
        return tex_cache[key]

    for key, spec in materials.items():
        mat = {"name": spec.get("name", key.capitalize()), "doubleSided": spec.get("double", False),
               "pbrMetallicRoughness": {"baseColorFactor": list(spec.get("tint", [1, 1, 1])) + [1.0], "metallicFactor": spec.get("metal", 0.0), "roughnessFactor": spec.get("rough", 0.8)}}
        if spec.get("tex"):
            mat["pbrMetallicRoughness"]["baseColorTexture"] = {"index": texture(spec["tex"], "alb", spec.get("seed", 1))}
            mat["normalTexture"] = {"index": texture(spec["tex"], "nrm", spec.get("seed", 1)), "scale": spec.get("normal", 1.0)}
        doc["materials"].append(mat)
        mat_index[key] = len(doc["materials"]) - 1
    groups = {}
    for pr in prims:
        groups.setdefault(pr.node, []).append(pr)
    skinned_nodes = [i for i, n in enumerate(nodes) if "skin" in n and "mesh" in n]
    rig = next(i for i, n in enumerate(nodes) if n.get("name") == "Rig")
    keep_body = skinned_nodes[0] if skinned_nodes else None
    for i in skinned_nodes[1:]:
        nodes[rig]["children"].remove(i)
    for i, n in enumerate(nodes):
        n.pop("mesh", None)
    if keep_body is not None:
        nodes[keep_body]["name"] = "Warrior"
        nodes[keep_body]["skin"] = 0
    for node_key, plist in groups.items():
        primitives = []
        for pr in plist:
            if not pr.tri:
                continue
            verts, rows_p, rows_n, rows_uv, rows_c, rows_j, rows_w, idx = {}, [], [], [], [], [], [], []
            for a, b, c in pr.tri:
                fn = cross(vsub(pr.pos[b], pr.pos[a]), vsub(pr.pos[c], pr.pos[a]))
                ax = max(range(3), key=lambda k: abs(fn[k]))
                for vi in (a, b, c):
                    key = (vi, ax)
                    if key not in verts:
                        p = pr.pos[vi]
                        uv = [p[2], p[1]] if ax == 0 else [p[0], p[2]] if ax == 1 else [p[0], p[1]]
                        verts[key] = len(rows_p)
                        rows_p.append(p), rows_n.append(pr.nrm[vi]), rows_uv.append([uv[0] * uv_scale, -uv[1] * uv_scale])
                        rows_c.append([max(0.0, min(1.0, x)) ** 2.2 for x in pr.col[vi]] + [1.0])  # glTF vertex colours are linear; the palettes are written as sRGB
                        if pr.node == "skinned":
                            jw = sorted(pr.jw[vi], key=lambda t: -t[1])[:4]
                            tot = sum(w for _, w in jw) or 1.0
                            jw = jw + [(0, 0.0)] * (4 - len(jw))
                            rows_j.append([j for j, _ in jw]), rows_w.append([w / tot for _, w in jw])
                    idx.append(verts[key])
            attrs = {"POSITION": add_accessor(m, rows_p, "VEC3", 5126, 34962, True), "NORMAL": add_accessor(m, rows_n, "VEC3", 5126, 34962),
                     "TEXCOORD_0": add_accessor(m, rows_uv, "VEC2", 5126, 34962), "COLOR_0": add_accessor(m, [[int(round(x * 255)) for x in r] for r in rows_c], "VEC4", 5121, 34962, normalized=True)}
            if pr.node == "skinned":
                attrs["JOINTS_0"] = add_accessor(m, rows_j, "VEC4", 5121, 34962)
                attrs["WEIGHTS_0"] = add_accessor(m, rows_w, "VEC4", 5126, 34962)
            primitives.append({"attributes": attrs, "indices": add_accessor(m, idx, "SCALAR", 5123, 34963), "material": mat_index[pr.mat]})
        doc["meshes"].append({"name": "Mesh_" + str(node_key), "primitives": primitives})
        if node_key == "skinned":
            nodes[keep_body]["mesh"] = len(doc["meshes"]) - 1
        else:
            target = next(n for n in nodes if n.get("name") == node_key)
            target["mesh"] = len(doc["meshes"]) - 1


def compact(m):
    """Drops accessors, views and images nothing refers to any more (the old meshes) and renumbers the rest."""
    doc = m.doc
    used = set()
    for mesh in doc["meshes"]:
        for p in mesh["primitives"]:
            used.update(p["attributes"].values())
            used.add(p["indices"])
    for sk in doc["skins"]:
        used.add(sk["inverseBindMatrices"])
    for a in doc["animations"]:
        for s in a["samplers"]:
            used.update((s["input"], s["output"]))
    order = sorted(used)
    amap = {a: i for i, a in enumerate(order)}
    for mesh in doc["meshes"]:
        for p in mesh["primitives"]:
            p["attributes"] = {k: amap[v] for k, v in p["attributes"].items()}
            p["indices"] = amap[p["indices"]]
    for sk in doc["skins"]:
        sk["inverseBindMatrices"] = amap[sk["inverseBindMatrices"]]
    for a in doc["animations"]:
        for s in a["samplers"]:
            s["input"], s["output"] = amap[s["input"]], amap[s["output"]]
    accs = [doc["accessors"][a] for a in order]
    views = sorted({a["bufferView"] for a in accs} | {i["bufferView"] for i in doc.get("images", [])})
    vmap, out, nv = {}, bytearray(), []
    for v in views:
        view = doc["bufferViews"][v]
        chunk = m.bin[view.get("byteOffset", 0):view.get("byteOffset", 0) + view["byteLength"]]
        vmap[v] = len(nv)
        nv.append(dict(view, buffer=0, byteOffset=len(out)))
        out += chunk
        while len(out) % 4:
            out.append(0)
    for a in accs:
        a["bufferView"] = vmap[a["bufferView"]]
    for i in doc.get("images", []):
        i["bufferView"] = vmap[i["bufferView"]]
    doc["accessors"], doc["bufferViews"] = accs, nv
    m.bin = out


def triangle_count(m):
    return sum(m.doc["accessors"][p["indices"]]["count"] // 3 for mesh in m.doc["meshes"] for p in mesh["primitives"])


# --------------------------------------------------------------------------------------------------------------------------------------------- the designs

P_REAL = dict(scales={"root": (1, 2.2, 1), "hips": (0.74, 1.1, 0.6), "spine": (0.72, 1.1, 0.52), "chest": (0.74, 1.1, 0.52), "head": (0.215, 0.26, 0.245),
                      "upperarm.l": (1, 0.62, 0.62), "upperarm.r": (1, 0.62, 0.62), "lowerarm.l": (1, 0.62, 0.62), "lowerarm.r": (1, 0.62, 0.62),
                      "wrist.l": (0.8, 0.7, 0.7), "wrist.r": (0.8, 0.7, 0.7), "hand.l": (0.8, 0.7, 0.7), "hand.r": (0.8, 0.7, 0.7),
                      "upperleg.l": (0.8, 2.2, 0.7), "upperleg.r": (0.8, 2.2, 0.7), "lowerleg.l": (0.72, 2.2, 0.7), "lowerleg.r": (0.72, 2.2, 0.7),
                      "foot.l": (0.7, 0.85, 0.85), "foot.r": (0.7, 0.85, 0.85)}, lift={"head": [0, 0.06, 0]})
ATT_REAL = {"Barbarian_Hat": (0.215, 0.26, 0.245), "Barbarian_Cape": (0.75, 1.1, 0.52), "Barbarian_Round_Shield": (0.85, 0.85, 0.85), "1H_Axe": (0.6, 0.6, 0.6)}


def scale_attachments(m, att):
    for n in m.doc["nodes"]:
        s = att.get(n.get("name"))
        if s:
            n["scale"] = list(s)
            n["translation"] = vmul(n.get("translation", [0, 0, 0]), s)


class Ctx:
    """What a generator needs after the body was re-proportioned: joint indices in the skin, joint positions, the head's box."""

    def __init__(self, m, newpos):
        self.m = m
        doc = m.doc
        self.names = [doc["nodes"][j]["name"] for j in doc["skins"][0]["joints"]]
        self.pos = {doc["nodes"][j]["name"]: newpos[j] for j in doc["skins"][0]["joints"]}
        head = next(n for n in doc["nodes"] if n.get("name") == "Barbarian_Head")
        a = doc["accessors"][doc["meshes"][head["mesh"]]["primitives"][0]["attributes"]["POSITION"]]
        self.head_lo, self.head_hi = a["min"], a["max"]
        body = next(n for n in doc["nodes"] if n.get("name") == "Barbarian_Body")
        b = doc["accessors"][doc["meshes"][body["mesh"]]["primitives"][0]["attributes"]["POSITION"]]
        self.body_lo, self.body_hi = b["min"], b["max"]

    def ji(self, name): return self.names.index(name)
    def only(self, name):
        j = self.ji(name)
        return lambda p: [(j, 1.0)]


def make_colour(palette, cavity=0.35, wear=0.0, dirt=0.0, dirt_colour=(0.22, 0.17, 0.12), dirt_top=0.9, vary=0.07, soot=0.0, seed=3, shade=0.0, paint=False, blood=0.0, cool=0.0):
    """Vertex colour of a surface point: the class's palette colour, low-frequency variation, cavity shading, bright worn edges on metal, dirt creeping up from the ground, soot on upward faces, baked sky/ground shading."""
    def colour(cls, p, n, cav, skinned, node):
        base = palette[cls]
        v = 1.0 + (vnoise3(p, 7.0, seed) - 0.5) * 2 * vary
        k = v * (1.0 - cavity * max(0.0, cav))
        if cls == "metal" and wear:
            k *= 1.0 + wear * max(0.0, -cav) * 1.5
        k *= 1.0 + shade * (n[1] * 0.5 - 0.1)
        col = [c * k for c in base]
        if dirt:
            h = max(0.0, 1.0 - p[1] / dirt_top) if skinned or True else 0.0
            amt = min(1.0, dirt * h * (0.55 + 0.9 * vnoise3(p, 9.0, seed + 5)))
            col = [c * (1 - amt) + d * amt for c, d in zip(col, dirt_colour)]
        if paint and cls == "skin" and skinned and math.sin(p[1] * 34 + p[0] * 6) > 0.78:
            col = [0.16, 0.27, 0.42]  # woad stripes
        if blood and cls in ("team", "cloth", "wool", "skin", "leather"):
            amt = max(0.0, min(1.0, (vnoise3(p, 5.0, seed + 21) - (1.0 - blood)) * 6))
            col = [c * (1 - amt) + b * amt for c, b in zip(col, (0.28, 0.06, 0.04))]
        if cool:
            sh = max(0.0, -n[1] + 0.2) * cool
            col = [col[0] * (1 - 0.25 * sh), col[1] * (1 - 0.1 * sh), min(1.0, col[2] * (1 + 0.25 * sh))]
        if soot:
            amt = min(1.0, soot * max(0.0, n[1]) * (0.3 + vnoise3(p, 6.0, seed + 9)))
            col = [c * (1 - amt) + 0.05 * amt for c in col]
        return col
    return colour


def helmet(ctx, mat, colour, crest=None, cheek=True, height=0.55):
    lo, hi = ctx.head_lo, ctx.head_hi
    cx, cz = (lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2 + 0.005
    r = max(hi[0] - lo[0], hi[2] - lo[2]) / 2 * 1.12
    y0 = lo[1] + (hi[1] - lo[1]) * height
    top = hi[1] + 0.02
    h = top - y0
    jw = ctx.only("head")
    prof = [(r * 1.02, y0), (r * 1.04, y0 + h * 0.05), (r * 0.98, y0 + h * 0.3), (r * 0.82, y0 + h * 0.6), (r * 0.5, y0 + h * 0.85), (r * 0.2, y0 + h * 0.97), (0.0, top)]
    out = [lathe(prof, 14, [cx, 0, cz], mat, colour, jw)]
    out.append(box_prim([cx, y0 - 0.04, hi[2] * 0.99 + 0.012], [0.007, 0.05, 0.005], mat, colour, jw))
    if cheek:
        for sx in (-1, 1):
            out.append(box_prim([cx + sx * r * 0.98, y0 - 0.035, cz], [0.005, 0.045, 0.045], mat, colour, jw))
    if crest:
        path = []
        for i in range(9):
            a = math.pi * (i / 8 - 0.5) * 1.15
            path.append([cx, y0 + h * 0.2 + math.cos(a) * (h * 0.85 + crest[1]), cz + math.sin(a) * (r * 0.95 + 0.01)])
        out.append(tube(path, [(0.006, crest[0])] * 9, 6, crest[2], crest[3], jw, cap=True))
    return out


WEAPON_NODES = ("1H_Axe", "Barbarian_Round_Shield")


def classify_default(node, cls, cell):
    if node == "Barbarian_Cape":
        return "team"
    if node in WEAPON_NODES and cell == (2, 2):
        return "iron"
    return cls


def make(folder, path, cfg):
    m = base_model(folder, cfg.get("body", "Barbarian"), cfg.get("keep", ("1H_Axe", "Barbarian_Round_Shield", "Barbarian_Hat", "Barbarian_Cape")))
    newpos, _ = reproportion(m, **cfg["prop"])
    scale_attachments(m, cfg.get("att", {}))
    ctx = Ctx(m, newpos)
    if cfg.get("nodes"):
        cfg["nodes"](ctx)
    prims = source_prims(m, cfg)
    if cfg.get("post"):
        prims = cfg["post"](prims)
    prims += cfg["extras"](ctx) if cfg.get("extras") else []
    emit(m, prims, cfg["materials"], cfg.get("uv_scale", 5.0))
    compact(m)
    size, (lo, hi) = finish(m, cfg.get("height", 0.85), path)
    head = ctx.head_hi[1] - ctx.head_lo[1]
    print(os.path.basename(path), "%d bytes, %d triangles, %.1f heads tall (raw height %.2f, head %.2f)" % (size, triangle_count(m), (hi[1] - lo[1]) / head, hi[1] - lo[1], head))
    return m


def build_d1(folder, out):
    """Proportions only: the current materials, hat, cape, axe and shield, on a 7 head body."""
    m = base_model(folder)
    newpos, _ = reproportion(m, **P_REAL)
    scale_attachments(m, ATT_REAL)
    ctx = Ctx(m, newpos)
    size, (lo, hi) = finish(m, 0.85, os.path.join(out, "1-proportions.glb"))
    print("1-proportions.glb", size, "bytes, %.1f heads tall" % ((hi[1] - lo[1]) / (ctx.head_hi[1] - ctx.head_lo[1])))


def d2_materials():
    return {
        "skin": dict(rough=0.72, tex="skin", seed=11, normal=0.3), "hair": dict(rough=0.9, tex="skin", seed=12, normal=0.4), "dark": dict(rough=0.45),
        "metal": dict(name="Bronze", metal=0.85, rough=0.4, tex="metal", seed=13, normal=0.25), "iron": dict(metal=0.8, rough=0.55, tex="metal", seed=20, normal=0.25), "leather": dict(rough=0.62, tex="leather", seed=14, normal=0.5),
        "hide": dict(rough=0.92, tex="leather", seed=15, normal=0.5, double=True), "team": dict(name="TeamColor", rough=0.92, tex="cloth", seed=16, normal=0.5, double=True),
        "wool": dict(rough=1.0, tex="cloth", seed=17, normal=0.4), "wood": dict(name="Haft", rough=0.7, tex="leather", seed=18, normal=0.4), "cloth": dict(rough=0.95, tex="cloth", seed=19, normal=0.5),
    }


D2_PALETTE = {"skin": (0.60, 0.43, 0.33), "hair": (0.58, 0.55, 0.50), "dark": (0.07, 0.06, 0.06), "metal": (0.60, 0.42, 0.22), "iron": (0.22, 0.22, 0.24), "leather": (0.29, 0.19, 0.12),
              "hide": (0.33, 0.27, 0.21), "team": (0.47, 0.45, 0.41), "wool": (0.55, 0.51, 0.44), "wood": (0.36, 0.25, 0.15), "cloth": (0.38, 0.35, 0.30)}


def cfg_d2():
    palette = D2_PALETTE
    colour = make_colour(palette, cavity=0.45, wear=0.5, dirt=0.35, dirt_top=0.75)
    return dict(prop=P_REAL, att=ATT_REAL, drop=("Barbarian_Hat",), classify=classify_default, colour=colour, materials=d2_materials(),
                extras=lambda c: helmet(c, "metal", colour("metal", [0, 1.5, 0], [0, 1, 0], 0.0, True, ""), crest=None))




# ----------------------------------------------------------------------------------------------------------------------------------- generated equipment

def add_child_node(m, parent, name, translation=(0, 0, 0), rotation=(0, 0, 0, 1), scale=(1, 1, 1)):
    nodes = m.doc["nodes"]
    nodes.append({"name": name, "translation": list(translation), "rotation": list(rotation), "scale": list(scale)})
    nodes[node_index(m.doc, parent)].setdefault("children", []).append(len(nodes) - 1)


def torso_rings(ctx, y0, y1, n_rings, seg, widen, depth, shape, bump=None):
    lo, hi = ctx.body_lo, ctx.body_hi
    rx0, rz0 = (hi[0] - lo[0]) / 2, (hi[2] - lo[2]) / 2
    cz = (hi[2] + lo[2]) / 2
    rings = []
    for k in range(n_rings):
        t = k / (n_rings - 1)
        y = y0 + (y1 - y0) * t
        f = shape(t)
        rings.append(ring([0.0, y, cz], rx0 * widen * f, rz0 * depth * f, seg, (lambda a, t=t: bump(a, t)) if bump else None))
    return rings


def cuirass(ctx, mat, colour, muscle=True):
    jw = lambda p: [(ctx.ji("chest"), 1.0)] if p[1] > ctx.pos["spine"][1] + 0.08 else [(ctx.ji("spine"), 1.0)]
    y0, y1 = ctx.pos["hips"][1] + 0.06, ctx.pos["chest"][1] + 0.1

    def bump(a, t):
        front = abs(math.atan2(math.sin(a), math.cos(a)))  # 0 = the front (+z)
        pec = 0.1 * math.exp(-((front - 0.5) / 0.28) ** 2) * math.exp(-((t - 0.78) / 0.14) ** 2)
        abs_ = 0.04 * math.exp(-((front - 0.18) / 0.2) ** 2) * math.exp(-((t - 0.3) / 0.22) ** 2)
        return 1.0 + (pec + abs_ if muscle else 0.0)

    rings = torso_rings(ctx, y0, y1, 9, 18, 1.0, 1.08, lambda t: 0.84 + 0.24 * min(1.0, t / 0.75) - 0.22 * max(0.0, (t - 0.75) / 0.25), bump)
    return [loft(rings, mat, colour, jw, cap_bottom=False)]


def greaves(ctx, mat, colour):
    out = []
    for s in ("l", "r"):
        knee, ankle = ctx.pos["lowerleg." + s], ctx.pos["foot." + s]
        path = [[knee[0] + (ankle[0] - knee[0]) * t, knee[1] + (ankle[1] - knee[1]) * t + 0.02 * (1 - t), knee[2] + 0.005] for t in (0.04, 0.3, 0.6, 0.92)]
        out.append(tube(path, [0.066, 0.058, 0.052, 0.058], 10, mat, colour, ctx.only("lowerleg." + s), cap=True))
    return out


def fur_ring(ctx, centre, rx, rz, y_top, y_bot, rows, seg, mat, jw, colour_tip, colour_root, teeth=0.05, sag=1.0):
    """A shaggy collar or kilt: rows of rings down from y_top to y_bot, the lowest cut into teeth; darker at the root, lighter at the tips."""
    rings = []
    for r in range(rows):
        t = r / (rows - 1)
        k = 0.78 + 0.28 * math.sin(t * math.pi * 0.9) * sag + 0.08 * t
        pts = ring([centre[0], y_top + (y_bot - y_top) * t, centre[2]], rx * k, rz * k, seg)
        if r >= rows - 2:
            for i, p in enumerate(pts):
                tooth = (0.0 if i % 2 == 0 else -teeth) * (1.0 if r == rows - 1 else 0.3) - 0.02 * hash01(i, r, 5) * (r == rows - 1)
                p[1] += tooth
        rings.append(pts)
    return loft(rings, mat, colour_root, jw, colour_fn=lambda p: [a + (b - a) * max(0.0, min(1.0, (y_top - p[1]) / (y_top - y_bot))) for a, b in zip(colour_root, colour_tip)], close=True)


def spear(mat_shaft, mat_head, colour_shaft, colour_head, length=2.1, offset=-0.45):
    node = "Spear"
    jw = lambda p: []
    shaft = tube([[0, offset, 0], [0, offset + length * 0.5, 0], [0, offset + length, 0]], [0.012, 0.012, 0.012], 8, mat_shaft, colour_shaft, jw, node=node)
    head = tube([[0, offset + length - 0.01, 0], [0, offset + length + 0.07, 0], [0, offset + length + 0.18, 0], [0, offset + length + 0.28, 0]], [(0.018, 0.006), (0.052, 0.008), (0.04, 0.007), (0.002, 0.002)], 8, mat_head, colour_head, jw, node=node)
    butt = tube([[0, offset - 0.1, 0], [0, offset, 0]], [0.002, 0.016], 8, mat_head, colour_head, jw, node=node)
    return [shaft, head, butt]


def hoplon(mat_face, mat_rim, colour_face, colour_rim, radius=0.5):
    node = "Hoplon"
    jw = lambda p: []
    face = lathe([(0.0, 0.1), (radius * 0.35, 0.095), (radius * 0.75, 0.065), (radius * 0.96, 0.02), (radius * 1.0, 0.0)], 20, [0, 0, 0], mat_face, colour_face, jw, basis=rot_x(math.pi / 2), node=node)
    rim = lathe([(radius * 1.0, 0.0), (radius * 1.03, -0.01), (radius * 1.03, -0.03), (radius * 0.9, -0.03), (radius * 0.9, 0.0)], 20, [0, 0, 0], mat_rim, colour_rim, jw, basis=rot_x(math.pi / 2), node=node)
    back = lathe([(radius * 0.9, -0.03), (radius * 0.4, -0.05), (0.0, -0.05)], 20, [0, 0, 0], mat_rim, colour_rim, jw, basis=rot_x(math.pi / 2), node=node)
    return [face, rim, back]


def ragged_cape(ctx, mat, colour_top, colour_bot, width=0.3, drop=0.85, rags=0.12, seed=4):
    chest, spine = ctx.ji("chest"), ctx.ji("spine")
    top = ctx.pos["chest"][1] + 0.12
    zc = ctx.body_lo[2] - 0.02
    rings = []
    rows, cols = 9, 11
    for r in range(rows):
        t = r / (rows - 1)
        pts = []
        for c in range(cols):
            u = c / (cols - 1) * 2 - 1
            x = u * width * (1 + 0.25 * t)
            y = top - drop * t - (rags * hash01(c, seed) * (t > 0.85) + 0.04 * hash01(c, r, seed + 1) * t)
            z = zc - 0.06 * t - 0.02 * math.sin(u * 3)
            pts.append([x, y, z])
        rings.append(pts)
    jw = lambda p: [(chest, 1.0)] if p[1] > top - 0.35 else [(spine, 1.0)]
    cape = loft(rings, mat, colour_top, jw, close=False, colour_fn=lambda p: [a + (b - a) * max(0, min(1, (top - p[1]) / drop)) for a, b in zip(colour_top, colour_bot)])
    cape.tri += [(a, c, b) for a, b, c in cape.tri]  # both faces (the loft is single sided; the material is double sided too)
    return [cape]


def bandage(ctx, joint_a, joint_b, t0, t1, radius, mat, colour, wraps=6, seg=9):
    a, b = ctx.pos[joint_a], ctx.pos[joint_b]
    out = []
    for k in range(wraps):
        t = t0 + (t1 - t0) * k / max(1, wraps - 1)
        c = [a[i] + (b[i] - a[i]) * t for i in range(3)]
        axis = norm(vsub(b, a))
        ref = [0, 0, 1] if abs(axis[2]) < 0.9 else [1, 0, 0]
        u = norm(cross(axis, ref))
        v = cross(axis, u)
        pts = [[c[i] + (u[i] * math.cos(2 * math.pi * j / seg) + v[i] * math.sin(2 * math.pi * j / seg)) * radius for i in range(3)] for j in range(seg)]
        top = [[q[i] + axis[i] * 0.012 for i in range(3)] for q in pts]
        out.append(loft([pts, top], mat, colour, ctx.only(joint_a), cap_top=False))
    return out


def beard(ctx, mat, colour_root, colour_tip, length=0.1, seg=10):
    lo, hi = ctx.head_lo, ctx.head_hi
    cx = (lo[0] + hi[0]) / 2
    chin = [cx, lo[1] + 0.065, hi[2] - 0.055]
    rings = []
    for k in range(5):
        t = k / 4
        pts = ring([chin[0], chin[1] - length * t, chin[2] + 0.01 * t], (hi[0] - lo[0]) * (0.36 - 0.12 * t), 0.075 * (1 - 0.35 * t), seg, lambda a: 0.6 + 0.4 * max(0.0, math.cos(a)))
        if k == 4:
            for i, p in enumerate(pts):
                p[1] -= 0.03 * (i % 2)
        rings.append(pts)
    return loft(rings, mat, colour_root, ctx.only("head"), close=True, colour_fn=lambda p: [a + (b - a) * max(0, min(1, (chin[1] - p[1]) / length)) for a, b in zip(colour_root, colour_tip)])


# ------------------------------------------------------------------------------------------------------------------------------------------ the six designs

BASE_MATS = {
    "skin": dict(rough=0.72, tex="skin", seed=11, normal=0.3), "hair": dict(rough=0.9, tex="skin", seed=12, normal=0.4), "dark": dict(rough=0.45),
    "metal": dict(name="Bronze", metal=0.85, rough=0.4, tex="metal", seed=13, normal=0.25), "iron": dict(metal=0.8, rough=0.55, tex="metal", seed=20, normal=0.25),
    "leather": dict(rough=0.62, tex="leather", seed=14, normal=0.5), "hide": dict(rough=0.92, tex="leather", seed=15, normal=0.5, double=True),
    "fur": dict(rough=0.95, tex="skin", seed=22, normal=0.4, double=True), "team": dict(name="TeamColor", rough=0.9, tex="cloth", seed=16, normal=0.5, double=True),
    "wool": dict(rough=1.0, tex="cloth", seed=17, normal=0.4), "wood": dict(name="Haft", rough=0.7, tex="leather", seed=18, normal=0.4), "cloth": dict(rough=0.95, tex="cloth", seed=19, normal=0.5),
}


def mats(**over):
    out = {k: dict(v) for k, v in BASE_MATS.items()}
    for k, v in over.items():
        out[k].update(v)
    return out


P_LEAN = dict(scales={**P_REAL["scales"], "hips": (0.66, 1.1, 0.55), "spine": (0.64, 1.12, 0.5), "chest": (0.68, 1.12, 0.5), "head": (0.2, 0.25, 0.23),
                      "upperarm.l": (1, 0.55, 0.55), "upperarm.r": (1, 0.55, 0.55), "lowerarm.l": (1, 0.55, 0.55), "lowerarm.r": (1, 0.55, 0.55),
                      "upperleg.l": (0.68, 2.1, 0.62), "upperleg.r": (0.68, 2.1, 0.62), "lowerleg.l": (0.62, 2.1, 0.62), "lowerleg.r": (0.62, 2.1, 0.62), "root": (1, 2.1, 1)}, lift={"head": [0, 0.06, 0]})
P_BRUTE = dict(scales={**P_REAL["scales"], "hips": (0.86, 1.0, 0.7), "spine": (0.86, 1.0, 0.62), "chest": (0.98, 1.04, 0.64), "head": (0.235, 0.27, 0.26),
                       "upperarm.l": (1, 0.78, 0.78), "upperarm.r": (1, 0.78, 0.78), "lowerarm.l": (1, 0.76, 0.76), "lowerarm.r": (1, 0.76, 0.76),
                       "upperleg.l": (0.86, 2.0, 0.8), "upperleg.r": (0.86, 2.0, 0.8), "lowerleg.l": (0.78, 2.0, 0.78), "lowerleg.r": (0.78, 2.0, 0.78), "root": (1, 2.0, 1)}, lift={"head": [0, 0.05, 0]})
P_MID = dict(scales={**P_REAL["scales"], "hips": (0.84, 1.0, 0.7), "spine": (0.84, 1.0, 0.62), "chest": (0.88, 1.0, 0.62), "head": (0.34, 0.4, 0.36),
                     "upperarm.l": (1, 0.8, 0.8), "upperarm.r": (1, 0.8, 0.8), "lowerarm.l": (1, 0.8, 0.8), "lowerarm.r": (1, 0.8, 0.8),
                     "wrist.l": (1, 1, 1), "wrist.r": (1, 1, 1), "hand.l": (1, 1, 1), "hand.r": (1, 1, 1),
                     "upperleg.l": (0.86, 1.8, 0.8), "upperleg.r": (0.86, 1.8, 0.8), "lowerleg.l": (0.8, 1.8, 0.8), "lowerleg.r": (0.8, 1.8, 0.8), "foot.l": (0.85, 1, 1), "foot.r": (0.85, 1, 1), "root": (1, 1.8, 1)}, lift={"head": [0, 0.03, 0]})


def cfg_d2():
    colour = make_colour(D2_PALETTE, cavity=0.45, wear=0.5, dirt=0.35, dirt_top=0.75)
    return dict(prop=P_REAL, att=ATT_REAL, drop=("Barbarian_Hat",), classify=classify_default, colour=colour, materials=mats(),
                extras=lambda c: helmet(c, "metal", colour("metal", [0, 1.5, 0], [0, 1, 0], 0.0, True, ""), crest=None))


def cfg_d3():
    pal = {"skin": (0.66, 0.48, 0.36), "hair": (0.40, 0.33, 0.26), "dark": (0.06, 0.05, 0.05), "metal": (0.52, 0.50, 0.47), "iron": (0.2, 0.2, 0.21), "leather": (0.24, 0.16, 0.10),
           "hide": (0.30, 0.22, 0.15), "fur": (0.2, 0.15, 0.1), "team": (0.5, 0.45, 0.4), "wool": (0.55, 0.5, 0.42), "wood": (0.33, 0.22, 0.13), "cloth": (0.3, 0.26, 0.2)}
    colour = make_colour(pal, cavity=0.5, wear=0.3, dirt=0.45, dirt_top=0.8, paint=True, vary=0.1)

    def classify(node, cls, cell):
        if node == "Barbarian_Cape":
            return "fur"
        if node == "Barbarian_Round_Shield":
            return "team" if cell == (4, 1) else "iron" if cell in ((2, 2), (6, 1)) else "metal" if cell == (3, 1) else cls
        if node in ("1H_Axe",):
            return "iron" if cell in ((2, 2), (3, 1)) else cls
        if node.startswith("Barbarian_") and cell in ((0, 1), (1, 1), (2, 2)) and node != "Barbarian_Hat":
            return "skin"
        return cls

    def extras(c):
        tip, root = (0.42, 0.34, 0.25), (0.16, 0.11, 0.08)
        out = []
        jw = c.only("chest")
        cz = (c.body_lo[2] + c.body_hi[2]) / 2
        half = (c.body_hi[0] - c.body_lo[0]) / 2
        mant = fur_ring(c, [0, 0, cz], half * 1.12, 0.22, c.pos["chest"][1] + 0.16, c.pos["chest"][1] - 0.05, 4, 16, "fur", jw, tip, root, teeth=0.05)
        for i, p in enumerate(mant.pos):
            if p[0] > 0:
                mant.pos[i] = [p[0] * 1.12, p[1] + 0.015, p[2]]  # heavier on the left shoulder: asymmetric kit
        mant.smooth_normals()
        out.append(mant)
        out.append(fur_ring(c, [0.02, 0, cz], 0.2, 0.17, c.pos["hips"][1] + 0.1, c.pos["hips"][1] - 0.22, 4, 14, "fur", c.only("hips"), tip, root, teeth=0.07, sag=0.5))
        out.append(beard(c, "hair", (0.27, 0.22, 0.17), (0.42, 0.36, 0.3)))
        for s in ("l", "r"):
            out += bandage(c, "lowerleg." + s, "foot." + s, 0.15, 0.55, 0.062, "leather", (0.22, 0.15, 0.1), wraps=5)
        return out

    return dict(prop=P_BRUTE, att={"Barbarian_Hat": (0.235, 0.27, 0.26), "Barbarian_Cape": (0.9, 1.0, 0.6), "Barbarian_Round_Shield": (0.92, 0.92, 0.92), "1H_Axe": (0.65, 0.65, 0.65)},
                classify=classify, colour=colour, materials=mats(metal=dict(name="Iron", metal=0.7, rough=0.5)), extras=extras, height=0.85)


def cfg_d4():
    pal = {"skin": (0.62, 0.45, 0.34), "hair": (0.25, 0.2, 0.15), "dark": (0.06, 0.05, 0.05), "metal": (0.72, 0.5, 0.22), "iron": (0.3, 0.3, 0.3), "leather": (0.33, 0.21, 0.12),
           "hide": (0.33, 0.27, 0.2), "fur": (0.2, 0.15, 0.1), "team": (0.6, 0.56, 0.5), "wool": (0.72, 0.68, 0.58), "wood": (0.42, 0.29, 0.17), "cloth": (0.7, 0.66, 0.56)}
    colour = make_colour(pal, cavity=0.3, wear=0.6, dirt=0.18, dirt_top=0.6)
    bronze = colour("metal", [0, 1.5, 0], [0, 1, 0], 0.0, True, "")

    def classify(node, cls, cell):
        if node == "Barbarian_Cape":
            return "team"
        if node.startswith("Barbarian_") and cell in ((1, 1),) and node != "Barbarian_Body":
            return "skin"
        if node == "Barbarian_LegLeft" or node == "Barbarian_LegRight":
            return "skin" if cell == (7, 1) else "leather" if cls in ("leather", "wool") else cls
        if cls == "wool":
            return "cloth"
        return cls

    def nodes(c):
        add_child_node(c.m, "handslot.r", "Spear", rotation=(0, 0, -0.7071068, 0.7071068))
        add_child_node(c.m, "handslot.l", "Hoplon", translation=(0, 0.017, 0.156), scale=(0.95, 0.95, 0.95))

    def extras(c):
        out = helmet(c, "metal", bronze, crest=(0.035, 0.07, "team", pal["team"]), height=0.5)
        out += cuirass(c, "metal", bronze)
        out += greaves(c, "metal", bronze)
        out.append(fur_ring(c, [0, 0, (c.body_lo[2] + c.body_hi[2]) / 2], 0.2, 0.15, c.pos["hips"][1] + 0.09, c.pos["hips"][1] - 0.15, 3, 18, "leather", c.only("hips"), (0.38, 0.25, 0.14), (0.3, 0.19, 0.11), teeth=0.09, sag=0.2))
        out += spear("leather", "metal", pal["wood"], (0.55, 0.57, 0.6))
        out += hoplon("metal", "metal", bronze, [x * 0.8 for x in bronze])
        return out

    return dict(prop=P_LEAN, att={"Barbarian_Cape": (0.7, 0.62, 0.5)}, keep=("Barbarian_Cape",), classify=classify, colour=colour, materials=mats(wood=dict(), iron=dict(metal=0.85)),
                nodes=nodes, extras=extras, height=0.85)


def cfg_d5():
    pal = {"skin": (0.5, 0.36, 0.28), "hair": (0.42, 0.4, 0.37), "dark": (0.05, 0.04, 0.04), "metal": (0.44, 0.33, 0.19), "iron": (0.17, 0.16, 0.16), "leather": (0.2, 0.13, 0.09),
           "hide": (0.26, 0.21, 0.16), "fur": (0.18, 0.14, 0.1), "team": (0.42, 0.4, 0.36), "wool": (0.45, 0.41, 0.35), "wood": (0.27, 0.19, 0.12), "cloth": (0.28, 0.26, 0.22)}
    colour = make_colour(pal, cavity=0.6, wear=0.7, dirt=0.95, dirt_top=1.35, dirt_colour=(0.13, 0.1, 0.07), soot=0.55, blood=0.38, vary=0.12, seed=7)

    def post(prims):
        for pr in prims:
            if pr.node in ("Barbarian_Round_Shield", "1H_Axe"):
                amp = 0.05 if pr.node == "Barbarian_Round_Shield" else 0.03
                pr.pos = [vadd(p, vscale(n, (vnoise3(p, 6.0, 11) - 0.5) * amp)) for p, n in zip(pr.pos, pr.nrm)]
        return prims

    def extras(c):
        dent = lambda a: 1.0 + 0.07 * math.sin(a * 3 + 1) * (hash01(int(a * 10)) - 0.3)
        out = helmet(c, "metal", colour("metal", [0, 1.5, 0], [0, 1, 0], 0.0, True, ""), crest=None)
        out += ragged_cape(c, "team", (0.34, 0.31, 0.27), (0.2, 0.16, 0.13), width=0.31, drop=0.85)
        out += bandage(c, "lowerarm.l", "wrist.l", 0.1, 0.85, 0.052, "cloth", (0.5, 0.46, 0.38), wraps=7)
        out += bandage(c, "upperarm.r", "lowerarm.r", 0.3, 0.7, 0.05, "cloth", (0.46, 0.4, 0.33), wraps=4)
        return out

    return dict(prop=P_REAL, att=ATT_REAL, drop=("Barbarian_Hat", "Barbarian_Cape"), classify=classify_default, colour=colour, materials=mats(cloth=dict(tex="cloth", normal=0.7), team=dict(double=True)),
                extras=extras, post=post, height=0.85)


def cfg_d6():
    pal = {"skin": (0.86, 0.6, 0.45), "hair": (0.72, 0.7, 0.66), "dark": (0.1, 0.09, 0.12), "metal": (0.62, 0.68, 0.74), "iron": (0.36, 0.4, 0.46), "leather": (0.62, 0.36, 0.2),
           "hide": (0.46, 0.36, 0.3), "fur": (0.3, 0.2, 0.15), "team": (0.62, 0.66, 0.68), "wool": (0.9, 0.86, 0.76), "wood": (0.66, 0.42, 0.22), "cloth": (0.5, 0.52, 0.58)}
    colour = make_colour(pal, cavity=0.35, vary=0.05, shade=0.5, cool=0.5, dirt=0.12, dirt_top=0.4, wear=0.2)
    return dict(prop=P_MID, att={"Barbarian_Hat": (0.34, 0.4, 0.36), "Barbarian_Cape": (0.85, 1.0, 0.55), "Barbarian_Round_Shield": (0.85, 0.85, 0.85), "1H_Axe": (0.6, 0.6, 0.6)},
                classify=classify_default, colour=colour, height=0.8,
                materials=mats(skin=dict(rough=0.8, normal=0.1), leather=dict(rough=0.75, normal=0.15), metal=dict(name="Steel", metal=0.4, rough=0.6, normal=0.1), iron=dict(metal=0.4, rough=0.6, normal=0.1),
                               hide=dict(normal=0.15), team=dict(rough=0.85, normal=0.15), wool=dict(normal=0.1), wood=dict(normal=0.1), cloth=dict(normal=0.15)))


DESIGNS = {"d1": lambda f, o: build_d1(f, o), "d2": lambda f, o: make(f, os.path.join(o, "2-worn-leather-bronze.glb"), cfg_d2()),
           "d3": lambda f, o: make(f, os.path.join(o, "3-fur-barbarian.glb"), cfg_d3()), "d4": lambda f, o: make(f, os.path.join(o, "4-bronze-hoplite.glb"), cfg_d4()),
           "d5": lambda f, o: make(f, os.path.join(o, "5-battle-damaged.glb"), cfg_d5()), "d6": lambda f, o: make(f, os.path.join(o, "6-painterly.glb"), cfg_d6())}


if __name__ == "__main__":
    source = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "migration", "design", "models", "warrior-options")
    os.makedirs(out, exist_ok=True)
    for n in (sys.argv[3:] or list(DESIGNS)):
        DESIGNS[n](source, out)
