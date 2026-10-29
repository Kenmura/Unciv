#!/usr/bin/env python3
"""C083 (from C080): builds the bundled unit model pack `Unciv.Godot/Packs/units` from KayKit's "Adventurers Character Pack 1.0" (CC0 1.0, Kay Lousberg, https://kaylousberg.com).

    python3 -I Unciv.Godot/tools/build-unit-models.py <KayKit Characters/gltf folder> [<pack folder, default Unciv.Godot/Packs/units>]

Stdlib only, no Blender (a Blender route for new art is `blender-export-unit.py`; both produce the same contract, see design/UNIT-MODELS.md section 1). Only the glTF JSON and the binary
chunk are rewritten. The unit list is the data file `attachments.json` of the pack:

* All bodies share one humanoid skeleton ("Rig"), so one animation library fits them all. Every unit keeps its body, the attachments the data file lists (weapon, shield, helmet, hat, cape; the
  rest of the static meshes are dropped), and exactly the six contract clips (idle, walk, attack, attack_ranged, hit, death), each taken from a source animation (per-unit overrides allowed).
* The one material is renamed `TeamColor` (tinted with the civ colour at load) and the rig is scaled so the figure stands `height` tile units tall with its feet at y = 0.
* A mounted unit is a rider on a generated horse rig: a skeleton of rigid boxes (feet at y = 0, facing +Z) with hand-keyed clips, the rider parented to its `saddle` joint. In each clip the rider's
  upper body follows the clip's source animation while legs and hips hold the seated pose.
* `manifest.json` (key -> file) is rewritten from the unit list.
"""
import copy
import json
import math
import os
import struct
import sys

LEG_NODES = {f"{part}.{side}" for part in ("upperleg", "lowerleg", "foot", "toes") for side in ("l", "r")} | {"hips"}
CLIPS = ["idle", "walk", "attack", "attack_ranged", "hit", "death"]


# ---------------------------------------------------------------------------------------------------------------------------------------------- glb io

def read_glb(path):
    data = open(path, "rb").read()
    jlen = struct.unpack("<I", data[12:16])[0]
    doc = json.loads(data[20:20 + jlen])
    blen = struct.unpack("<I", data[20 + jlen:24 + jlen])[0]
    return doc, data[28 + jlen:28 + jlen + blen]


def write_glb(path, doc, binary):
    jbytes = pad(json.dumps(doc, separators=(",", ":"), sort_keys=False).encode(), b" ")
    total = 12 + 8 + len(jbytes) + 8 + len(binary)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<I4s", len(jbytes), b"JSON") + jbytes)
        f.write(struct.pack("<I4s", len(binary), b"BIN\0") + binary)
    return total


def pad(b, fill):
    return b + fill * (-len(b) % 4)


# --------------------------------------------------------------------------------------------------------------------------------- reduce a KayKit body

def node_name(doc, index):
    return doc["nodes"][index].get("name")


def make_clip(doc, name, spec, seat=None):
    """One contract clip from a source animation. With `seat`, the channels of legs and hips come from that animation instead (a rider sits whatever he does with his arms)."""
    by_name = {a["name"]: a for a in doc["animations"]}
    if seat is None:
        return dict(copy.deepcopy(by_name[spec]), name=name)
    samplers, channels = [], []

    def take(anim, wanted):
        for ch in anim["channels"]:
            if wanted(node_name(doc, ch["target"]["node"])):
                samplers.append(dict(anim["samplers"][ch["sampler"]]))
                channels.append({"sampler": len(samplers) - 1, "target": dict(ch["target"])})

    take(by_name[spec], lambda n: n not in LEG_NODES)
    take(by_name[seat], lambda n: n in LEG_NODES)
    return {"name": name, "samplers": samplers, "channels": channels}


def reduce_body(doc, binary, keep, clips, height, seat=None):
    """Keeps the skinned body and the static meshes named in `keep`, the six contract clips and the data they use; renames the material; scales the rig to `height`."""
    doc["animations"] = [make_clip(doc, name, clips[name], seat) for name in CLIPS]
    dropped = {n["name"] for n in doc["nodes"] if "mesh" in n and "skin" not in n and n["name"] not in keep}
    missing = set(keep) - {n["name"] for n in doc["nodes"] if "mesh" in n and "skin" not in n}
    if missing:
        raise SystemExit(f"attachments not in the body: {sorted(missing)}")
    for node in doc["nodes"]:
        if node.get("name") in dropped:
            node.pop("mesh")
    for node in doc["nodes"]:
        if "children" in node:
            kept = [c for c in node["children"] if doc["nodes"][c].get("name") not in dropped]
            if kept:
                node["children"] = kept
            else:
                del node["children"]

    used_meshes = sorted({n["mesh"] for n in doc["nodes"] if "mesh" in n})
    mesh_map = {m: i for i, m in enumerate(used_meshes)}
    for node in doc["nodes"]:
        if "mesh" in node:
            node["mesh"] = mesh_map[node["mesh"]]
    doc["meshes"] = [doc["meshes"][m] for m in used_meshes]

    used_acc = set()
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            used_acc.update(prim["attributes"].values())
            used_acc.add(prim["indices"])
    for skin in doc["skins"]:
        used_acc.add(skin["inverseBindMatrices"])
    for anim in doc["animations"]:
        for s in anim["samplers"]:
            used_acc.update((s["input"], s["output"]))
    acc_order = sorted(used_acc)
    acc_map = {a: i for i, a in enumerate(acc_order)}
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            prim["attributes"] = {k: acc_map[v] for k, v in prim["attributes"].items()}
            prim["indices"] = acc_map[prim["indices"]]
    for skin in doc["skins"]:
        skin["inverseBindMatrices"] = acc_map[skin["inverseBindMatrices"]]
    for anim in doc["animations"]:
        for s in anim["samplers"]:
            s["input"], s["output"] = acc_map[s["input"]], acc_map[s["output"]]
    accessors = [doc["accessors"][a] for a in acc_order]

    used_views = sorted({a["bufferView"] for a in accessors} | {i["bufferView"] for i in doc["images"]})
    view_map, out, views = {}, b"", []
    for v in used_views:
        view = doc["bufferViews"][v]
        chunk = binary[view.get("byteOffset", 0):view.get("byteOffset", 0) + view["byteLength"]]
        view_map[v] = len(views)
        views.append(dict(view, buffer=0, byteOffset=len(out)))
        out = pad(out + chunk, b"\0")
    for a in accessors:
        a["bufferView"] = view_map[a["bufferView"]]
    for i in doc["images"]:
        i["bufferView"] = view_map[i["bufferView"]]
    doc["accessors"], doc["bufferViews"], doc["buffers"] = accessors, views, [{"byteLength": len(out)}]

    doc["materials"][0]["name"] = "TeamColor"
    # height of the skinned body parts in the bind pose (the hat and the weapon sit inside that range), feet at y = 0 in the source
    tops = [doc["accessors"][p["attributes"]["POSITION"]]["max"][1] for m in doc["meshes"] for p in m["primitives"] if "JOINTS_0" in p["attributes"]]
    rig = next(n for n in doc["nodes"] if n.get("name") == "Rig")
    rig["scale"] = [height / max(tops)] * 3
    doc["asset"] = {"version": "2.0", "generator": "build-unit-models.py (C083)"}
    return doc, out


# ------------------------------------------------------------------------------------------------------------------------------------ generated horse

class Builder:
    """A tiny glTF writer: one binary buffer, accessors and views appended as they are made."""

    def __init__(self, doc=None, binary=b""):
        if doc is not None:
            self.doc, self.bin = doc, binary
            return
        self.doc = {"asset": {"version": "2.0", "generator": "build-unit-models.py (C083)"}, "scene": 0, "scenes": [{"nodes": []}], "nodes": [], "meshes": [], "skins": [],
                    "materials": [], "accessors": [], "bufferViews": [], "buffers": [{"byteLength": 0}], "animations": []}
        self.bin = b""

    def accessor(self, values, kind, component, target=None, bounds=False):
        width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[kind]
        fmt = {5126: "f", 5123: "H", 5121: "B"}[component]
        flat = [x for v in values for x in (v if isinstance(v, (list, tuple)) else [v])]
        chunk = struct.pack("<%d%s" % (len(flat), fmt), *flat)
        view = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(chunk)}
        if target:
            view["target"] = target
        self.bin = pad(self.bin + chunk, b"\0")
        self.doc["bufferViews"].append(view)
        acc = {"bufferView": len(self.doc["bufferViews"]) - 1, "componentType": component, "count": len(values), "type": kind}
        if bounds:
            cols = list(zip(*[v if isinstance(v, (list, tuple)) else [v] for v in values]))
            acc["min"], acc["max"] = [min(c) for c in cols], [max(c) for c in cols]
        self.doc["accessors"].append(acc)
        return len(self.doc["accessors"]) - 1

    def finish(self):
        self.doc["buffers"] = [{"byteLength": len(self.bin)}]
        return self.doc, self.bin


def quat_x(angle):
    return [math.sin(angle / 2), 0.0, 0.0, math.cos(angle / 2)]


def quat_z(angle):
    return [0.0, 0.0, math.sin(angle / 2), math.cos(angle / 2)]


IDENTITY = [0.0, 0.0, 0.0, 1.0]

# joint name -> (parent, world position in the bind pose); the horse faces +Z and stands on y = 0
HORSE_JOINTS = [
    ("root", None, (0.0, 0.0, 0.0)),
    ("body", "root", (0.0, 0.32, 0.0)),
    ("saddle", "body", (0.0, 0.43, 0.0)),
    ("neck", "body", (0.0, 0.40, 0.28)),
    ("head", "neck", (0.0, 0.58, 0.40)),
    ("tail", "body", (0.0, 0.40, -0.26)),
    ("leg_fl", "body", (0.07, 0.26, 0.20)),
    ("leg_fr", "body", (-0.07, 0.26, 0.20)),
    ("leg_bl", "body", (0.07, 0.26, -0.20)),
    ("leg_br", "body", (-0.07, 0.26, -0.20)),
]
# rigid parts: (joint, centre, size)
HORSE_PARTS = [
    ("body", (0.0, 0.32, 0.0), (0.20, 0.20, 0.52)),
    ("neck", (0.0, 0.50, 0.34), (0.09, 0.22, 0.09)),
    ("head", (0.0, 0.62, 0.46), (0.09, 0.10, 0.20)),
    ("tail", (0.0, 0.33, -0.30), (0.05, 0.20, 0.05)),
    ("leg_fl", (0.07, 0.13, 0.20), (0.06, 0.26, 0.06)),
    ("leg_fr", (-0.07, 0.13, 0.20), (0.06, 0.26, 0.06)),
    ("leg_bl", (0.07, 0.13, -0.20), (0.06, 0.26, 0.06)),
    ("leg_br", (-0.07, 0.13, -0.20), (0.06, 0.26, 0.06)),
]
HORSE_COLOUR = [0.45, 0.30, 0.18, 1.0]


def box(centre, size):
    """24 vertices (flat normals) and 36 indices of an axis-aligned box."""
    hx, hy, hz = (s / 2 for s in size)
    positions, normals, indices = [], [], []
    for axis, sign in ((0, 1), (0, -1), (1, 1), (1, -1), (2, 1), (2, -1)):
        u, v = [a for a in range(3) if a != axis]
        base = len(positions)
        for du, dv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            p = [0.0, 0.0, 0.0]
            p[axis] = sign * (hx, hy, hz)[axis]
            p[u] = du * (hx, hy, hz)[u]
            p[v] = dv * (hx, hy, hz)[v]
            positions.append([centre[i] + p[i] for i in range(3)])
            n = [0.0, 0.0, 0.0]
            n[axis] = float(sign)
            normals.append(n)
        order = (0, 1, 2, 0, 2, 3)
        # wind the face counter-clockwise seen from outside: flip for the faces whose (u, v) frame is left-handed
        flip = (sign > 0) != ((axis + 1) % 3 == u)
        indices += [base + i for i in (order if not flip else (0, 2, 1, 0, 3, 2))]
    return positions, normals, indices


def build_horse():
    b = Builder()
    doc = b.doc
    world = {name: pos for name, _, pos in HORSE_JOINTS}
    index = {name: i for i, (name, _, _) in enumerate(HORSE_JOINTS)}
    for name, parent, pos in HORSE_JOINTS:
        node = {"name": name, "translation": [round(pos[i] - (world[parent][i] if parent else 0.0), 5) for i in range(3)]}
        doc["nodes"].append(node)
    for name, parent, _ in HORSE_JOINTS:
        if parent:
            doc["nodes"][index[parent]].setdefault("children", []).append(index[name])

    positions, normals, joints, weights, indices = [], [], [], [], []
    for joint, centre, size in HORSE_PARTS:
        p, n, i = box(centre, size)
        indices += [len(positions) + k for k in i]
        positions += p
        normals += n
        joints += [[index[joint], 0, 0, 0]] * len(p)
        weights += [[1.0, 0.0, 0.0, 0.0]] * len(p)
    acc = {
        "POSITION": b.accessor(positions, "VEC3", 5126, 34962, bounds=True),
        "NORMAL": b.accessor(normals, "VEC3", 5126, 34962),
        "JOINTS_0": b.accessor(joints, "VEC4", 5121, 34962),
        "WEIGHTS_0": b.accessor(weights, "VEC4", 5126, 34962),
    }
    idx = b.accessor(indices, "SCALAR", 5123, 34963)
    inverse = [[1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -world[name][0], -world[name][1], -world[name][2], 1] for name, _, _ in HORSE_JOINTS]
    ibm = b.accessor(inverse, "MAT4", 5126)
    doc["materials"] = [{"name": "Horse", "pbrMetallicRoughness": {"baseColorFactor": HORSE_COLOUR, "metallicFactor": 0.0, "roughnessFactor": 1.0}}]
    doc["meshes"] = [{"name": "Horse", "primitives": [{"attributes": acc, "indices": idx, "material": 0}]}]
    doc["skins"] = [{"name": "HorseRig", "joints": list(range(len(HORSE_JOINTS))), "inverseBindMatrices": ibm, "skeleton": 0}]
    doc["nodes"].append({"name": "HorseMesh", "mesh": 0, "skin": 0})
    doc["scenes"][0]["nodes"] = [0, len(doc["nodes"]) - 1]

    all_tracks = {}

    def clip(name, tracks):
        """tracks: (joint, 'rotation'|'translation', [(time, value)...]); linear interpolation, values in the joint's local frame."""
        samplers, channels = [], []
        for joint, path, keys in tracks:
            times = [t for t, _ in keys]
            values = [v for _, v in keys]
            samplers.append({"input": b.accessor(times, "SCALAR", 5126, bounds=True), "output": b.accessor(values, "VEC4" if path == "rotation" else "VEC3", 5126), "interpolation": "LINEAR"})
            channels.append({"sampler": len(samplers) - 1, "target": {"node": index[joint], "path": path}})
        doc["animations"].append({"name": name, "samplers": samplers, "channels": channels})
        all_tracks[name] = tracks

    def rest(joint):
        return doc["nodes"][index[joint]]["translation"]

    def swing(joint, length, phase, amount):
        return (joint, "rotation", [(0.0, quat_x(phase * amount)), (length / 2, quat_x(-phase * amount)), (length, quat_x(phase * amount))])

    def bob(length, amount):
        y = rest("body")[1]
        return ("body", "translation", [(0.0, [0.0, y, 0.0]), (length / 4, [0.0, y + amount, 0.0]), (length / 2, [0.0, y, 0.0]), (3 * length / 4, [0.0, y + amount, 0.0]), (length, [0.0, y, 0.0])])

    # idle: breathing and a tail flick
    clip("idle", [("neck", "rotation", [(0.0, quat_x(0.0)), (1.0, quat_x(0.05)), (2.0, quat_x(0.0))]), ("tail", "rotation", [(0.0, quat_x(0.0)), (1.0, quat_x(-0.25)), (2.0, quat_x(0.0))])])
    # walk: diagonal pairs swing together
    length = 0.8
    clip("walk", [swing("leg_fl", length, 1, 0.55), swing("leg_br", length, 1, 0.55), swing("leg_fr", length, -1, 0.55), swing("leg_bl", length, -1, 0.55), bob(length, 0.012),
                  ("tail", "rotation", [(0.0, quat_x(-0.1)), (length / 2, quat_x(0.1)), (length, quat_x(-0.1))])])
    # attack: rears up on its hind legs, front legs pawing, and comes down
    y = rest("body")[1]
    clip("attack", [("body", "rotation", [(0.0, quat_x(0.0)), (0.3, quat_x(-0.45)), (0.6, quat_x(-0.45)), (0.9, quat_x(0.0))]),
                    ("body", "translation", [(0.0, [0.0, y, 0.0]), (0.3, [0.0, y + 0.05, -0.04]), (0.6, [0.0, y + 0.05, -0.04]), (0.9, [0.0, y, 0.0])]),
                    ("leg_fl", "rotation", [(0.0, quat_x(0.0)), (0.3, quat_x(0.9)), (0.6, quat_x(0.9)), (0.9, quat_x(0.0))]),
                    ("leg_fr", "rotation", [(0.0, quat_x(0.0)), (0.3, quat_x(0.9)), (0.6, quat_x(0.9)), (0.9, quat_x(0.0))])])
    # attack_ranged: stands, tosses its head at the shot
    clip("attack_ranged", [("neck", "rotation", [(0.0, quat_x(0.0)), (0.4, quat_x(-0.3)), (1.0, quat_x(0.0))]), ("head", "rotation", [(0.0, quat_x(0.0)), (0.4, quat_x(-0.3)), (1.0, quat_x(0.0))])])
    # hit: shoved back, head up
    clip("hit", [("body", "translation", [(0.0, [0.0, y, 0.0]), (0.15, [0.0, y, -0.06]), (0.5, [0.0, y, 0.0])]),
                 ("neck", "rotation", [(0.0, quat_x(0.0)), (0.15, quat_x(-0.35)), (0.5, quat_x(0.0))])])
    # death: falls on its side and stays there (the tail of the clip is a hold)
    clip("death", [("root", "rotation", [(0.0, IDENTITY), (0.9, quat_z(math.pi / 2)), (1.2, quat_z(math.pi / 2))]),
                   ("root", "translation", [(0.0, [0.0, 0.0, 0.0]), (0.9, [0.3, 0.1, 0.0]), (1.2, [0.3, 0.1, 0.0])])])
    return b.finish() + (all_tracks,)


# ------------------------------------------------------------------------------------------------------------------------------------ merge a rider in

def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx, aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]


def qrot(q, v):
    """Rotates vector v by unit quaternion q."""
    r = qmul(qmul(q, [v[0], v[1], v[2], 0.0]), [-q[0], -q[1], -q[2], q[3]])
    return r[:3]


def sample(keys, t, default):
    """Linear sample of [(time, value)...] (clamped at the ends); `default` when there are no keys."""
    if not keys:
        return list(default)
    if t <= keys[0][0]:
        return list(keys[0][1])
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            f = (t - t0) / (t1 - t0)
            return [a + (b - a) * f for a, b in zip(v0, v1)]
    return list(keys[-1][1])


def merge_rider(horse, horse_bin, rider, rider_bin, saddle_height, tracks):
    """
    Appends the rider (its own skeleton, a second root of the scene) to the horse. The rider's 'Rig' node is not parented to a horse joint (glTF importers fuse joint trees into one skeleton,
    which would clash with the rider's own bone names and scale): instead each clip also animates the Rig node along with the horse's root and body joints, so he stays on the saddle.
    Clips of the same name are merged into one.
    """
    node_off, mesh_off, skin_off, acc_off = len(horse["nodes"]), len(horse["meshes"]), len(horse["skins"]), len(horse["accessors"])
    view_off, mat_off, img_off, tex_off, smp_off = len(horse["bufferViews"]), len(horse["materials"]), len(horse.get("images", [])), len(horse.get("textures", [])), len(horse.get("samplers", []))
    byte_off = len(horse_bin)
    for view in rider["bufferViews"]:
        horse["bufferViews"].append(dict(view, byteOffset=view["byteOffset"] + byte_off))
    for a in rider["accessors"]:
        horse["accessors"].append(dict(a, bufferView=a["bufferView"] + view_off))
    for mesh in rider["meshes"]:
        horse["meshes"].append({**mesh, "primitives": [dict(p, attributes={k: v + acc_off for k, v in p["attributes"].items()}, indices=p["indices"] + acc_off, material=p["material"] + mat_off) for p in mesh["primitives"]]})
    horse["materials"] += rider["materials"]
    horse.setdefault("images", [])
    horse.setdefault("samplers", [])
    horse.setdefault("textures", [])
    horse["images"] += [dict(i, bufferView=i["bufferView"] + view_off) for i in rider["images"]]
    horse["samplers"] += rider.get("samplers", [])
    horse["textures"] += [dict(t, source=t["source"] + img_off, **({"sampler": t["sampler"] + smp_off} if "sampler" in t else {})) for t in rider["textures"]]
    for material in horse["materials"][mat_off:]:
        pbr = material.get("pbrMetallicRoughness", {})
        if "baseColorTexture" in pbr:
            pbr["baseColorTexture"] = dict(pbr["baseColorTexture"], index=pbr["baseColorTexture"]["index"] + tex_off)
    for skin in rider["skins"]:
        horse["skins"].append({**skin, "joints": [j + node_off for j in skin["joints"]], "inverseBindMatrices": skin["inverseBindMatrices"] + acc_off, **({"skeleton": skin["skeleton"] + node_off} if "skeleton" in skin else {})})
    for node in rider["nodes"]:
        n = dict(node)
        if "children" in n:
            n["children"] = [c + node_off for c in n["children"]]
        if "mesh" in n:
            n["mesh"] += mesh_off
        if "skin" in n:
            n["skin"] += skin_off
        horse["nodes"].append(n)
    rig = next(i for i, n in enumerate(horse["nodes"]) if i >= node_off and n.get("name") == "Rig")
    rig_rest = [0.0, saddle_height, 0.0]
    horse["nodes"][rig]["translation"] = rig_rest
    horse["scenes"][0]["nodes"].append(rig)

    merged = pad(horse_bin, b"\0") + rider_bin
    b = Builder(horse, merged)
    body_rest = [0.0, 0.32, 0.0]
    by_name = {a["name"]: a for a in horse["animations"]}
    for anim in rider["animations"]:
        target = by_name[anim["name"]]
        samplers = [dict(s, input=s["input"] + acc_off, output=s["output"] + acc_off) for s in anim["samplers"]]
        base = len(target["samplers"])
        target["samplers"] += samplers
        target["channels"] += [{"sampler": c["sampler"] + base, "target": dict(c["target"], node=c["target"]["node"] + node_off)} for c in anim["channels"]]
        # the rider rides: his Rig node follows the root and body joints of the horse
        horse_tracks = tracks[anim["name"]]
        keyed = {(joint, path): keys for joint, path, keys in horse_tracks}
        times = sorted({t for key in (("root", "rotation"), ("root", "translation"), ("body", "rotation"), ("body", "translation")) for t, _ in keyed.get(key, [])})
        if not times:
            continue
        rotations, translations = [], []
        for t in times:
            qr = sample(keyed.get(("root", "rotation")), t, IDENTITY)
            tr = sample(keyed.get(("root", "translation")), t, [0.0, 0.0, 0.0])
            qb = sample(keyed.get(("body", "rotation")), t, IDENTITY)
            tb = sample(keyed.get(("body", "translation")), t, body_rest)
            qw = qmul(qr, qb)
            norm = math.sqrt(sum(c * c for c in qw))
            qw = [c / norm for c in qw]
            pw = [tr[i] + qrot(qr, tb)[i] for i in range(3)]
            offset = qrot(qw, [rig_rest[i] - body_rest[i] for i in range(3)])
            rotations.append((t, qw))
            translations.append((t, [pw[i] + offset[i] for i in range(3)]))
        for path, keys in (("rotation", rotations), ("translation", translations)):
            target["samplers"].append({"input": b.accessor([t for t, _ in keys], "SCALAR", 5126, bounds=True), "output": b.accessor([v for _, v in keys], "VEC4" if path == "rotation" else "VEC3", 5126), "interpolation": "LINEAR"})
            target["channels"].append({"sampler": len(target["samplers"]) - 1, "target": {"node": rig, "path": path}})
    doc, out = b.finish()
    return doc, out


# ----------------------------------------------------------------------------------------------------------------------------------------------- main

def clips_for(data, unit):
    clips = dict(data["clips"])
    clips.update(unit.get("clips", {}))
    return clips


def main(source, pack):
    data = json.load(open(os.path.join(pack, "attachments.json")))
    models = os.path.join(pack, "models", "unit")
    os.makedirs(models, exist_ok=True)
    keys = {}
    for name, unit in data["units"].items():
        doc, binary = read_glb(os.path.join(source, data["bodies"][unit["body"]]))
        doc, binary = reduce_body(doc, binary, set(unit["keep"]), clips_for(data, unit), data["height"])
        path = os.path.join(models, name + ".glb")
        print(f"unit/{name}: {write_glb(path, doc, binary)} bytes")
        keys[f"unit/{name}"] = f"models/unit/{name}.glb"
    mounted = data["mounted"]
    for name, unit in mounted["units"].items():
        rider, rider_bin = read_glb(os.path.join(source, data["bodies"][unit["body"]]))
        clips = clips_for(data, unit)
        rider, rider_bin = reduce_body(rider, rider_bin, set(unit["keep"]), clips, mounted["riderHeight"], seat=mounted["seat"])
        horse, horse_bin, tracks = build_horse()
        horse, merged = merge_rider(horse, horse_bin, rider, rider_bin, mounted["saddleHeight"], tracks)
        path = os.path.join(models, name + ".glb")
        print(f"unit/{name}: {write_glb(path, horse, merged)} bytes (mounted)")
        keys[f"unit/{name}"] = f"models/unit/{name}.glb"
    manifest = {"keys": dict(sorted(keys.items())), "pack": "units"}
    with open(os.path.join(pack, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    if len(sys.argv) not in (2, 3):
        print(__doc__)
        sys.exit(64)
    main(sys.argv[1], sys.argv[2] if len(sys.argv) == 3 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Packs", "units"))
