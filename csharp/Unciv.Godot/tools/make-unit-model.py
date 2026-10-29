#!/usr/bin/env python3
"""C080: derives the bundled unit model from a KayKit "Adventurers Character Pack 1.0" character (CC0 1.0, Kay Lousberg, https://kaylousberg.com).

    python3 -I Unciv.Godot/tools/make-unit-model.py <Barbarian.glb> <out.glb>

Only the glTF JSON and the binary chunk are rewritten (stdlib, no Blender): the character keeps its rig, one weapon and shield, hat and cape; the 80 source
animations shrink to the six contract clips (idle, walk, attack, attack_ranged, hit, death; see design/UNIT-MODELS.md section 1); the one material is renamed
`TeamColor` (tinted with the civ colour at load); the rig is scaled so the figure stands 0.7 tile units tall with its feet at y = 0.
"""
import json
import struct
import sys

CLIPS = {
    "idle": "Idle",
    "walk": "Walking_A",
    "attack": "1H_Melee_Attack_Slice_Diagonal",
    "attack_ranged": "1H_Ranged_Shoot",
    "hit": "Hit_A",
    "death": "Death_A",
}
DROP_NODES = {"1H_Axe_Offhand", "2H_Axe", "Mug"}  # attachments that would all show at once on the hand slots
HEIGHT = 0.7


def read(path):
    data = open(path, "rb").read()
    jlen = struct.unpack("<I", data[12:16])[0]
    doc = json.loads(data[20:20 + jlen])
    blen = struct.unpack("<I", data[20 + jlen:24 + jlen])[0]
    return doc, data[28 + jlen:28 + jlen + blen]


def pad(b, fill):
    return b + fill * (-len(b) % 4)


def main(src, dst):
    doc, binary = read(src)
    by_name = {a["name"]: a for a in doc["animations"]}
    doc["animations"] = [dict(by_name[source], name=name) for name, source in CLIPS.items()]
    for node in doc["nodes"]:
        if node.get("name") in DROP_NODES:
            node.pop("mesh")
    for node in doc["nodes"]:
        if "children" in node:
            kept = [c for c in node["children"] if doc["nodes"][c].get("name") not in DROP_NODES]
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
    rig["scale"] = [HEIGHT / max(tops)] * 3

    jbytes = pad(json.dumps(doc, separators=(",", ":")).encode(), b" ")
    total = 12 + 8 + len(jbytes) + 8 + len(out)
    with open(dst, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<I4s", len(jbytes), b"JSON") + jbytes)
        f.write(struct.pack("<I4s", len(out), b"BIN\0") + out)
    print(f"{dst}: {total} bytes, scale {rig['scale'][0]:.4f}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(64)
    main(sys.argv[1], sys.argv[2])
