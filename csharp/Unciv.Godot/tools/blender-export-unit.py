"""C083: exports the unit open in Blender as a contract-conforming `.glb` for the `units` pack (design/UNIT-MODELS.md section 1).

    blender --background unit.blend --python Unciv.Godot/tools/blender-export-unit.py -- <out.glb> [--height 0.7]

Blender is not part of this repository's toolchain and CI never runs this script: the contract is enforced on the produced file by `UnitModelContractTests` and the `unit-visuals` scenario.
For the same reason the pack's bundled files come from `build-unit-models.py` (stdlib); this script is the route for new art made in Blender.

What the scene must hold: one armature (the shared humanoid skeleton, bone names of KayKit's Rig, so the shared animation library applies) with the body meshes skinned to it, weapons and other
attachments as plain mesh objects parented to the hand/head bones, and actions named like the contract clips or like the sources listed in `attachments.json` (`Idle`, `Walking_A`, ...).

What it does: renames the body material to `TeamColor`, scales the figure to the target height with its feet on y = 0, keeps only the contract clips (an action named `idle`, `walk`, `attack`,
`attack_ranged`, `hit`, `death`; sources are mapped by CLIP_SOURCES) and exports binary glTF 2.0, Y up, with skinning and animation.
"""
import sys

import bpy  # noqa: provided by Blender

CLIP_SOURCES = {
    "idle": "Idle",
    "walk": "Walking_A",
    "attack": "1H_Melee_Attack_Slice_Diagonal",
    "attack_ranged": "1H_Ranged_Shoot",
    "hit": "Hit_A",
    "death": "Death_A",
}


def main(out, height):
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("no mesh in the scene")
    armature = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)

    # the first material of the body becomes the civ-tinted one
    body = max(meshes, key=lambda o: len(o.data.vertices))
    if body.data.materials:
        body.data.materials[0].name = "TeamColor"

    # scale to the contract height, feet on y = 0 (Blender is Z up; the exporter converts)
    tops = [max((o.matrix_world @ v.co).z for v in o.data.vertices) for o in meshes]
    bottoms = [min((o.matrix_world @ v.co).z for v in o.data.vertices) for o in meshes]
    factor = height / (max(tops) - min(bottoms))
    root = armature or body
    root.scale = [c * factor for c in root.scale]
    root.location.z -= min(bottoms) * factor
    bpy.context.view_layer.update()

    # keep the six contract clips, named as the contract wants
    if armature is not None and armature.animation_data is not None:
        actions = {a.name: a for a in bpy.data.actions}
        for clip, source in CLIP_SOURCES.items():
            action = actions.get(clip) or actions.get(source)
            if action is None:
                print(f"warning: no action for clip '{clip}' (looked for '{clip}' and '{source}')")
                continue
            action.name = clip
            track = armature.animation_data.nla_tracks.new()
            track.name = clip
            track.strips.new(clip, 0, action)
        for action in list(bpy.data.actions):
            if action.name not in CLIP_SOURCES:
                bpy.data.actions.remove(action)

    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_skins=True, export_animations=True,
                              export_animation_mode="NLA_TRACKS", export_apply=False)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        raise SystemExit(__doc__)
    main(args[0], float(args[args.index("--height") + 1]) if "--height" in args else 0.7)
