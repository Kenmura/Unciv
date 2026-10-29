# Unit models and attack animations (plan)

Status: tickets filed as C080-C086 (phase 4, root C080 held Blocked until phases 1-3 are clear; written 2026-10-28). Goal: every unit gets a proper 3D model (eventually high-quality art) and units play
an attack animation when they fight. Builds on the placeholder asset system (CLIENT-SLICES.md, C003/C004) and the combat presenter (C033).

## What exists today

- **Model loading.** `UnitNode.Build` asks `AssetRegistry` for `unit/<Name>`. Fallback chain: `unit/Warrior` -> `unitType/Sword` -> `unit/_default`,
  each tried as `assets/override/<key>.{tres,glb,png,ogg}` then the selected pack's `manifest.json`, then the procedural box (`ModelSpecs.Unit`).
  A `.glb` already loads through this path (`AssetRegistry.LoadFile` instantiates any `PackedScene`): dropping `assets/override/unit/Warrior.glb` works today.
- **Gaps.**
  - Civ colouring only looks for a node named `Banner` (`MapMaterials.ColourBanner`).
  - No contract for scale, height or facing.
  - Units never rotate to face anything.
- **Combat animation.** `CombatPresentation` (pure) turns engine events into timed cues; `CombatPresenter` poses them:
  - melee: the whole node lunges toward the target;
  - ranged: a ball flies to the target;
  - the defender shakes and a damage number floats up;
  - a dead unit shrinks and sinks.

  Nothing ever plays an `AnimationPlayer`. Enabled in `App.cs`; manual stepping exists for goldens and tests.
- **Scale.** Gods & Kings has ~100 units over 19 unit types (18 Sword, 18 Gunpowder, 15 Archery, 15 Civilian, 14 Mounted, then ships, siege, armor,
  aircraft). Land military units show up to 4 figures per tile, so dozens of animated figures can be on screen. The renderer is `gl_compatibility`.

## Design

### 1. Model contract

- **Format:** glTF 2.0 `.glb`, Y-up, facing +Z (glTF default), feet at y = 0.
- **Size:** about 0.7 tile units tall, inside a 0.25 radius at scale 1. `UnitVisuals.Formation` already shrinks figures when there are several.
- **Civ colour:** a material named `TeamColor`, tinted at load; a `Banner` node remains a fallback.
- **Animations:** an optional `AnimationPlayer` with fixed clip names: `idle`, `walk`, `attack`, `attack_ranged`, `hit`, `death`.
  A method-call or marker at the moment of impact or release, so the projectile and the defender's hit line up with the swing.
- **Budget per figure:** about 2-5k triangles, one 512-1024 px texture atlas.
- Runtime-loaded mod packs (not imported by the editor) would need `GLTFDocument` loading; editor-imported packs work as today.

### 2. `UnitAnimator` per figure

Finds the clips and maps each presenter cue to one, falling back to today's procedural pose when the clip is missing:

| Cue | Clip | If the clip is missing |
|---|---|---|
| Move | `walk` | slide |
| Strike | `attack` | lunge |
| Shot | `attack_ranged`, projectile released at the marker | instant shot |
| Hit | `hit` | shake |
| Death | `death` | shrink and sink |

- Figures loop `idle` otherwise. Each figure gets a fixed start offset (derived from its unit id) so a squad doesn't move in lockstep.
- Units turn to face their path or target (new, in `CombatPresenter.Pose`).
- Clip time follows the presenter's clock, not the frame clock, so goldens, manual stepping and the backlog speed-up keep working.
- Placeholders and partial packs never break.

### 3. Re-timing combat cues

- `StrikeSeconds` is 0.25 s, too short for a swing.
- Melee becomes: step forward -> swing -> defender plays `hit` at the impact marker -> step back.
- Timings stay constants in `CombatPresentation` (maybe per unit type); the logic stays pure and tested.

### 4. Articulated placeholder (optional)

- A mannequin with separate nodes: torso, head, and an arm holding a weapon by unit type.
- Its `AnimationPlayer` clips are built in code.
- Gives every unit attack animations before any art exists, and placeholders and real art share one code path.

### 5. Producing ~100 models

- **Humanoids:** one shared humanoid skeleton and one shared animation library (retarget via `SkeletonProfileHumanoid`).
  - Each unit = body mesh + attachments (weapon on the hand bone, shield, helmet), listed in a per-unit data file.
  - About 10 era bodies x weapon sets cover Sword, Gunpowder, Archery and Civilian.
- **Mounted:** rider + horse rig.
- **Ships, siege, armor and aircraft:** rigid models with code-driven motion (recoil, bob, turret turn), no skeleton.
- **Licensing** (ASSETS.md rule): CC0/permissive only, every file recorded, no Firaxis art.
  - Candidate CC0 kits: Quaternius (animated low-poly medieval/fantasy characters), KayKit, Kenney.
  - Avoid Mixamo: free to use, but redistributing its files in an open repo is risky.
- **High-quality art later:** commissioned or consistently styled art arrives as an asset pack with no code change.

### 6. Performance

- Pause idle animations for off-screen or fogged units.
- When zoomed far out, swap to a static pose or a lower-detail model.
- Measure frame time on a full map in the harness before committing to 4 animated figures per tile.

### 7. Verification

- **Contract test:** each `.glb` in a pack is in bounds, has its feet at 0, has a `TeamColor` material and uses valid clip names.
- **Harness scenario:** melee and ranged attacks, asserting the playing clip at each step, plus goldens.
- Every-key-resolves test already exists.

## Suggested ticket order

1. **Loader and contract:** `TeamColor` tinting, finding the `AnimationPlayer`, the contract test. Ship one CC0 animated model as `unit/Warrior`.
2. **`UnitAnimator`, facing and re-timed cues,** with the procedural fallback.
3. **Articulated placeholder** with clips built in code.
4. **Art pipeline:** shared rig, animation library, attachment data, Blender export script, `units` asset pack.
5. **Fill in art by era,** ancient and classical first.
6. **Level of detail and performance caps.**
7. **Polish:** sparks, dust, muzzle flashes, sounds timed to impact (`SoundPlayer`).

## Open decisions (owner)

- **Art source:** CC0 kits (recommended to start: Quaternius/KayKit), commissioned art, or AI-generated models (licences need checking case by case).
- **Figures per tile:** keep up to 4 (Civ 5 style), or show one bigger model per unit (much cheaper art and animation).
- **Placeholder:** build the articulated placeholder (step 3), or go straight to real art.
