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

## C083 status: the art pipeline as built

- **Pack:** `Unciv.Godot/Packs/units/` is a second always-on bundled pack (after `unciv`, `AssetResolver.UnitsPack`): `models/unit/*.glb`, `manifest.json`, `CREDITS.md` and `attachments.json`.
- **Shared skeleton and library:** all KayKit Adventurers characters share one humanoid rig, so the six contract clips are cut from one animation library for every body (no per-body retargeting is needed; `SkeletonProfileHumanoid` retargeting stays the route for art with a different rig). `attachments.json` lists per unit the body, the attachments to keep on the hand, head and back (weapon, shield, helmet, hat, cape) and optional clip-source overrides.
- **Build:** `tools/build-unit-models.py <KayKit Characters/gltf>` (stdlib) regenerates the pack. `tools/blender-export-unit.py` is the Blender route for new art; Blender is not in the toolchain, so it is unrun here and the contract tests plus `unit-visuals` are the gate.
- **Mounted:** the rider (own skeleton, scaled to `riderHeight`) sits a generated horse (rigid boxes, 10 joints, six hand-keyed clips). The rider's Rig node follows the horse's root and body joints through generated tracks, his legs and hips hold the seated pose in every clip (a glTF importer would fuse a rider parented to a horse joint into one skeleton).
- **Rigid units:** ships, siege, armour and aircraft keep their generated blocks and move as one piece through `RigidMotion` (pure, tested): ships bob and roll, siege and armour kick back and the turret turns, aircraft bank and dive. `UnitNode.ShowRigid` poses it, `CombatPresenter` drives it from the cue clock.
- **Follow-ups:** real art for ships/siege/armour/aircraft files (the generated blocks stay the fallback); a separate idle bob needs a presenter idle clock; the tracks of a rigid unit that also has a clip model are ignored.


## C084 status: ancient and classical art

- 23 more `unit/*` keys in the units pack, all from `attachments.json` data and `build-unit-models.py`, no code in the client: Settler; the axe infantry Maori Warrior, Jaguar, Brute, Marauder, Pictish Warrior, Mohawk Warrior; the sword and spear infantry Spearman, Hoplite, Persian Immortal, Legion; the throwers and bowmen Bowman, Slinger, Atlatlist, Skirmisher, Composite Bowman; the mounted Chariot Archer, War Chariot, War Elephant, Companion Cavalry, Cataphract, African Forest Elephant (Horse Archer already existed).
- A unique unit gets its own key (the fallback chain goes by unit type, not `replaces`), so each one is a variant of its base: another body, attachments or attack clip. KayKit has no spear or bow, so spearmen use the sword with the stab clip, bowmen the crossbow and throwers the `Throw` clip: placeholders for art that arrives as a pack.
- Elephants are the horse rig with a `mount` entry (`scale` 1.12 and a grey `colour`, limited by the contract's 0.65 radius); a real elephant model replaces them with no code change. A mounted unit's `attack` clip must be about one second (Chop, Slice_Horizontal) or the horse's stretched clip no longer matches the rider's; the infantry may use longer ones (Stab, 2H Chop).
- Not covered, still generated blocks: Trireme, Quinquereme, Dromon, Galley, Work Boats, Catapult, Ballista (code-driven rigid motion) and the Battering Ram (a `Sword` type with no rigid kind: a follow-up gives it a rigid siege kind).
- `UnitModelContractTests.EveryHumanoidAndMountedUnitHasAModel` (C094; was era-limited) fails when a Gods & Kings unit that is not a ship, siege unit, the ram, aircraft, missile, helicopter, armour or spaceship part has no key.
- The chariots (Chariot Archer, War Chariot) are riders on the horse rig: there is no chariot or wheel model yet. Scope: units with a required tech of the ancient or classical era plus the barbarians; Great People, Missionary and Inquisitor (other than the Great Scientist) stay generated blocks.

## C093 status: medieval and renaissance art

- 19 more `unit/*` keys, data only (`attachments.json` plus `build-unit-models.py`, same pipeline as C084): infantry Pikeman, Landsknecht, Samurai, Berserker; bowmen Longbowman, Chu-Ko-Nu; gunpowder Musketman, Musketeer, Janissary, Minuteman, Tercio; mounted Camel Archer (sand-coloured mount), Keshik, Conquistador, Naresuan's Elephant (the grey elephant mount), Mandekalu Cavalry, Lancer, Sipahi, Hakkapeliitta.
- Placeholders as before: KayKit has no musket, pike or bow, so gunpowder units hold the crossbow (distinguished by body, cape and knife) and use the two-handed stab as their melee clip; pikemen use sword, spike shield and the stab clip. Distinct unit art arrives as a pack.
- Still generated blocks: Trebuchet, Hwach'a, Cannon (siege) and all ships, as in C084.
- The roster contract test now covers eras 0 to 3 (and the barbarians). `unit-visuals` adds `medieval` and `medieval-mounts` captures.

## C094 status: industrial, modern and great people

- 25 more `unit/*` keys, data only: Rifleman, Norwegian Ski Infantry, Carolean, Mehal Sefari, Great War Infantry, Foreign Legion, Marine, Paratrooper, Mechanized Infantry; the gun crews Machine Gun, Gatling Gun, Anti-Tank Gun, Anti-Aircraft Gun, Mobile SAM; mounted Cavalry, Cossack, Hussar, Khan; Great Artist, Merchant, Engineer, Prophet, General, Missionary, Inquisitor. Infantry (already in the gallery) now has a model too.
- Placeholders: KayKit has no rifles or machine guns, so riflemen use the crossbow with the two-handed stab, crews of the heavier guns the mage with a staff and the spellcast clip.
- Ships, aircraft, missiles, helicopters, armour and spaceship parts stay generated blocks with code-driven rigid motion (`RigidMotion`): KayKit has no such models, and a new (CC0) kit or commissioned art arrives as a pack with no code change.
